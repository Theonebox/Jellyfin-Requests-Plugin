using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
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

        // Keep the cache current between scheduled refreshes.
        _libraryManager.ItemAdded += OnItemAdded;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        var item = e.Item;
        if (item is not (Movie or Series) || item.ProviderIds is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            foreach (var providerId in item.ProviderIds.Values)
            {
                if (!string.IsNullOrWhiteSpace(providerId))
                {
                    _providerIds.Add(providerId.Trim());
                }
            }
        }
    }

    public void Refresh()
    {
        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only movies and series can be requested; scanning episodes, people, etc. is
        // slow and lets unrelated IDs mark search results as "In Library".
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series }
        };

        foreach (var item in _libraryManager.GetItemList(query))
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
