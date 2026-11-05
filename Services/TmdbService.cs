using System.Globalization;
using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class TmdbService
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w500";

    private readonly HttpClient _httpClient;

    public TmdbService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IEnumerable<TmdbSearchResult>> SearchMoviesAsync(string query)
    {
        return await SearchAsync(query, "movie", "movie").ConfigureAwait(false);
    }

    public async Task<IEnumerable<TmdbSearchResult>> SearchTvShowsAsync(string query)
    {
        return await SearchAsync(query, "tv", "tv").ConfigureAwait(false);
    }

    private async Task<IEnumerable<TmdbSearchResult>> SearchAsync(
        string query,
        string endpoint,
        string mediaType)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("A search query is required.", nameof(query));
        }

        var apiKey = NormalizeCredential(GetApiKey());
        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var isLikelyApiKey = IsV3ApiKey(apiKey);
        var strategies = isLikelyApiKey
            ? new[] { CredentialType.ApiKey }
            : new[] { CredentialType.Bearer, CredentialType.ApiKey };

        try
        {
            string? json = null;
            HttpStatusCode failedStatus = HttpStatusCode.Unauthorized;

            foreach (var strategy in strategies)
            {
                var url = strategy == CredentialType.Bearer
                    ? $"{BaseUrl}/search/{endpoint}?query={encodedQuery}&include_adult=false"
                    : $"{BaseUrl}/search/{endpoint}?api_key={Uri.EscapeDataString(apiKey)}&query={encodedQuery}&include_adult=false";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("application/json");
                if (strategy == CredentialType.Bearer)
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    json = responseJson;
                    break;
                }

                failedStatus = response.StatusCode;
            }

            if (json is null)
            {
                throw new TmdbServiceException(
                    failedStatus == HttpStatusCode.Unauthorized
                        ? "TMDb rejected the configured credentials."
                        : $"TMDb returned an error: {(int)failedStatus}",
                    failedStatus);
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<TmdbSearchResult>();
            }

            var matches = new List<TmdbSearchResult>();
            foreach (var result in results.EnumerateArray())
            {
                var id = result.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.TryGetInt32(out var parsedId)
                        ? parsedId
                        : 0;

                var name = GetString(
                    result,
                    endpoint == "movie" ? "title" : "name");
                var overview = GetString(result, "overview");
                var posterPath = GetString(result, "poster_path");
                var dateText = GetString(
                    result,
                    endpoint == "movie" ? "release_date" : "first_air_date");

                matches.Add(new TmdbSearchResult
                {
                    Id = id,
                    Name = name,
                    Overview = overview,
                    PosterPath = string.IsNullOrWhiteSpace(posterPath)
                        ? string.Empty
                        : $"{PosterBaseUrl}{posterPath}",
                    MediaType = mediaType,
                    ReleaseDate = ParseDate(dateText)
                });
            }

            return matches;
        }
        catch (TmdbServiceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new TmdbServiceException("TMDb returned invalid JSON.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TmdbServiceException("Unable to connect to TMDb.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new TmdbServiceException("The TMDb request timed out.", exception);
        }
    }

    private static string GetApiKey()
    {
        var apiKey = Plugin.Instance?.Configuration.TmdbApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new TmdbServiceException(
                "A TMDb API key has not been configured.",
                HttpStatusCode.BadRequest);
        }

        return apiKey;
    }

    private static string NormalizeCredential(string credential)
    {
        var normalized = credential.Trim().Trim('"', '\'');
        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Bearer ".Length..].Trim();
        }

        return normalized;
    }

    private static bool IsV3ApiKey(string credential)
    {
        return credential.Length == 32
            && credential.All(character =>
                char.IsAsciiHexDigit(character));
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static DateTime? ParseDate(string value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var date)
                ? date
                : null;
    }
}

internal enum CredentialType
{
    ApiKey,
    Bearer
}

public sealed class TmdbServiceException : Exception
{
    public TmdbServiceException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public TmdbServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = HttpStatusCode.BadGateway;
    }

    public HttpStatusCode StatusCode { get; }
}
