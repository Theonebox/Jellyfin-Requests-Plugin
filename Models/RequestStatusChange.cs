namespace Jellyfin.Plugin.Requests.Models;

public sealed class RequestStatusChange
{
    public RequestStatus Status { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
