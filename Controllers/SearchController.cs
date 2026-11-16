using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[Authorize]
[Route("Requests/Search")]
public sealed class SearchController : ControllerBase
{
    private static MetadataIdentifyService MetadataIdentify =>
        Plugin.MetadataIdentify
        ?? throw new InvalidOperationException("The metadata identification service has not been initialized.");

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MetadataSearchResult>>> Search(
        [FromQuery] string query,
        [FromQuery] string mediaType = "All",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "The query parameter is required." });
        }

        try
        {
            // mediaType is one type or a comma-separated list ("Movies,TV Shows"); "All" means every
            // enabled type. Types the administrator has disabled are never searched.
            var configuration = Plugin.Instance?.Configuration;
            var categories = ParseCategories(mediaType)
                .Where(category => configuration?.IsEnabled(category) ?? true)
                .ToHashSet();
            if (categories.Count == 0)
            {
                return BadRequest(new { error = "Requests for this media type are currently disabled." });
            }

            var results = await MetadataIdentify
                .SearchAsync(query, categories, cancellationToken)
                .ConfigureAwait(false);

            // "In library" is resolved while merging; add existing requests (by anyone) so the page
            // can show "Already requested" without extra calls per result.
            var requests = Plugin.Requests;
            foreach (var result in results)
            {
                result.RequestStatus = requests?.FindExistingStatus(result.MediaCategory, result.ProviderId, result.ProviderIds);
            }

            return Ok(results);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            return StatusCode(502, new { error = "Jellyfin metadata providers could not complete the search." });
        }
    }

    private static IEnumerable<MediaCategory> ParseCategories(string? mediaType)
    {
        foreach (var part in (mediaType ?? "All").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.Replace(" ", string.Empty).ToLowerInvariant())
            {
                case "all":
                    yield return MediaCategory.Movies;
                    yield return MediaCategory.TVShows;
                    yield return MediaCategory.Music;
                    break;
                case "movies" or "movie":
                    yield return MediaCategory.Movies;
                    break;
                case "tvshows" or "tv":
                    yield return MediaCategory.TVShows;
                    break;
                case "music" or "albums" or "album":
                    yield return MediaCategory.Music;
                    break;
            }
        }
    }

    [HttpGet("Providers")]
    public ActionResult<object> GetProviders()
    {
        var providers = MetadataIdentify.GetConfiguredProviders();
        return Ok(new
        {
            searchProvider = "Jellyfin Metadata Identification",
            status = providers.Count > 0 ? "Available" : "No metadata providers reported",
            metadataSources = providers
        });
    }
}
