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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        var movieTask = _providerManager.GetRemoteSearchResults<Movie, MovieInfo>(
            new RemoteSearchQuery<MovieInfo>
            {
                SearchInfo = new MovieInfo { Name = query.Trim() }
            },
            cancellationToken);

        var tvTask = _providerManager.GetRemoteSearchResults<Series, SeriesInfo>(
            new RemoteSearchQuery<SeriesInfo>
            {
                SearchInfo = new SeriesInfo { Name = query.Trim() }
            },
            cancellationToken);

        await Task.WhenAll(movieTask, tvTask).ConfigureAwait(false);

        var results = new List<MetadataSearchResult>();
        AppendResults(results, movieTask.Result, "Movie");
        AppendResults(results, tvTask.Result, "TV");

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
        string mediaType)
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
                ProviderIds = new Dictionary<string, string>(providerIds, StringComparer.OrdinalIgnoreCase)
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
