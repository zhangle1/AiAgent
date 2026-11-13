using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace AiAgent.Backend.Services.Ontology;

public sealed record OntologyNode(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("label")] string Label);
public sealed record OntologyEdge(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("target")] string Target);
public sealed record OntologyGraph(
    [property: JsonPropertyName("nodes")] List<OntologyNode> Nodes,
    [property: JsonPropertyName("edges")] List<OntologyEdge> Edges);
public sealed record OntologyConnection(
    [property: JsonPropertyName("type")] string Type = "sqlserver",
    [property: JsonPropertyName("host")] string Host = "",
    [property: JsonPropertyName("port")] int Port = 1433,
    [property: JsonPropertyName("database")] string Database = "",
    [property: JsonPropertyName("username")] string Username = "");
public sealed class OntologyModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public OntologyConnection Connection { get; set; } = new();
    public string? ProtectedPassword { get; set; }
    public OntologyGraph Graph { get; set; } = new([], []);
}
public sealed class OntologyStore(IHostEnvironment environment, IDataProtectionProvider protection)
{
    private readonly object gate = new();
    private readonly IDataProtector protector = protection.CreateProtector("AiAgent.Ontology.v1");
    public static readonly string[] Kinds = ["source", "transform", "output"];
    public static object Plugins => Kinds.Select(kind => new { id = kind, label = kind switch { "source" => "MES 数据源", "transform" => "字段映射", _ => "聊天输出" }, version = 1 });
    private string DirectoryFor(string user, long project) => Path.Combine(environment.ContentRootPath, "data", "ontology", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user))), project.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private string FileFor(string user, long project, string id)
    {
        if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("模型编号无效。");
        return Path.Combine(DirectoryFor(user, project), id + ".json");
    }
    private OntologyModel Read(string file) => JsonSerializer.Deserialize<OntologyModel>(protector.Unprotect(File.ReadAllText(file)))!;
    public object View(OntologyModel m) => new { id = m.Id, name = m.Name, version = m.Version, connection = m.Connection, has_password = m.ProtectedPassword != null, graph = m.Graph };
    public object List(string user, long project)
    {
        lock (gate) { var directory = DirectoryFor(user, project); return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json").Select(Read).Select(m => new { id = m.Id, name = m.Name }).ToArray() : []; }
    }
    public OntologyModel Get(string user, long project, string id)
    {
        lock (gate) { var file = FileFor(user, project, id); if (!File.Exists(file)) throw new KeyNotFoundException("模型不存在。"); return Read(file); }
    }
    private void Write(string user, long project, OntologyModel model)
    {
        var file = FileFor(user, project, model.Id); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, protector.Protect(JsonSerializer.Serialize(model))); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public object Create(string user, long project, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("模型名称应为 1–100 字符。");
        lock (gate) { var model = new OntologyModel { Name = name.Trim() }; Write(user, project, model); return View(model); }
    }
    public object Update(string user, long project, string id, int version, OntologyGraph? graph, OntologyConnection? connection, string? password)
    {
        lock (gate)
        {
            var model = Get(user, project, id);
            if (model.Version != version) throw new InvalidOperationException("模型已更新，请重新加载后编辑。");
            if (graph != null) { Validate(graph); model.Graph = graph; }
            if (connection != null)
            {
                if (!new[] { "sqlserver", "mysql", "postgresql", "oracle" }.Contains(connection.Type) || connection.Port is < 1 or > 65535 || new[] { connection.Host, connection.Database, connection.Username }.Any(x => x == null || x.Length > 256) || password?.Length > 4096) throw new ArgumentException("连接配置无效。");
                model.Connection = connection;
                if (!string.IsNullOrEmpty(password)) model.ProtectedPassword = protector.Protect(password);
            }
            model.Version++; Write(user, project, model); return View(model);
        }
    }
    public static void Validate(OntologyGraph graph)
    {
        if (graph.Nodes == null || graph.Edges == null || graph.Nodes.Count > 40 || graph.Edges.Count > 100 || graph.Nodes.Any(n => n == null || string.IsNullOrWhiteSpace(n.Id) || n.Id.Length > 64 || !Kinds.Contains(n.Kind) || string.IsNullOrWhiteSpace(n.Label) || n.Label.Length > 100)) throw new ArgumentException("流程节点无效。");
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet();
        if (ids.Count != graph.Nodes.Count || graph.Edges.Any(e => e == null || !ids.Contains(e.Source) || !ids.Contains(e.Target)) || graph.Edges.Distinct().Count() != graph.Edges.Count) throw new ArgumentException("流程引用或编号无效。");
        var visited = new HashSet<string>(); var active = new HashSet<string>();
        bool Visit(string id) { if (active.Contains(id)) return false; if (!visited.Add(id)) return true; active.Add(id); foreach (var edge in graph.Edges.Where(e => e.Source == id)) if (!Visit(edge.Target)) return false; active.Remove(id); return true; }
        if (ids.Any(id => !Visit(id))) throw new ArgumentException("流程不能包含循环。");
    }
    public object Context(string user, long project, string id)
    {
        var model = Get(user, project, id);
        return new { text = $"本体模型上下文（用户配置，非执行证据）\n项目：{project}\n模型：{model.Name}\n引用：ontology://{project}/{id}@{model.Version}\n数据库类型：{model.Connection.Type}\n流程：{JsonSerializer.Serialize(model.Graph)}\n连接尚未验证；流程仅为定义。需要访问数据时使用已授权连接能力，不从提示词获取凭据。", version = model.Version };
    }
}
