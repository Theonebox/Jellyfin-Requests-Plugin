namespace Jellyfin.Plugin.Requests.Models;

public sealed class MediaRequest
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string MediaId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    public string PosterUrl { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string SourceProvider { get; set; } = string.Empty;

    public int? ProductionYear { get; set; }

    public MediaCategory MediaCategory { get; set; } = MediaCategory.All;

    public RequestScope RequestScope { get; set; } = RequestScope.EntireMovie;

    public List<int> RequestedSeasonNumbers { get; set; } = new();

    public List<string> RequestedEpisodeIds { get; set; } = new();

    public List<string> RequestedEpisodeNames { get; set; } = new();

    /// <summary>The requester's note.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>An administrator's reply shown to the requester (e.g. why it was denied).</summary>
    public string AdminNote { get; set; } = string.Empty;

    public string MediaQuality { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; }

    /// <summary>The <see cref="StatusOption.Id"/> of the current status.</summary>
    public string Status { get; set; } = string.Empty;

    public List<RequestStatusChange> StatusHistory { get; set; } = new();

    public MediaRequest ShallowCopy() => (MediaRequest)MemberwiseClone();
}
