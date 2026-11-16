using System.Net.Http.Headers;
using System.Text.Json;
using Jellyfin.Plugin.Requests.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Services;

/// <summary>
/// This week's trending movies and TV shows from TMDB, using the administrator's own API key.
/// Results are cached for an hour and shared by all users, so TMDB is called at most a couple of
/// times an hour regardless of how many people open the Requests page.
/// </summary>
public sealed class TrendingService
{
    private const string ApiBase = "https://api.themoviedb.org/3/";
    private const string PosterBase = "https://image.tmdb.org/t/p/w342";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TrendingService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<MediaCategory, (DateTime Expires, string Key, IReadOnlyList<MetadataSearchResult> Items)> _cache = new();

    public TrendingService(IHttpClientFactory httpClientFactory, ILogger<TrendingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Trending titles of one type (Movies or TVShows); empty if the key is missing or TMDB fails.</summary>
    public async Task<IReadOnlyList<MetadataSearchResult>> GetTrendingAsync(MediaCategory category, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || category is not (MediaCategory.Movies or MediaCategory.TVShows))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        if (TryGetCached(category, apiKey, out var cached))
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another request may have refreshed it while we waited.
            if (TryGetCached(category, apiKey, out cached))
            {
                return cached;
            }

            var path = category == MediaCategory.Movies ? "trending/movie/week" : "trending/tv/week";
            using var document = await GetAsync(path, apiKey, cancellationToken).ConfigureAwait(false);
            var items = document is null ? Array.Empty<MetadataSearchResult>() : Parse(document.RootElement, category);

            // Failures are cached briefly too, so a bad key doesn't hit TMDB on every page open.
            var expires = DateTime.UtcNow + (document is null ? TimeSpan.FromMinutes(5) : CacheDuration);
            lock (_cache)
            {
                _cache[category] = (expires, apiKey, items);
            }

            return items;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Checks a key against TMDB; returns null when valid, otherwise an error message.</summary>
    public async Task<string?> ValidateKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var document = await GetAsync("configuration", apiKey, cancellationToken).ConfigureAwait(false);
        return document is null ? "TMDB rejected the key, or couldn't be reached." : null;
    }

    public void ClearCache()
    {
        lock (_cache)
        {
            _cache.Clear();
        }
    }

    private bool TryGetCached(MediaCategory category, string apiKey, out IReadOnlyList<MetadataSearchResult> items)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(category, out var entry) && entry.Key == apiKey && entry.Expires > DateTime.UtcNow)
            {
                items = entry.Items;
                return true;
            }
        }

        items = Array.Empty<MetadataSearchResult>();
        return false;
    }

    private async Task<JsonDocument?> GetAsync(string path, string apiKey, CancellationToken cancellationToken)
    {
        var key = apiKey.Trim();
        // v4 "read access tokens" are JWTs sent as a bearer token; v3 keys go in the query string.
        var isToken = key.StartsWith("eyJ", StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path + (isToken ? string.Empty : "?api_key=" + Uri.EscapeDataString(key)));
        if (isToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Requests: TMDB returned {Status} for {Path}.", (int)response.StatusCode, path);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("Requests: could not load {Path} from TMDB: {Message}", path, exception.Message);
            return null;
        }
    }

    private static IReadOnlyList<MetadataSearchResult> Parse(JsonElement root, MediaCategory category)
    {
        var results = new List<MetadataSearchResult>();
        if (!root.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            var name = Text(item, category == MediaCategory.Movies ? "title" : "name");
            if (id is null || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var date = Text(item, category == MediaCategory.Movies ? "release_date" : "first_air_date");
            var poster = Text(item, "poster_path");
            results.Add(new MetadataSearchResult
            {
                ProviderId = id,
                Name = name,
                Overview = Text(item, "overview"),
                MediaType = category == MediaCategory.Movies ? "Movie" : "TV",
                MediaCategory = category,
                PosterUrl = string.IsNullOrEmpty(poster) ? string.Empty : PosterBase + poster,
                ProductionYear = date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out var year) ? year : null,
                SourceProvider = "TheMovieDb",
                ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = id }
            });
        }

        return results;
    }

    private static string Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
