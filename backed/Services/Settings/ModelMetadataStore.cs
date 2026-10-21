using System.Text.Json.Nodes;

namespace AiAgent.Backend.Services.Settings;

/// <summary>Public model metadata only. Credentials remain in the existing profile store.</summary>
public sealed class ModelMetadataStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private JsonObject? _catalog;

    public ModelMetadataStore(IHostEnvironment environment)
        => _path = Path.Combine(environment.ContentRootPath, "data", "models", "model.json");

    public string CatalogPath => "data/models/model.json";

    public void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_catalog is not null) return;
            if (File.Exists(_path))
                _catalog = JsonNode.Parse(File.ReadAllText(_path))?.AsObject()
                    ?? throw new InvalidDataException("The local model.json catalog is invalid.");
            else
            {
                using var seed = typeof(ModelMetadataStore).Assembly.GetManifestResourceStream("AiAgent.Models.models.dev.json")
                    ?? throw new InvalidOperationException("The bundled model metadata is missing.");
                var candidate = JsonNode.Parse(seed)?.AsObject() ?? throw new InvalidDataException("Invalid bundled model metadata.");
                WriteAtomic(candidate);
                _catalog = candidate;
            }
        }
    }

    public JsonObject? Find(string provider, string modelId)
    {
        EnsureLoaded();
        lock (_gate)
        {
            var key = provider.ToLowerInvariant() switch { "gemini" => "google", "claude" => "anthropic", _ => provider.ToLowerInvariant() };
            if (_catalog![key]?["models"] is JsonObject models && models[modelId] is JsonObject exact)
                return (JsonObject)exact.DeepClone();
            var candidates = _catalog.Select(entry => entry.Value?["models"]?[modelId]).OfType<JsonObject>().ToList();
            // A custom gateway's wire id is not enough to guess between publishers.
            return candidates.Count == 1 ? (JsonObject)candidates[0].DeepClone() : null;
        }
    }

    private void WriteAtomic(JsonObject catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, catalog.ToJsonString());
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
