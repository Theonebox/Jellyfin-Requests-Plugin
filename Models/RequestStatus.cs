namespace Jellyfin.Plugin.Requests.Models;

public enum RequestStatus
{
    Pending,
    Approved,
    Denied,
    InProgress,
    Available,
    Completed,
    Cancelled
}
