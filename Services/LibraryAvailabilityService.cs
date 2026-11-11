using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Requests.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Requests.Services;

/// <summary>
/// In-memory index of the provider ids of every movie and series in the library, used to mark
/// search results "In library" and to recognise different search results as the same title.
/// Keys are type- and provider-qualified ("movie|tmdb:123"), so a TMDb id can't match an
/// unrelated TVDB id and a movie can't match a series that shares a TMDb number.
/// Built by <see cref="LibraryAvailabilityTask"/> and kept current from library events.
/// </summary>
public sealed class LibraryAvailabilityService
{
    private readonly ILibraryManager _libraryManager;
    private readonly object _syncRoot = new();
    private Dictionary<string, Guid> _itemByKey = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<Guid, Entry> _entries = new();

    public LibraryAvailabilityService(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;

        // New items usually get their provider ids a moment after being added (identification),
        // so updates matter as much as additions.
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemRemoved;
    }

    public void Refresh()
    {
        var itemByKey = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<Guid, Entry>();
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series }
        };

        foreach (var item in _libraryManager.GetItemList(query))
        {
            Index(item, itemByKey, entries);
        }

        lock (_syncRoot)
        {
            _itemByKey = itemByKey;
            _entries = entries;
        }
    }

    /// <summary>The library movie/series that has any of these provider ids, if any.</summary>
    public Guid? FindItem(MediaCategory category, IReadOnlyDictionary<string, string> providerIds)
    {
        var isSeries = category == MediaCategory.TVShows;
        lock (_syncRoot)
        {
            foreach (var (provider, id) in providerIds)
            {
                if (!string.IsNullOrWhiteSpace(id) && _itemByKey.TryGetValue(Key(isSeries, provider, id), out var itemId))
                {
                    return itemId;
                }
            }

            return null;
        }
    }

    /// <summary>All provider ids the library knows for an item (e.g. both Tmdb and Imdb).</summary>
    public IReadOnlyDictionary<string, string>? GetProviderIds(Guid itemId)
    {
        lock (_syncRoot)
        {
            return _entries.TryGetValue(itemId, out var entry) ? new Dictionary<string, string>(entry.Ids) : null;
        }
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie or Series)
        {
            lock (_syncRoot)
            {
                RemoveLocked(e.Item.Id);
                Index(e.Item, _itemByKey, _entries);
            }
        }
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie or Series)
        {
            lock (_syncRoot)
            {
                RemoveLocked(e.Item.Id);
            }
        }
    }

    private void RemoveLocked(Guid itemId)
    {
        if (_entries.Remove(itemId, out var entry))
        {
            foreach (var (provider, id) in entry.Ids)
            {
                _itemByKey.Remove(Key(entry.IsSeries, provider, id));
            }
        }
    }

    private static void Index(BaseItem item, Dictionary<string, Guid> itemByKey, Dictionary<Guid, Entry> entries)
    {
        var ids = (item.ProviderIds ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            return;
        }

        var entry = new Entry(item is Series, ids);
        entries[item.Id] = entry;
        foreach (var (provider, id) in ids)
        {
            itemByKey[Key(entry.IsSeries, provider, id)] = item.Id;
        }
    }

    private static string Key(bool isSeries, string provider, string id)
        => (isSeries ? "tv|" : "movie|") + provider.Trim() + ":" + id.Trim();

    private sealed record Entry(bool IsSeries, Dictionary<string, string> Ids);
}
