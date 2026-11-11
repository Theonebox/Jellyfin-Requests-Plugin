using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

/// <summary>
/// Stores media requests in a JSON file under the plugin configuration directory.
/// All access is serialized through one lock; callers always receive copies.
/// </summary>
public sealed class RequestService
{
    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly List<MediaRequest> _requests;
    private readonly string _filePath;

    public RequestService(string pluginConfigurationPath)
    {
        var directory = Path.Combine(pluginConfigurationPath, "Requests");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "requests.json");
        _requests = Load();
    }

    /// <summary>The first configured status: given to new requests; owners may cancel while in it.</summary>
    public static string InitialStatusId => Plugin.Instance?.Configuration.GetStatusOptions()[0].Id ?? "Pending";

    public IReadOnlyList<MediaRequest> GetAll()
    {
        lock (_syncRoot)
        {
            return _requests
                .OrderByDescending(request => request.RequestedAtUtc)
                .Select(Clone)
                .ToList();
        }
    }

    public MediaRequest? GetById(Guid id)
    {
        lock (_syncRoot)
        {
            var request = _requests.FirstOrDefault(item => item.Id == id);
            return request is null ? null : Clone(request);
        }
    }

    public MediaRequest Add(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_syncRoot)
        {
            Normalize(request);
            if (IsDuplicate(request))
            {
                throw new DuplicateRequestException("This has already been requested.");
            }

            request.Id = Guid.NewGuid();
            request.RequestedAtUtc = DateTime.UtcNow;
            request.Status = InitialStatusId;
            request.StatusHistory = new List<RequestStatusChange>
            {
                new() { Status = request.Status, ChangedAtUtc = request.RequestedAtUtc }
            };

            _requests.Add(Clone(request));
            Save();
            return Clone(request);
        }
    }

    /// <summary>Sets (or clears, when empty) the administrator's reply on a request.</summary>
    public MediaRequest? UpdateAdminNote(Guid id, string note)
    {
        lock (_syncRoot)
        {
            var request = _requests.FirstOrDefault(item => item.Id == id);
            if (request is null)
            {
                return null;
            }

            note = note.Trim();
            if (!string.Equals(request.AdminNote, note, StringComparison.Ordinal))
            {
                request.AdminNote = note;
                Save();
            }

            return Clone(request);
        }
    }

    public MediaRequest? UpdateStatus(Guid id, string status)
    {
        lock (_syncRoot)
        {
            var request = _requests.FirstOrDefault(item => item.Id == id);
            if (request is null)
            {
                return null;
            }

            if (!string.Equals(request.Status, status, StringComparison.Ordinal))
            {
                request.Status = status;
                request.StatusHistory.Add(new RequestStatusChange { Status = status, ChangedAtUtc = DateTime.UtcNow });
                Save();
            }

            return Clone(request);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_syncRoot)
        {
            var removed = _requests.RemoveAll(request => request.Id == id) > 0;
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    /// <summary>
    /// Returns the status of an existing request (by any user) for the given title, or null.
    /// A request for the whole movie/series wins over season or episode requests.
    /// </summary>
    public string? FindExistingStatus(MediaCategory category, string providerId, IReadOnlyDictionary<string, string> providerIds)
    {
        var probe = new MediaRequest { ProviderId = providerId ?? string.Empty, ProviderIds = new(providerIds, StringComparer.OrdinalIgnoreCase) };

        lock (_syncRoot)
        {
            var matches = _requests
                .Where(request => request.MediaCategory == category && HasMatchingProviderId(request, probe))
                .ToList();
            if (matches.Count == 0)
            {
                return null;
            }

            var whole = matches.FirstOrDefault(request => request.RequestScope is RequestScope.EntireMovie or RequestScope.EntireSeries);
            return (whole ?? matches[0]).Status;
        }
    }

    private List<MediaRequest> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<MediaRequest>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var requests = string.IsNullOrWhiteSpace(json)
                ? new List<MediaRequest>()
                : JsonSerializer.Deserialize<List<MediaRequest>>(json, _jsonOptions) ?? new List<MediaRequest>();
            requests.ForEach(Normalize);
            return requests;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new List<MediaRequest>();
        }
    }

    private void Save()
    {
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_requests, _jsonOptions));
        File.Move(tempPath, _filePath, true);
    }

    private static MediaRequest Clone(MediaRequest request)
    {
        var copy = request.ShallowCopy();
        copy.ProviderIds = new Dictionary<string, string>(request.ProviderIds, StringComparer.OrdinalIgnoreCase);
        copy.RequestedSeasonNumbers = request.RequestedSeasonNumbers.ToList();
        copy.RequestedEpisodeIds = request.RequestedEpisodeIds.ToList();
        copy.RequestedEpisodeNames = request.RequestedEpisodeNames.ToList();
        copy.StatusHistory = request.StatusHistory
            .Select(change => new RequestStatusChange { Status = change.Status, ChangedAtUtc = change.ChangedAtUtc })
            .ToList();
        return copy;
    }

    private bool IsDuplicate(MediaRequest candidate)
    {
        foreach (var request in _requests)
        {
            if (request.MediaCategory != candidate.MediaCategory || !HasMatchingProviderId(request, candidate))
            {
                continue;
            }

            var duplicate = candidate.RequestScope switch
            {
                RequestScope.EntireMovie or RequestScope.EntireSeries => request.RequestScope == candidate.RequestScope,
                RequestScope.SeasonSelection => request.RequestScope == RequestScope.SeasonSelection
                    && request.RequestedSeasonNumbers.Intersect(candidate.RequestedSeasonNumbers).Any(),
                RequestScope.EpisodeSelection => request.RequestScope == RequestScope.EpisodeSelection
                    && request.RequestedEpisodeIds.Intersect(candidate.RequestedEpisodeIds, StringComparer.OrdinalIgnoreCase).Any(),
                _ => false
            };

            if (duplicate)
            {
                return true;
            }
        }

        return false;
    }

    // Compares provider-qualified ids (Tmdb:555 vs Tvdb:555 are different titles). The bare
    // ProviderId is only a fallback for records that carry no provider id map.
    private static bool HasMatchingProviderId(MediaRequest left, MediaRequest right)
    {
        if (left.ProviderIds.Count > 0 && right.ProviderIds.Count > 0)
        {
            return left.ProviderIds.Any(pair =>
                right.ProviderIds.TryGetValue(pair.Key, out var value)
                && string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase));
        }

        return !string.IsNullOrWhiteSpace(left.ProviderId)
            && string.Equals(left.ProviderId, right.ProviderId, StringComparison.OrdinalIgnoreCase);
    }

    private static void Normalize(MediaRequest request)
    {
        request.UserId = request.UserId?.Trim() ?? string.Empty;
        request.UserName = request.UserName?.Trim() ?? string.Empty;
        request.MediaId = request.MediaId?.Trim() ?? string.Empty;
        request.ProviderId = request.ProviderId?.Trim() ?? string.Empty;
        request.ProviderIds = (request.ProviderIds ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        request.RequestedSeasonNumbers = (request.RequestedSeasonNumbers ?? new List<int>())
            .Where(value => value >= 0).Distinct().Order().ToList();
        request.RequestedEpisodeIds = (request.RequestedEpisodeIds ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        request.RequestedEpisodeNames = (request.RequestedEpisodeNames ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        request.Note = request.Note?.Trim() ?? string.Empty;
        request.AdminNote = request.AdminNote?.Trim() ?? string.Empty;
        request.MediaQuality = request.MediaQuality?.Trim() ?? string.Empty;
        // "Available" was removed as a status; fold it into "Completed". Blank (very old) -> initial.
        request.Status = string.IsNullOrWhiteSpace(request.Status) ? InitialStatusId
            : string.Equals(request.Status, "Available", StringComparison.OrdinalIgnoreCase) ? "Completed"
            : request.Status.Trim();
        request.StatusHistory ??= new List<RequestStatusChange>();
        if (request.StatusHistory.Count == 0 && request.RequestedAtUtc != default)
        {
            request.StatusHistory.Add(new RequestStatusChange { Status = request.Status, ChangedAtUtc = request.RequestedAtUtc });
        }

        if (request.MediaCategory == MediaCategory.All)
        {
            request.MediaCategory = request.MediaType.StartsWith("TV", StringComparison.OrdinalIgnoreCase)
                ? MediaCategory.TVShows
                : MediaCategory.Movies;
        }

        if (request.MediaCategory == MediaCategory.TVShows)
        {
            if (request.RequestScope == RequestScope.EntireMovie)
            {
                request.RequestScope = RequestScope.EntireSeries;
            }
        }
        else
        {
            request.RequestScope = RequestScope.EntireMovie;
        }
    }
}

public sealed class DuplicateRequestException : InvalidOperationException
{
    public DuplicateRequestException(string message)
        : base(message)
    {
    }
}
