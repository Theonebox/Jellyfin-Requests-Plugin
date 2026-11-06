using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.Requests.Services;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Requests;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public static RequestService? Requests { get; private set; }
    public static MetadataIdentifyService? MetadataIdentify { get; private set; }
    public static LibraryAvailabilityService? LibraryAvailability { get; private set; }

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IProviderManager providerManager,
        ILibraryManager libraryManager)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Requests = new RequestService(applicationPaths.PluginConfigurationsPath);
        MetadataIdentify = new MetadataIdentifyService(providerManager);
        LibraryAvailability = new LibraryAvailabilityService(libraryManager);
        LibraryAvailability.Refresh();
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
                Name = "RequestsConfiguration",
                DisplayName = "Requests",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "RequestsUser",
                DisplayName = "Media Requests",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.requests.html",
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "how_to_vote"
            }
        };
    }
}
