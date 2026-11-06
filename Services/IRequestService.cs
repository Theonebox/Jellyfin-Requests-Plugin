using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

public interface IRequestService
{
    IReadOnlyList<MediaRequest> GetAll();
    MediaRequest? GetById(Guid id);
    MediaRequest Add(MediaRequest request);
    MediaRequest? Update(MediaRequest update);
    MediaRequest? UpdateStatus(Guid id, RequestStatus status);
    bool Delete(Guid id);
    bool IsDuplicate(MediaRequest request);
}
