using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class MetadataIdentifyService
{
    private readonly IProviderManager _providerManager;
    private readonly LibraryAvailabilityService _library;

    public MetadataIdentifyService(IProviderManager providerManager, LibraryAvailabilityService library)
    {
        _providerManager = providerManager;
        _library = library;
    }

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(
        string query,
        MediaCategory category = MediaCategory.All,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        var searchMovies = category is MediaCategory.All or MediaCategory.Movies;
        var searchTv = category is MediaCategory.All or MediaCategory.TVShows;

        Task<IEnumerable<RemoteSearchResult>> movieTask = searchMovies
            ? _providerManager.GetRemoteSearchResults<Movie, MovieInfo>(
            new RemoteSearchQuery<MovieInfo>
            {
                SearchInfo = new MovieInfo { Name = query.Trim() }
            },
            cancellationToken)
            : Task.FromResult<IEnumerable<RemoteSearchResult>>(Array.Empty<RemoteSearchResult>());

        Task<IEnumerable<RemoteSearchResult>> tvTask = searchTv
            ? _providerManager.GetRemoteSearchResults<Series, SeriesInfo>(
            new RemoteSearchQuery<SeriesInfo>
            {
                SearchInfo = new SeriesInfo { Name = query.Trim() }
            },
            cancellationToken)
            : Task.FromResult<IEnumerable<RemoteSearchResult>>(Array.Empty<RemoteSearchResult>());

        await Task.WhenAll(movieTask, tvTask).ConfigureAwait(false);

        var results = new List<MetadataSearchResult>();
        if (searchMovies)
        {
            AppendResults(results, movieTask.Result, "Movie", MediaCategory.Movies);
        }
        if (searchTv)
        {
            AppendResults(results, tvTask.Result, "TV", MediaCategory.TVShows);
        }

        return MergeDuplicates(results, _library);
    }

    /// <summary>
    /// Different metadata providers return the same title separately (e.g. TheMovieDb returns only
    /// a Tmdb id, OMDb only an Imdb id). Results of the same type are merged when, directly or
    /// through a chain:
    /// <list type="number">
    /// <item>they share a provider id;</item>
    /// <item>their ids point to the same library item (the library knows both a Tmdb and an Imdb
    /// id for it, which links results that share no id themselves);</item>
    /// <item>fallback for titles not in the library: they come from <em>different</em> providers and
    /// have the same title and year (one provider never returns the same title twice, so its
    /// separate results are never merged with each other).</item>
    /// </list>
    /// The first result keeps its position; gaps (poster, overview, year) are filled from the others.
    /// Also sets <see cref="MetadataSearchResult.InLibrary"/> and adds the library's ids.
    /// </summary>
    internal static List<MetadataSearchResult> MergeDuplicates(List<MetadataSearchResult> results, LibraryAvailabilityService? library)
    {
        var parent = Enumerable.Range(0, results.Count).ToArray();
        int Find(int index) => parent[index] == index ? index : parent[index] = Find(parent[index]);
        void Union(int a, int b)
        {
            (a, b) = (Find(a), Find(b));
            if (a != b)
            {
                parent[Math.Max(a, b)] = Math.Min(a, b); // the earlier result stays the root
            }
        }

        var owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Link(string key, int index)
        {
            if (owner.TryGetValue(key, out var other))
            {
                Union(other, index);
            }
            else
            {
                owner[key] = index;
            }
        }

        var libraryItems = new Guid?[results.Count];
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            foreach (var (provider, id) in result.ProviderIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    Link($"{result.MediaCategory}|id|{provider}:{id.Trim()}", index);
                }
            }

            libraryItems[index] = library?.FindItem(result.MediaCategory, result.ProviderIds);
            if (libraryItems[index] is { } itemId)
            {
                Link($"{result.MediaCategory}|library|{itemId:N}", index);
            }
        }

        // A title match must never put two different ids from the same provider into one result
        // (two TMDb entries are by definition different titles), even through a chain.
        bool WouldConflict(int a, int b)
        {
            var (rootA, rootB) = (Find(a), Find(b));
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < results.Count; i++)
            {
                var root = Find(i);
                if (root != rootA && root != rootB)
                {
                    continue;
                }

                foreach (var (provider, id) in results[i].ProviderIds)
                {
                    if (ids.TryGetValue(provider, out var existing) && !string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    ids[provider] = id;
                }
            }

            return false;
        }

        // Fallback: same type + year + title from different providers. Grouped by key first, so
        // titles are normalised once and only results that can match are compared.
        var byTitle = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            if (result.ProductionYear is null)
            {
                continue;
            }

            var key = $"{result.MediaCategory}|{result.ProductionYear}|{NormalizeTitle(result.Name)}";
            if (!byTitle.TryGetValue(key, out var group))
            {
                byTitle[key] = group = new List<int>();
            }

            foreach (var other in group)
            {
                if (!string.Equals(results[other].SourceProvider, result.SourceProvider, StringComparison.OrdinalIgnoreCase)
                    && !WouldConflict(other, index))
                {
                    Union(other, index);
                }
            }

            group.Add(index);
        }

        var merged = new List<MetadataSearchResult>();
        var byRoot = new Dictionary<int, MetadataSearchResult>();
        var libraryByRoot = new Dictionary<int, Guid>();
        for (var index = 0; index < results.Count; index++)
        {
            var root = Find(index);
            var result = results[index];
            if (libraryItems[index] is { } itemId)
            {
                libraryByRoot.TryAdd(root, itemId);
            }

            if (!byRoot.TryGetValue(root, out var target))
            {
                byRoot[root] = result;
                merged.Add(result);
                continue;
            }

            foreach (var (provider, id) in result.ProviderIds)
            {
                target.ProviderIds.TryAdd(provider, id);
            }

            if (string.IsNullOrWhiteSpace(target.PosterUrl))
            {
                target.PosterUrl = result.PosterUrl;
            }

            if (string.IsNullOrWhiteSpace(target.Overview))
            {
                target.Overview = result.Overview;
            }

            target.ProductionYear ??= result.ProductionYear;
        }

        foreach (var (root, result) in byRoot)
        {
            if (libraryByRoot.TryGetValue(root, out var itemId))
            {
                result.InLibrary = true;
                foreach (var (provider, id) in library?.GetProviderIds(itemId) ?? new Dictionary<string, string>())
                {
                    result.ProviderIds.TryAdd(provider, id);
                }
            }

            result.ProviderId = GetProviderId(result.ProviderIds, "Tmdb", "Imdb", "Tvdb");
        }

        return merged;
    }

    // "Project Hail Mary" == "project hail-mary": case, spacing and punctuation don't matter.
    private static string NormalizeTitle(string title)
        => new string(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public IReadOnlyList<string> GetConfiguredProviders()
    {
        return _providerManager.GetAllMetadataPlugins()
            .SelectMany(summary => summary.Plugins)
            .Select(plugin => plugin.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AppendResults(
        ICollection<MetadataSearchResult> destination,
        IEnumerable<RemoteSearchResult> results,
        string mediaType,
        MediaCategory category)
    {
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Name))
            {
                continue;
            }

            var providerIds = result.ProviderIds ?? new Dictionary<string, string>();
            var providerId = GetProviderId(providerIds, "Tmdb", "Imdb", "Tvdb");
            destination.Add(new MetadataSearchResult
            {
                ProviderId = providerId,
                Name = result.Name ?? string.Empty,
                Overview = result.Overview ?? string.Empty,
                MediaType = mediaType,
                PosterUrl = result.ImageUrl ?? string.Empty,
                ProductionYear = result.ProductionYear,
                SourceProvider = result.SearchProviderName ?? string.Empty,
                ProviderIds = new Dictionary<string, string>(providerIds, StringComparer.OrdinalIgnoreCase),
                MediaCategory = category
            });
        }
    }

    private static string GetProviderId(
        IReadOnlyDictionary<string, string> providerIds,
        params string[] preferredKeys)
    {
        foreach (var key in preferredKeys)
        {
            if (providerIds.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return providerIds.FirstOrDefault(pair => !string.IsNullOrWhiteSpace(pair.Value)).Value
            ?? string.Empty;
    }
}
