using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Requests.Services;

/// <summary>Rebuilds the "In library" cache at startup and daily (new items are added live).</summary>
public sealed class LibraryAvailabilityTask : IScheduledTask
{
    public string Name => "Requests: Detect library media";

    public string Key => "JellyfinRequestsLibraryAvailability";

    public string Description => "Updates the library availability cache used by Jellyfin Requests.";

    public string Category => "Jellyfin Requests";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Plugin.LibraryAvailability?.Refresh();
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks };
    }
}
