using System.Text.Json.Serialization;
using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

/// <summary>
/// Trending movies and TV shows (TMDB) shown on the Requests page before anything is searched.
/// Optional: off unless an administrator enables it and supplies their own TMDB key.
/// </summary>
[ApiController]
[Authorize]
[Route("Requests/Trending")]
public sealed class TrendingController : ControllerBase
{
    private readonly TrendingService _trending;

    public TrendingController(TrendingService trending)
    {
        _trending = trending;
    }

    /// <summary>Trending titles for every enabled type, with "In library" / "Already requested" filled in.</summary>
    [HttpGet]
    public async Task<ActionResult<object>> Get(CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || !configuration.EnableTrending || string.IsNullOrWhiteSpace(configuration.TmdbApiKey))
        {
            return Ok(new { enabled = false, movies = Array.Empty<MetadataSearchResult>(), tvShows = Array.Empty<MetadataSearchResult>() });
        }

        var moviesTask = configuration.EnableMovies
            ? _trending.GetTrendingAsync(MediaCategory.Movies, configuration.TmdbApiKey, cancellationToken)
            : Task.FromResult<IReadOnlyList<MetadataSearchResult>>(Array.Empty<MetadataSearchResult>());
        var tvTask = configuration.EnableTvShows
            ? _trending.GetTrendingAsync(MediaCategory.TVShows, configuration.TmdbApiKey, cancellationToken)
            : Task.FromResult<IReadOnlyList<MetadataSearchResult>>(Array.Empty<MetadataSearchResult>());
        await Task.WhenAll(moviesTask, tvTask).ConfigureAwait(false);

        return Ok(new { enabled = true, movies = Annotate(moviesTask.Result), tvShows = Annotate(tvTask.Result) });
    }

    /// <summary>Admin: turn trending on/off and set (or clear) the TMDB key. The key is never returned.</summary>
    [HttpPut("Settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] TrendingSettingsModel model, CancellationToken cancellationToken)
    {
        if (!User.IsAdministrator())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator access is required." });
        }

        var plugin = Plugin.Instance;
        if (plugin is null || model is null)
        {
            return BadRequest(new { error = "Invalid request." });
        }

        // ApiKey: null = keep the saved key, "" = remove it, anything else = new key (checked first).
        var key = model.ApiKey?.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            var error = await _trending.ValidateKeyAsync(key, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                return BadRequest(new { error });
            }
        }

        if (key is not null)
        {
            plugin.Configuration.TmdbApiKey = key;
        }

        plugin.Configuration.EnableTrending = model.Enabled;
        plugin.SaveConfiguration();
        _trending.ClearCache();
        return Ok(new { enabled = plugin.Configuration.EnableTrending, keySet = !string.IsNullOrWhiteSpace(plugin.Configuration.TmdbApiKey) });
    }

    // Copies so the shared cache isn't modified, then marks library/request state like search does.
    private static List<MetadataSearchResult> Annotate(IReadOnlyList<MetadataSearchResult> items)
    {
        var copies = items.Select(item => new MetadataSearchResult
        {
            ProviderId = item.ProviderId,
            Name = item.Name,
            Overview = item.Overview,
            MediaType = item.MediaType,
            MediaCategory = item.MediaCategory,
            PosterUrl = item.PosterUrl,
            ProductionYear = item.ProductionYear,
            SourceProvider = item.SourceProvider,
            ProviderIds = new Dictionary<string, string>(item.ProviderIds, StringComparer.OrdinalIgnoreCase)
        }).ToList();

        var merged = MetadataIdentifyService.MergeDuplicates(copies, Plugin.LibraryAvailability);
        foreach (var result in merged)
        {
            result.RequestStatus = Plugin.Requests?.FindExistingStatus(result.MediaCategory, result.ProviderId, result.ProviderIds);
        }

        return merged;
    }
}

public sealed class TrendingSettingsModel
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }
}
