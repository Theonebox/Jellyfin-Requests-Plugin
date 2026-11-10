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
        [FromQuery] string mediaType = "Movies",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "The query parameter is required." });
        }

        try
        {
            var category = mediaType.Equals("TV Shows", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("TVShows", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("TV", StringComparison.OrdinalIgnoreCase)
                ? MediaCategory.TVShows
                : mediaType.Equals("All", StringComparison.OrdinalIgnoreCase)
                    ? MediaCategory.All
                    : MediaCategory.Movies;

            var configuration = Plugin.Instance?.Configuration;
            if (configuration is not null)
            {
                // Never search a media type the administrator has disabled.
                MediaCategory? allowed = (category, configuration.EnableMovies, configuration.EnableTvShows) switch
                {
                    (MediaCategory.All, true, true) => MediaCategory.All,
                    (MediaCategory.All, true, false) or (MediaCategory.Movies, true, _) => MediaCategory.Movies,
                    (MediaCategory.All, false, true) or (MediaCategory.TVShows, _, true) => MediaCategory.TVShows,
                    _ => null
                };

                if (allowed is null)
                {
                    return BadRequest(new { error = "Requests for this media type are currently disabled." });
                }

                category = allowed.Value;
            }

            var results = await MetadataIdentify
                .SearchAsync(query, category, cancellationToken)
                .ConfigureAwait(false);

            // Resolve library availability here in one pass instead of one HTTP call per result.
            var availability = Plugin.LibraryAvailability;
            if (availability is not null)
            {
                foreach (var result in results)
                {
                    result.InLibrary = availability.IsInLibrary(result.ProviderIds.Values);
                }
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
