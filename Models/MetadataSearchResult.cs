using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Requests.Models;

public sealed class MetadataSearchResult
{
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("posterUrl")]
    public string PosterUrl { get; set; } = string.Empty;

    [JsonPropertyName("productionYear")]
    public int? ProductionYear { get; set; }

    [JsonPropertyName("sourceProvider")]
    public string SourceProvider { get; set; } = string.Empty;

    [JsonPropertyName("providerIds")]
    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public MediaCategory MediaCategory { get; set; } = MediaCategory.Movies;
}
