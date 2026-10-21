using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.KunBuddy;

public sealed class KunBuddySessionUpload
{
    [JsonPropertyName("account")] public string Account { get; set; } = "";
    [JsonPropertyName("client_id")] public string ClientId { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("entries")] public List<KunBuddySessionEntry> Entries { get; set; } = [];
}

public sealed class KunBuddySessionEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("markdown")] public string Markdown { get; set; } = "";
}

public sealed class KunBuddySessionReceipt
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";
    [JsonPropertyName("document_id")] public long DocumentId { get; set; }
    [JsonPropertyName("appended")] public int Appended { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}
