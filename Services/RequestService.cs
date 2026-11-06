using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class RequestService : IRequestService
{
    private readonly string _filePath;
    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly List<MediaRequest> _requests;

    public RequestService(string pluginConfigurationPath)
    {
        var requestsDirectory = Path.Combine(pluginConfigurationPath, "Requests");
        Directory.CreateDirectory(requestsDirectory);
        _filePath = Path.Combine(requestsDirectory, "requests.json");
        _requests = Load();
    }

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
            if (IsDuplicateLocked(request))
            {
                throw new DuplicateRequestException("This media request has already been submitted.");
            }

            request.Id = Guid.NewGuid();
            request.RequestedAtUtc = DateTime.UtcNow;
            request.Status = RequestStatus.Pending;
            _requests.Add(Clone(request));
            Save();
            return Clone(request);
        }
    }

    public MediaRequest? Update(MediaRequest update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_syncRoot)
        {
            var request = _requests.FirstOrDefault(item => item.Id == update.Id);
            if (request is null)
            {
                return null;
            }

            request.UserId = string.IsNullOrWhiteSpace(update.UserId) ? request.UserId : update.UserId.Trim();
            request.UserName = string.IsNullOrWhiteSpace(update.UserName) ? request.UserName : update.UserName.Trim();
            request.MediaId = string.IsNullOrWhiteSpace(update.MediaId) ? request.MediaId : update.MediaId.Trim();
            request.Title = string.IsNullOrWhiteSpace(update.Title) ? request.Title : update.Title.Trim();
            request.MediaType = string.IsNullOrWhiteSpace(update.MediaType) ? request.MediaType : update.MediaType.Trim();
            request.Overview = update.Overview?.Trim() ?? request.Overview;
            request.ProviderId = string.IsNullOrWhiteSpace(update.ProviderId) ? request.ProviderId : update.ProviderId.Trim();
            request.ProviderIds = update.ProviderIds?.Count > 0
                ? new Dictionary<string, string>(update.ProviderIds, StringComparer.OrdinalIgnoreCase)
                : request.ProviderIds;
            request.SourceProvider = string.IsNullOrWhiteSpace(update.SourceProvider) ? request.SourceProvider : update.SourceProvider.Trim();
            request.ProductionYear = update.ProductionYear ?? request.ProductionYear;
            request.MediaCategory = update.MediaCategory == MediaCategory.All ? request.MediaCategory : update.MediaCategory;
            request.RequestScope = update.RequestScope;
            request.RequestKind = update.RequestKind;
            request.RequestedSeasonNumbers = update.RequestedSeasonNumbers?.Distinct().OrderBy(value => value).ToList() ?? request.RequestedSeasonNumbers;
            request.RequestedEpisodeIds = update.RequestedEpisodeIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? request.RequestedEpisodeIds;
            request.RequestedEpisodeNames = update.RequestedEpisodeNames?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? request.RequestedEpisodeNames;
            request.Note = update.Note?.Trim() ?? request.Note;
            request.Status = update.Status;
            Save();
            return Clone(request);
        }
    }

    public MediaRequest? UpdateStatus(Guid id, RequestStatus status)
    {
        lock (_syncRoot)
        {
            var request = _requests.FirstOrDefault(item => item.Id == id);
            if (request is null)
            {
                return null;
            }

            request.Status = status;
            Save();
            return Clone(request);
        }
    }

    public bool IsDuplicate(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_syncRoot)
        {
            Normalize(request);
            return IsDuplicateLocked(request);
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

    private List<MediaRequest> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<MediaRequest>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<MediaRequest>();
            }

            var requests = JsonSerializer.Deserialize<List<MediaRequest>>(json, _jsonOptions) ?? new List<MediaRequest>();
            foreach (var request in requests)
            {
                Normalize(request);
            }
            return requests;
        }
        catch (JsonException)
        {
            return new List<MediaRequest>();
        }
        catch (IOException)
        {
            return new List<MediaRequest>();
        }
    }

    private void Save()
    {
        var tempPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(_requests, _jsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, true);
    }

    private static MediaRequest Clone(MediaRequest request)
    {
        return new MediaRequest
        {
            Id = request.Id,
            UserId = request.UserId,
            UserName = request.UserName,
            MediaId = request.MediaId,
            Title = request.Title,
            MediaType = request.MediaType,
            Overview = request.Overview,
            ProviderId = request.ProviderId,
            ProviderIds = request.ProviderIds is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(request.ProviderIds, StringComparer.OrdinalIgnoreCase),
            SourceProvider = request.SourceProvider,
            ProductionYear = request.ProductionYear,
            RequestedAtUtc = request.RequestedAtUtc,
            Status = request.Status,
            MediaCategory = request.MediaCategory,
            RequestScope = request.RequestScope,
            RequestKind = request.RequestKind,
            RequestedSeasonNumbers = request.RequestedSeasonNumbers?.ToList() ?? new List<int>(),
            RequestedEpisodeIds = request.RequestedEpisodeIds?.ToList() ?? new List<string>(),
            RequestedEpisodeNames = request.RequestedEpisodeNames?.ToList() ?? new List<string>(),
            Note = request.Note ?? string.Empty
        };
    }

    private bool IsDuplicateLocked(MediaRequest candidate)
    {
        foreach (var request in _requests)
        {
            if (!string.Equals(request.MediaId, candidate.MediaId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.ProviderId, candidate.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate.RequestScope == RequestScope.EntireMovie
                || candidate.RequestScope == RequestScope.EntireSeries)
            {
                if (request.RequestScope == candidate.RequestScope)
                {
                    return true;
                }
            }
            else if (candidate.RequestScope == RequestScope.SeasonSelection)
            {
                if (request.RequestScope == RequestScope.SeasonSelection
                    && request.RequestedSeasonNumbers.Intersect(candidate.RequestedSeasonNumbers).Any())
                {
                    return true;
                }
            }
            else if (candidate.RequestScope == RequestScope.EpisodeSelection)
            {
                if (request.RequestScope == RequestScope.EpisodeSelection
                    && request.RequestedEpisodeIds.Intersect(candidate.RequestedEpisodeIds, StringComparer.OrdinalIgnoreCase).Any())
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void Normalize(MediaRequest request)
    {
        if (request.MediaCategory == MediaCategory.All)
        {
            request.MediaCategory = string.Equals(request.MediaType, "TV Show", StringComparison.OrdinalIgnoreCase)
                ? MediaCategory.TVShows
                : MediaCategory.Movies;
        }

        request.RequestedSeasonNumbers = request.RequestedSeasonNumbers?
            .Where(value => value >= 0)
            .Distinct()
            .OrderBy(value => value)
            .ToList() ?? new List<int>();
        request.RequestedEpisodeIds = request.RequestedEpisodeIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        request.RequestedEpisodeNames = request.RequestedEpisodeNames?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        request.Note = request.Note?.Trim() ?? string.Empty;

        if (request.MediaCategory == MediaCategory.TVShows)
        {
            request.RequestKind = request.RequestScope switch
            {
                RequestScope.SeasonSelection => RequestKind.Season,
                RequestScope.EpisodeSelection => RequestKind.Episode,
                _ => RequestKind.Series
            };
        }
        else
        {
            request.RequestKind = RequestKind.Movie;
            request.RequestScope = RequestScope.EntireMovie;
        }
    }
}
