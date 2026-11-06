using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class LibraryAvailabilityTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;

    public LibraryAvailabilityTask(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public string Name => "Requests: Detect library media";
    public string Key => "JellyfinRequestsLibraryAvailability";
    public string Description => "Updates the library availability cache used by Jellyfin Requests.";
    public string Category => "Jellyfin Requests";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        cancellationToken.ThrowIfCancellationRequested();
        var service = Plugin.LibraryAvailability ?? new LibraryAvailabilityService(_libraryManager);
        service.Refresh();
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        };
    }
}
