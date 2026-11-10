using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Requests;

public class PluginConfiguration : BasePluginConfiguration
{
    public static readonly string[] DefaultQualityOptions = { "720p", "1080p", "4K" };

    /// <summary>
    /// Quality choices offered to users. This is an array on purpose: Jellyfin stores plugin
    /// configuration as XML, and XmlSerializer *appends* saved items to a pre-filled List
    /// (so defaults came back on every restart), whereas arrays are replaced.
    /// </summary>
    public string[] MediaQualityOptions { get; set; } = DefaultQualityOptions;

    public bool EnableMovies { get; set; } = true;

    public bool EnableTvShows { get; set; } = true;
}
