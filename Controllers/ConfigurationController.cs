using System.Text.Json.Serialization;
using Jellyfin.Plugin.Requests.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

/// <summary>
/// Plugin settings. Reading is open to every signed-in user (the Requests page needs the
/// quality and media-type options); changing settings and diagnostics are administrator-only.
/// </summary>
[ApiController]
[Authorize]
[Route("Requests/Configuration")]
public sealed class ConfigurationController : ControllerBase
{
    private static PluginConfiguration? Configuration => Plugin.Instance?.Configuration;

    [HttpGet("QualityOptions")]
    public ActionResult<object> GetQualityOptions()
        => Ok(new { options = Clean(Configuration?.MediaQualityOptions) is { Length: > 0 } options ? options : PluginConfiguration.DefaultQualityOptions });

    [HttpPut("QualityOptions")]
    public IActionResult UpdateQualityOptions([FromBody] QualityOptionsModel model)
    {
        if (!User.IsAdministrator())
        {
            return Forbidden();
        }

        var options = Clean(model?.Options);
        if (options.Length == 0)
        {
            return BadRequest(new { error = "At least one media quality option is required." });
        }

        return Save(configuration => configuration.MediaQualityOptions = options, new { options });
    }

    [HttpGet("MediaOptions")]
    public ActionResult<object> GetMediaOptions()
    {
        var configuration = Configuration;
        return configuration is null
            ? Unavailable()
            : Ok(new { movies = configuration.EnableMovies, tvShows = configuration.EnableTvShows });
    }

    [HttpPut("MediaOptions")]
    public IActionResult UpdateMediaOptions([FromBody] MediaOptionsModel model)
    {
        if (!User.IsAdministrator())
        {
            return Forbidden();
        }

        if (model is null || (!model.Movies && !model.TvShows))
        {
            return BadRequest(new { error = "At least one media type must remain enabled." });
        }

        return Save(
            configuration =>
            {
                configuration.EnableMovies = model.Movies;
                configuration.EnableTvShows = model.TvShows;
            },
            new { movies = model.Movies, tvShows = model.TvShows });
    }

    [HttpGet("Diagnostics")]
    public ActionResult<object> GetDiagnostics()
    {
        if (!User.IsAdministrator())
        {
            return Forbidden();
        }

        var service = Plugin.Requests;
        if (service is null)
        {
            return Unavailable();
        }

        var requests = service.GetAll();
        return Ok(new
        {
            totalRequests = requests.Count,
            pendingRequests = requests.Count(request => request.Status == RequestStatus.Pending),
            approvedRequests = requests.Count(request => request.Status == RequestStatus.Approved),
            availableRequests = requests.Count(request => request.Status == RequestStatus.Available),
            pluginVersion = Plugin.Instance?.Version?.ToString() ?? "Unknown",
            storageFilePath = service.StorageFilePath,
            lastSaveTimestampUtc = service.LastSaveTimestampUtc
        });
    }

    private static string[] Clean(IEnumerable<string>? options)
        => (options ?? Array.Empty<string>())
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IActionResult Save(Action<PluginConfiguration> apply, object result)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return Unavailable();
        }

        apply(plugin.Configuration);
        plugin.SaveConfiguration();
        return Ok(result);
    }

    private ObjectResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator access is required." });

    private ObjectResult Unavailable() => StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin configuration is unavailable." });
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
