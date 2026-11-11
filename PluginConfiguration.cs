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

    /// <summary>Quality options with descriptions, in display order.</summary>
    public QualityOption[] QualityOptions { get; set; } = Array.Empty<QualityOption>();

    /// <summary>
    /// Quality names as stored before descriptions existed (1.0.0.74 and earlier). Read as a fallback
    /// when <see cref="QualityOptions"/> is empty, and kept in sync so older versions still load.
    /// </summary>
    public string[] MediaQualityOptions { get; set; } = DefaultQualityOptions;

    /// <summary>
    /// Request statuses, in display order. The first one is given to new requests, and users can
    /// cancel their own requests only while they are still in it.
    /// </summary>
    public StatusOption[] StatusOptions { get; set; } = DefaultStatusOptions();

    public bool EnableMovies { get; set; } = true;

    public bool EnableTvShows { get; set; } = true;

    /// <summary>Subtle motion on the Requests page (fades, card hover). Purely CSS; off = no motion at all.</summary>
    public bool EnableAnimations { get; set; } = true;

    // Ids match the values older versions stored, so existing requests keep their status.
    public static StatusOption[] DefaultStatusOptions() => new[]
    {
        new StatusOption("Pending", "Pending", "#bdbdbd", "schedule"),
        new StatusOption("Approved", "Approved", "#66bb6a", "thumbUp"),
        new StatusOption("Denied", "Denied", "#ef5350", "block"),
        new StatusOption("InProgress", "In Progress", "#ffa726", "sync"),
        new StatusOption("Completed", "Completed", "#42a5f5", "check"),
        new StatusOption("Cancelled", "Cancelled", "#ef5350", "close")
    };

    /// <summary>The configured quality options (migrating names-only settings), or the defaults.</summary>
    public QualityOption[] GetQualityOptions()
    {
        if (QualityOptions is { Length: > 0 })
        {
            return QualityOptions;
        }

        var names = MediaQualityOptions is { Length: > 0 } ? MediaQualityOptions : DefaultQualityOptions;
        return names.Select(name => new QualityOption(name, string.Empty)).ToArray();
    }

    /// <summary>
    /// The configured statuses, or the defaults if none are saved. Statuses saved without a valid
    /// icon (e.g. by a version before icons were configurable) get their default icon.
    /// </summary>
    public StatusOption[] GetStatusOptions()
    {
        var options = StatusOptions is { Length: > 0 } ? StatusOptions : DefaultStatusOptions();
        foreach (var option in options)
        {
            if (!StatusIcons.IsValid(option.Icon))
            {
                option.Icon = StatusIcons.DefaultFor(option.Id);
            }
        }

        return options;
    }
}
