using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.Requests.Services;

namespace Jellyfin.Plugin.Requests;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public static RequestService? Requests { get; private set; }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Requests = new RequestService(applicationPaths.PluginConfigurationsPath);
    }

    public override string Name => "Requests";

    public override Guid Id =>
        Guid.Parse("6A5A5B44-639D-4F8A-934F-7AEB03475F91");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "Requests",
                DisplayName = "Requests",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        };
    }
}
