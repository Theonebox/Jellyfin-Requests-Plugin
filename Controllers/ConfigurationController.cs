using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
public sealed partial class ConfigurationController : ControllerBase
{
    private static PluginConfiguration? Configuration => Plugin.Instance?.Configuration;

    /// <summary>
    /// Everything the Requests page and settings page need, in one call: quality options, enabled
    /// media types, statuses and the animations switch.
    /// </summary>
    [HttpGet("Client")]
    public ActionResult<object> GetClientSettings()
    {
        var configuration = Configuration;
        return Ok(new
        {
            qualityOptions = QualityOptions(),
            movies = configuration?.EnableMovies ?? true,
            tvShows = configuration?.EnableTvShows ?? true,
            statusOptions = configuration?.GetStatusOptions() ?? PluginConfiguration.DefaultStatusOptions(),
            statusIcons = StatusIcons.All,
            animations = configuration?.EnableAnimations ?? true
        });
    }

    [HttpPut("Animations")]
    public IActionResult UpdateAnimations([FromBody] AnimationsModel model)
    {
        if (!User.IsAdministrator())
        {
            return Forbidden();
        }

        var enabled = model?.Enabled ?? true;
        return Save(configuration => configuration.EnableAnimations = enabled, new { enabled });
    }

    private static QualityOption[] QualityOptions()
        => Clean(Configuration?.GetQualityOptions()) is { Length: > 0 } options
            ? options
            : PluginConfiguration.DefaultQualityOptions.Select(name => new QualityOption(name, string.Empty)).ToArray();

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

        return Save(
            configuration =>
            {
                configuration.QualityOptions = options;
                configuration.MediaQualityOptions = options.Select(option => option.Name).ToArray();
            },
            new { options });
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
            // Keyed by status id, so it follows whatever statuses are configured.
            statusCounts = requests.GroupBy(request => request.Status).ToDictionary(group => group.Key, group => group.Count()),
            pluginVersion = Plugin.Instance?.Version?.ToString() ?? "Unknown"
        });
    }

    [HttpPut("StatusOptions")]
    public IActionResult UpdateStatusOptions([FromBody] StatusOptionsModel model)
    {
        if (!User.IsAdministrator())
        {
            return Forbidden();
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var options = new List<StatusOption>();
        foreach (var option in model?.Options ?? new List<StatusOption>())
        {
            var name = option.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            // New entries arrive without an id; existing ones keep theirs so renames are safe.
            var id = string.IsNullOrWhiteSpace(option.Id) ? "s" + Guid.NewGuid().ToString("N")[..8] : option.Id.Trim();
            if (!seenIds.Add(id))
            {
                return BadRequest(new { error = $"Duplicate status id \"{id}\"." });
            }

            var color = HexColorRegex().IsMatch(option.Color ?? string.Empty) ? option.Color! : "#bdbdbd";
            var icon = StatusIcons.IsValid(option.Icon) ? option.Icon : StatusIcons.DefaultFor(id);
            options.Add(new StatusOption(id, name, color.ToLowerInvariant(), icon));
        }

        if (options.Count == 0)
        {
            return BadRequest(new { error = "At least one status is required." });
        }

        var result = options.ToArray();
        return Save(configuration => configuration.StatusOptions = result, new { options = result });
    }

    // Trims, drops blank names and duplicate names (case-insensitive), keeping order.
    private static QualityOption[] Clean(IEnumerable<QualityOption>? options)
        => (options ?? Array.Empty<QualityOption>())
            .Where(option => !string.IsNullOrWhiteSpace(option?.Name))
            .Select(option => new QualityOption(option.Name.Trim(), option.Description?.Trim() ?? string.Empty))
            .DistinctBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
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

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private ObjectResult Unavailable() => StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin configuration is unavailable." });
}

public sealed class QualityOptionsModel
{
    [JsonPropertyName("options")]
    public List<QualityOption>? Options { get; set; }
}

public sealed class MediaOptionsModel
{
    [JsonPropertyName("movies")]
    public bool Movies { get; set; }

    [JsonPropertyName("tvShows")]
    public bool TvShows { get; set; }
}

public sealed class StatusOptionsModel
{
    [JsonPropertyName("options")]
    public List<StatusOption>? Options { get; set; }
}

public sealed class AnimationsModel
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}
