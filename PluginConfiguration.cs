using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Requests;

public class PluginConfiguration : BasePluginConfiguration
{
    public string TmdbApiKey { get; set; } = string.Empty;
}
