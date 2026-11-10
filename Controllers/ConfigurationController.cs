using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[Authorize]
[Route("Requests/Configuration")]
public sealed class ConfigurationController : ControllerBase
{
    [HttpGet("QualityOptions")]
    public ActionResult<object> GetQualityOptions()
    {
        var options = Plugin.Instance?.Configuration.MediaQualityOptions
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string> { "720p", "1080p", "4K" };

        return Ok(new { options });
    }

    [HttpPut("QualityOptions")]
    public IActionResult UpdateQualityOptions([FromBody] QualityOptionsModel model)
    {
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

        if (model?.Options is null)
        {
            return BadRequest(new { error = "At least one media quality option is required." });
        }

        var options = model.Options
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return BadRequest(new { error = "At least one media quality option is required." });
        }

        if (Plugin.Instance is null)
        {
            return StatusCode(500, new { error = "Plugin configuration is unavailable." });
        }

        Plugin.Instance.Configuration.MediaQualityOptions = options;
        Plugin.Instance.SaveConfiguration();
        return Ok(new { options });
    }

    [HttpGet("MediaOptions")]
    public ActionResult<object> GetMediaOptions()
    {
        var configuration = Plugin.Instance?.Configuration;
        return configuration is null
            ? StatusCode(500, new { error = "Plugin configuration is unavailable." })
            : Ok(new { movies = configuration.EnableMovies, tvShows = configuration.EnableTvShows });
    }

    [HttpPut("MediaOptions")]
    public IActionResult UpdateMediaOptions([FromBody] MediaOptionsModel model)
    {
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

        if (model is null || (!model.Movies && !model.TvShows))
        {
            return BadRequest(new { error = "At least one media type must remain enabled." });
        }

        if (Plugin.Instance is null)
        {
            return StatusCode(500, new { error = "Plugin configuration is unavailable." });
        }

        Plugin.Instance.Configuration.EnableMovies = model.Movies;
        Plugin.Instance.Configuration.EnableTvShows = model.TvShows;
        Plugin.Instance.SaveConfiguration();
        return Ok(new { movies = model.Movies, tvShows = model.TvShows });
    }

    [HttpGet("Diagnostics")]
    public ActionResult<object> GetDiagnostics()
    {
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

        var service = Plugin.Requests;
        if (service is null)
        {
            return StatusCode(500, new { error = "Request service is unavailable." });
        }

        var requests = service.GetAll();
        return Ok(new
        {
            totalRequests = requests.Count,
            pendingRequests = requests.Count(request => request.Status == Models.RequestStatus.Pending),
            approvedRequests = requests.Count(request => request.Status == Models.RequestStatus.Approved),
            availableRequests = requests.Count(request => request.Status == Models.RequestStatus.Available),
            pluginVersion = Plugin.Instance?.Version?.ToString() ?? "Unknown",
            storageFilePath = service.StorageFilePath,
            lastSaveTimestampUtc = service.LastSaveTimestampUtc
        });
    }

    private bool IsAdministrator()
    {
        if (User.IsInRole("Administrator"))
        {
            return true;
        }

        foreach (var claim in User.Claims)
        {
            if ((claim.Type == ClaimTypes.Role || claim.Type == "IsAdministrator" || claim.Type == "UserRole")
                && (string.Equals(claim.Value, "Administrator", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Value, "True", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class QualityOptionsModel
{
    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }
}

public sealed class MediaOptionsModel
{
    [JsonPropertyName("movies")]
    public bool Movies { get; set; }

    [JsonPropertyName("tvShows")]
    public bool TvShows { get; set; }
}
