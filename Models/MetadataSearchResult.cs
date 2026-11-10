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

    [JsonPropertyName("mediaCategory")]
    public MediaCategory MediaCategory { get; set; } = MediaCategory.Movies;

    /// <summary>True when a movie/series with one of these provider IDs already exists in the library.</summary>
    [JsonPropertyName("inLibrary")]
    public bool InLibrary { get; set; }

    /// <summary>Status of an existing request for this title by any user, if there is one.</summary>
    [JsonPropertyName("requestStatus")]
    public string? RequestStatus { get; set; }
}
