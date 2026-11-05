namespace Jellyfin.Plugin.Requests.Models;

public sealed class TmdbSearchResult
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    public string PosterPath { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public DateTime? ReleaseDate { get; set; }
}
