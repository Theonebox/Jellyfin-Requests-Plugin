using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.Requests.Services;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

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
        ILibraryManager libraryManager,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _applicationPaths = applicationPaths;
        _logger = logger;
        Requests = new RequestService(applicationPaths.PluginConfigurationsPath);
        // The availability cache is filled by the "Requests: Detect library media" scheduled
        // task (startup + daily) and kept current through library events. Scanning the whole
        // library here would block plugin construction during server start-up.
        LibraryAvailability = new LibraryAvailabilityService(libraryManager);
        MetadataIdentify = new MetadataIdentifyService(providerManager, LibraryAvailability);

        // Make the header button available even where the response middleware can't run.
        WebClientInjector.PatchIndexFile(applicationPaths.WebPath, logger);
    }

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<Plugin> _logger;

    public override void OnUninstalling()
    {
        WebClientInjector.UnpatchIndexFile(_applicationPaths.WebPath, _logger);
        base.OnUninstalling();
    }

    public const string ManageRequestsPageName = "RequestsManage";

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
                // Same page, listed in the dashboard sidebar; the controller opens it on the
                // Requests tab when loaded under this name.
                Name = ManageRequestsPageName,
                DisplayName = "Requests",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "how_to_vote"
            },
            new PluginPageInfo
            {
                // Loaded as an ES module via data-controller="__plugin/RequestsConfigurationjs".
                Name = "RequestsConfigurationjs",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.js"
            }
        };
    }
}
