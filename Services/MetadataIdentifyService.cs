using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class MetadataIdentifyService
{
    private readonly IProviderManager _providerManager;

    public MetadataIdentifyService(IProviderManager providerManager)
    {
        _providerManager = providerManager;
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

        return MergeByProviderId(results);
    }

    /// <summary>
    /// Different metadata providers return the same title separately (e.g. TheMovieDb and OMDb).
    /// Results of the same media type that share any provider id (Tmdb, Imdb, Tvdb, ...) — directly
    /// or through a chain — are merged into one, keeping the first result's position and filling
    /// gaps (poster, overview, year) from the others. Matching is by id only, never by name.
    /// </summary>
    private static List<MetadataSearchResult> MergeByProviderId(List<MetadataSearchResult> results)
    {
        var parent = Enumerable.Range(0, results.Count).ToArray();
        int Find(int index) => parent[index] == index ? index : parent[index] = Find(parent[index]);

        var owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < results.Count; index++)
        {
            foreach (var (provider, id) in results[index].ProviderIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var key = $"{results[index].MediaCategory}|{provider}|{id.Trim()}";
                if (owner.TryGetValue(key, out var other))
                {
                    // Union, keeping the earlier result as the group's root.
                    var (a, b) = (Find(other), Find(index));
                    if (a != b)
                    {
                        parent[Math.Max(a, b)] = Math.Min(a, b);
                    }
                }
                else
                {
                    owner[key] = index;
                }
            }
        }

        var merged = new List<MetadataSearchResult>();
        var byRoot = new Dictionary<int, MetadataSearchResult>();
        for (var index = 0; index < results.Count; index++)
        {
            var root = Find(index);
            var result = results[index];
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

        foreach (var result in merged)
        {
            result.ProviderId = GetProviderId(result.ProviderIds, "Tmdb", "Imdb", "Tvdb");
        }

        return merged;
    }

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
