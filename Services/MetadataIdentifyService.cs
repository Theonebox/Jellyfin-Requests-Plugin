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

        return results
            .GroupBy(result => $"{result.MediaType}:{result.ProviderId}:{result.Name}:{result.ProductionYear}")
            .Select(group => group.First())
            .ToList();
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
