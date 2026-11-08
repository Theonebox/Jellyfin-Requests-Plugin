using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Jellyfin.Plugin.Requests;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class NavigationStartupService : IScheduledTask
{
    private readonly ILogger<NavigationStartupService> _logger;

    public NavigationStartupService(ILogger<NavigationStartupService> logger)
    {
        _logger = logger;
    }

    public string Name => "Requests Navigation Startup";

    public string Key => "Jellyfin.Plugin.Requests.NavigationStartup";

    public string Description => "Registers the Requests navigation transformation.";

    public string Category => "Startup Services";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var fileTransformationAssembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly => assembly.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) == true);

        var pluginInterfaceType = fileTransformationAssembly?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        var registerMethod = pluginInterfaceType?.GetMethod("RegisterTransformation");
        var jobjectType = fileTransformationAssembly?.GetType("Newtonsoft.Json.Linq.JObject");
        var parseMethod = jobjectType?.GetMethod("Parse", new[] { typeof(string) });

        if (registerMethod is null || parseMethod is null)
        {
            _logger.LogWarning("File Transformation is not available; Requests navigation was not registered.");
            return Task.CompletedTask;
        }

        var payload = parseMethod.Invoke(null, new object[]
        {
            JsonSerializer.Serialize(new
            {
                id = "e1b932f4-7bc3-4f4b-8a0d-requests-navigation",
                fileNamePattern = "index.html",
                callbackAssembly = typeof(NavigationStartupService).Assembly.FullName,
                callbackClass = typeof(NavigationTransformation).FullName,
                callbackMethod = nameof(NavigationTransformation.IndexHtml)
            })
        });

        registerMethod.Invoke(null, new[] { payload });
        _logger.LogInformation("Registered Requests navigation transformation.");
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
    }
}
