using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace AiAgent.Backend.Services.Ontology;

public sealed record OntologySource(string Id, string Label, OntologyConnection Connection,
    string Relation = "", string KeyColumn = "", string ValueColumn = "", string? ProbeKey = null, string? Note = null);
public sealed record StoredOntologySource(OntologySource Definition, string ProtectedPassword, DateTimeOffset CheckedAt);
public sealed record SourceRequest(int Version, OntologySource Source, string? Password);
public sealed record LookupRequest(int Version, string[] SourceIds, string? Key);

public sealed class OntologySources(OntologyStore store, IConfiguration config, IHttpClientFactory? clients = null)
{
    private static bool Identifier(string? value) => value != null && Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]{0,62}\z");

    // New components can connect to public literals directly. Named and internal targets
    // use the project's configured endpoints so the driver never re-resolves an unchecked host.
    private static readonly IPNetwork[] NonPublicV4 =
    [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"), IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("192.0.2.0/24"), IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"), IPNetwork.Parse("198.18.0.0/15"), IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"), IPNetwork.Parse("224.0.0.0/4"), IPNetwork.Parse("240.0.0.0/4")
    ];
    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork[] SpecialV6 =
    [
        IPNetwork.Parse("2001::/23"), IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"), IPNetwork.Parse("3fff::/20")
    ];

    private static bool IsPublicLiteral(string host)
    {
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return ip.ToString() == host && !NonPublicV4.Any(range => range.Contains(ip));
        return ip.ScopeId == 0 && GlobalV6.Contains(ip) && !SpecialV6.Any(range => range.Contains(ip));
    }

    internal DbConnection OpenTarget(long project, OntologyConnection c, string password)
    {
        if (c.Type == "sqlite")
        {
            if (!Identifier(c.Database)) throw new ArgumentException("请选择已登记的 SQLite 库别名。");
            var file = config[$"Ontology:Projects:{project}:Sqlite:{c.Database}"];
            if (string.IsNullOrEmpty(file) || !Path.IsPathFullyQualified(file)) throw new ArgumentException("项目尚未登记此 SQLite 库。");
            return new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        }
        if (Uri.CheckHostName(c.Host) == UriHostNameType.Unknown || c.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(c.Database) || string.IsNullOrWhiteSpace(c.Username))
            throw new ArgumentException("请填写有效的数据库主机、端口、库名和账号。");
        var target = $"{c.Type}/{c.Host}:{c.Port}/{c.Database}";
        var projectSettings = config.GetSection($"Ontology:Projects:{project}");
        var targets = projectSettings.GetChildren().FirstOrDefault(section => section.Key.Equals("Targets", StringComparison.OrdinalIgnoreCase));
        if (targets != null)
        {
            if (!targets.GetChildren().Any(item => string.Equals(item.Value, target, StringComparison.Ordinal)))
                throw new ArgumentException("当前项目限制了可连接的数据库，请选择项目清单中的目标，或联系管理员添加该地址。");
        }
        else if (!IsPublicLiteral(c.Host))
            throw new ArgumentException("可直接测试公网 IP。使用域名或内网地址时，请先在项目中配置该数据库目标。");
        if (c.Type == "oracle")
        {
            if (!Regex.IsMatch(c.Database, @"\A[A-Za-z0-9_.$-]{1,128}\z")) throw new ArgumentException("Oracle 服务名无效。");
            var host = Uri.CheckHostName(c.Host) == UriHostNameType.IPv6 ? $"[{c.Host.Trim('[', ']')}]" : c.Host;
            return new OracleConnection(new OracleConnectionStringBuilder {
                DataSource = $"tcps://{host}:{c.Port}/{c.Database}?ssl_server_dn_match=yes&connect_timeout=5",
                UserID = c.Username, Password = password, Pooling = false
            }.ConnectionString);
        }
        return c.Type switch
        {
            "sqlserver" => new SqlConnection(new SqlConnectionStringBuilder { DataSource = $"tcp:{c.Host},{c.Port}", InitialCatalog = c.Database, UserID = c.Username, Password = password, Encrypt = true, TrustServerCertificate = false, ConnectTimeout = 5, Pooling = false }.ConnectionString),
            "mysql" => new MySqlConnection(new MySqlConnectionStringBuilder { Server = c.Host, Port = (uint)c.Port, Database = c.Database, UserID = c.Username, Password = password, SslMode = MySqlSslMode.VerifyFull, ConnectionTimeout = 5, Pooling = false }.ConnectionString),
            "postgresql" => new NpgsqlConnection(new NpgsqlConnectionStringBuilder { Host = c.Host, Port = c.Port, Database = c.Database, Username = c.Username, Password = password, SslMode = SslMode.Require, TrustServerCertificate = false, Timeout = 5, Pooling = false }.ConnectionString),
            _ => throw new ArgumentException("不支持此数据库驱动。")
        };
    }

    internal static (string Metadata, string Lookup) SelectStatements(OntologySource source)
    {
        var parts = source.Relation?.Split('.') ?? [];
        if (source.Connection == null || parts.Length is < 1 or > 2 || !parts.All(Identifier) || !Identifier(source.KeyColumn) || !Identifier(source.ValueColumn))
            throw new ArgumentException("请填写表名、查询键及返回字段；表名可含 schema。");
        var driver = source.Connection.Type;
        string Quote(string name) => driver switch
        {
            "oracle" => $"\"{name.ToUpperInvariant()}\"",
            "mysql" => $"`{name}`",
            "sqlserver" => $"[{name}]",
            _ => $"\"{name}\""
        };
        var fields = $"{Quote(source.KeyColumn)}, {Quote(source.ValueColumn)}";
        var table = string.Join(".", parts.Select(Quote));
        var metadata = $"SELECT {fields} FROM {table} WHERE 1=0";
        var lookup = driver == "oracle" ? $"SELECT {fields} FROM {table} WHERE {Quote(source.KeyColumn)} = :lookup_key AND ROWNUM <= 2"
                : $"SELECT {(driver == "sqlserver" ? "TOP (2) " : "")}{fields} FROM {table} WHERE {Quote(source.KeyColumn)} = @key{(driver == "sqlserver" ? "" : " LIMIT 2")}";
        return (metadata, lookup);
    }

    private static bool HasMapping(OntologySource source) =>
        !string.IsNullOrWhiteSpace(source.Relation) || !string.IsNullOrWhiteSpace(source.KeyColumn) || !string.IsNullOrWhiteSpace(source.ValueColumn);

    private async Task<string?> Read(long project, OntologySource source, string password, string? key, CancellationToken token, bool connectionOnly = false)
    {
        if (source.Connection?.Type == "http")
            return await ReadHttp(project, source, password, key, token);
        var driver = source.Connection!.Type;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var db = OpenTarget(project, source.Connection, password);
            await db.OpenAsync(deadline.Token);
            await using var cmd = db.CreateCommand();
            cmd.CommandTimeout = 5;
            if (connectionOnly)
            {
                cmd.CommandText = driver == "oracle" ? "SELECT 1 FROM DUAL" : "SELECT 1";
                await cmd.ExecuteScalarAsync(deadline.Token);
                return null;
            }
            var sql = SelectStatements(source);
            cmd.CommandText = key == null ? sql.Metadata : sql.Lookup;
            if (key != null)
            {
                // Bind numeric and UUID keys using the provider's declared column type.
                await using var metadata = db.CreateCommand();
                metadata.CommandTimeout = 5;
                metadata.CommandText = sql.Metadata;
                await using var columns = await metadata.ExecuteReaderAsync(deadline.Token);
                var type = columns.GetFieldType(0);
                var param = cmd.CreateParameter();
                param.ParameterName = driver == "oracle" ? "lookup_key" : "@key";
                try
                {
                    param.Value = type == typeof(Guid) ? Guid.Parse(key) :
                        type == typeof(object) ? key : Convert.ChangeType(key, type, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException)
                { throw new ArgumentException("记录键值与查询键的字段类型不匹配。"); }
                cmd.Parameters.Add(param);
            }
            await using var reader = await cmd.ExecuteReaderAsync(deadline.Token);
            if (key == null) return null;
            if (!await reader.ReadAsync(deadline.Token)) return "未找到匹配记录";
            var text = reader.IsDBNull(1) ? "NULL" : reader.GetFieldType(1) == typeof(byte[]) ? "（二进制字段）" :
                Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (await reader.ReadAsync(deadline.Token)) return "查询键匹配多条记录，请配置唯一查询键";
            return JsonSerializer.Serialize(new Dictionary<string,string> { [source.ValueColumn] = text.Length > 1500 ? text[..1500] + "…" : text });
        }
        catch (DbException) when (connectionOnly) { throw new ArgumentException("数据库连接失败，请核对地址、端口、库名、账号密码和 TLS 配置。"); }
        catch (TimeoutException) { throw new ArgumentException("数据库请求超时。"); }
        catch (DbException) { throw new ArgumentException("数据库读取失败，请检查查询配置及账号的读取权限。"); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new ArgumentException("数据库请求超时。"); }
    }

    private async Task<string?> ReadHttp(long project, OntologySource source, string secret, string? key, CancellationToken token)
    {
        if (!Identifier(source.Relation) || !Identifier(source.KeyColumn) || !Identifier(source.ValueColumn))
            throw new ArgumentException("请填写已登记的接口别名与 JSON 字段名。");
        var template = config[$"Ontology:Projects:{project}:Http:{source.Relation}"];
        var record = key ?? source.ProbeKey;
        if (string.IsNullOrWhiteSpace(record) || record.Length > 256 || record is "." or "..")
            throw new ArgumentException("请填写有效的测试记录键值。");
        if (template == null || !template.Contains("{key}", StringComparison.Ordinal) ||
            !Uri.TryCreate(template.Replace("{key}", Uri.EscapeDataString(record)), UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.UserInfo.Length != 0 || template.IndexOf("{key}", StringComparison.Ordinal) < uri.GetLeftPart(UriPartial.Authority).Length)
            throw new ArgumentException("当前项目尚未登记此 HTTPS GET 操作。");
        using var client = clients!.CreateClient("ontology-readonly");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (secret.Length > 0) request.Headers.Authorization = new("Bearer", secret);
        try
        {
            using var response = await client.SendAsync(request, token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound && key != null) return "未找到匹配记录";
            if (!response.IsSuccessStatusCode) throw new ArgumentException($"只读接口返回 HTTP {(int)response.StatusCode}。");
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
            var row = json.RootElement;
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(source.KeyColumn, out var id) || id.ToString() != record ||
                !row.TryGetProperty(source.ValueColumn, out var value) || value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                throw new ArgumentException("接口结果与配置的记录键或标量返回字段不匹配。");
            var text = value.ToString();
            return JsonSerializer.Serialize(new Dictionary<string,string> { [source.ValueColumn] = text.Length > 1500 ? text[..1500] + "…" : text });
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !token.IsCancellationRequested)
        { throw new ArgumentException("接口读取失败，请检查服务、响应格式和访问权限。"); }
    }

    public async Task<object> Verify(string user, long project, string id, SourceRequest request, bool save, CancellationToken token)
    {
        var model = store.Get(user, project, id);
        if (model.Version != request.Version) throw new InvalidOperationException("模型版本已变化，请重新加载。");
        var source = request.Source;
        if (source == null || source.Connection == null || !Guid.TryParseExact(source.Id, "N", out _) || string.IsNullOrWhiteSpace(source.Label) ||
            source.Label.Length > 80 || request.Password?.Length > 4096) throw new ArgumentException("组件配置无效。");
        var previous = model.Sources.FirstOrDefault(x => x.Definition.Id == source.Id);
        if (source.Connection.Type == "note")
        {
            if (string.IsNullOrWhiteSpace(source.Note) || source.Note.Length > 4000)
                throw new ArgumentException("请填写 1–4000 字符的说明文本。");
            var note = new OntologySource(source.Id, source.Label, new("note"), "", "", "", Note: source.Note.Trim());
            return save ? store.SaveSource(user, project, id, request.Version, note, "", DateTimeOffset.UtcNow) : new { checkedAt = DateTimeOffset.UtcNow };
        }
        var password = request.Password;
        if (string.IsNullOrEmpty(password) && previous?.Definition.Connection == source.Connection &&
            (source.Connection.Type != "http" || previous.Definition.Relation == source.Relation))
            password = store.ReadPassword(previous);
        var connectionOnly = source.Connection.Type != "http" && (!save || !HasMapping(source));
        await Read(project, source, password ?? "", null, token, connectionOnly);
        var checkedAt = DateTimeOffset.UtcNow;
        return save ? store.SaveSource(user, project, id, request.Version, source, password ?? "", checkedAt) : new { checkedAt };
    }

    public async Task<object> Lookup(string user, long project, string id, LookupRequest request, CancellationToken token)
    {
        var model = store.Get(user, project, id);
        if (model.Version != request.Version) throw new InvalidOperationException("模型版本已变化，请重新加载。");
        if (request.SourceIds == null || request.SourceIds.Length is < 1 or > 8 || request.Key?.Length > 256) throw new ArgumentException("请选择 1–8 个组件并核对查询键。");
        var lines = new List<string> { $"项目本体模型 {model.Name} · ontology://{project}/{id}@{model.Version}" };
        var key = string.IsNullOrWhiteSpace(request.Key) ? null : request.Key.Trim();
        var readRecord = false;
        foreach (var sourceId in request.SourceIds.Distinct())
        {
            var entry = model.Sources.FirstOrDefault(s => s.Definition.Id == sourceId) ?? throw new ArgumentException("组件已变更，请重新选择。");
            var source = entry.Definition;
            if (source.Connection.Type == "note")
            {
                lines.Add($"模型说明 · {source.Label}\n{source.Note}");
                continue;
            }
            if (source.Connection.Type != "http" && !HasMapping(source))
            {
                lines.Add($"{source.Label} · {source.Connection.Type} 数据库连接 · 未配置按键查询");
                continue;
            }
            lines.Add($"{source.Label} · {source.Relation} · 查询键 {source.KeyColumn} · 返回字段 {source.ValueColumn}");
            if (key != null)
            {
                var result = await Read(project, source, store.ReadPassword(entry), key, token);
                readRecord = true;
                lines.Add($"读取时间 {DateTimeOffset.UtcNow:O} · 键值 {JsonSerializer.Serialize(key)} · 数据 {result}");
            }
        }
        if (readRecord) lines.Add("以上为本次只读查询的结果。");
        else lines.Add("本轮附加模型说明，尚未读取业务记录。");
        return new { text = string.Join("\n", lines), version = model.Version };
    }
}
