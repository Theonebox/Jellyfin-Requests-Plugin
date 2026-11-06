using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[AllowAnonymous]
[Route("Requests/Search")]
public sealed class SearchController : ControllerBase
{
    private static MetadataIdentifyService MetadataIdentify =>
        Plugin.MetadataIdentify
        ?? throw new InvalidOperationException("The metadata identification service has not been initialized.");

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MetadataSearchResult>>> Search(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "The query parameter is required." });
        }

        try
        {
            var results = await MetadataIdentify
                .SearchAsync(query, cancellationToken)
                .ConfigureAwait(false);
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
