using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Requests.Models;

public sealed class MediaRequest
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string MediaId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;
}
