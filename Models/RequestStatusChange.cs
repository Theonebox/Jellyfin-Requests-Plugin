namespace Jellyfin.Plugin.Requests.Models;

public sealed class RequestStatusChange
{
    /// <summary>The <see cref="StatusOption.Id"/> the request moved to.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; }
}
