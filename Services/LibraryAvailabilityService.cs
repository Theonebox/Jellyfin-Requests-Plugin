using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class LibraryAvailabilityService
{
    private readonly ILibraryManager _libraryManager;
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _providerIds = new(StringComparer.OrdinalIgnoreCase);

    public LibraryAvailabilityService(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public void Refresh()
    {
        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery { Recursive = true }))
        {
            if (item.ProviderIds is null)
            {
                continue;
            }

            foreach (var providerId in item.ProviderIds.Values)
            {
                if (!string.IsNullOrWhiteSpace(providerId))
                {
                    providerIds.Add(providerId.Trim());
                }
            }
        }

        lock (_syncRoot)
        {
            _providerIds.Clear();
            foreach (var providerId in providerIds)
            {
                _providerIds.Add(providerId);
            }

        }
    }

    public bool IsInLibrary(IEnumerable<string>? providerIds)
    {
        lock (_syncRoot)
        {
            foreach (var providerId in providerIds ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(providerId) && _providerIds.Contains(providerId.Trim()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
