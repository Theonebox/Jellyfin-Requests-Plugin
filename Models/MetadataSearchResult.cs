namespace Jellyfin.Plugin.Requests.Models;

public sealed class MetadataSearchResult
{
    public string ProviderId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public string PosterUrl { get; set; } = string.Empty;

    public int? ProductionYear { get; set; }

    public string SourceProvider { get; set; } = string.Empty;

    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
