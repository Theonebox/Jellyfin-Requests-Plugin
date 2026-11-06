using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Requests;

public class PluginConfiguration : BasePluginConfiguration
{
    public List<string> MediaQualityOptions { get; set; } = new()
    {
        "720p",
        "1080p",
        "4K"
    };
}
