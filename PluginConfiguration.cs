using Jellyfin.Plugin.Requests.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Requests;

/// <remarks>
/// Jellyfin stores this as XML. Collections are arrays on purpose: XmlSerializer *appends* saved
/// items to a pre-filled List (so defaults reappeared after every restart), whereas arrays are
/// replaced with exactly what was saved.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    public static readonly string[] DefaultQualityOptions = { "720p", "1080p", "4K" };

    public string[] MediaQualityOptions { get; set; } = DefaultQualityOptions;

    /// <summary>
    /// Request statuses, in display order. The first one is given to new requests, and users can
    /// cancel their own requests only while they are still in it.
    /// </summary>
    public StatusOption[] StatusOptions { get; set; } = DefaultStatusOptions();

    public bool EnableMovies { get; set; } = true;

    public bool EnableTvShows { get; set; } = true;

    // Ids match the values older versions stored, so existing requests keep their status.
    public static StatusOption[] DefaultStatusOptions() => new[]
    {
        new StatusOption("Pending", "Pending", "#bdbdbd"),
        new StatusOption("Approved", "Approved", "#66bb6a"),
        new StatusOption("Denied", "Denied", "#ef5350"),
        new StatusOption("InProgress", "In Progress", "#ffa726"),
        new StatusOption("Completed", "Completed", "#42a5f5"),
        new StatusOption("Cancelled", "Cancelled", "#ef5350")
    };

    /// <summary>The configured statuses, or the defaults if none are saved.</summary>
    public StatusOption[] GetStatusOptions()
        => StatusOptions is { Length: > 0 } ? StatusOptions : DefaultStatusOptions();
}
