using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

public sealed class RequestService
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
            request.Status = update.Status;
            Save();
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

            return JsonSerializer.Deserialize<List<MediaRequest>>(json, _jsonOptions) ?? new List<MediaRequest>();
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
            RequestedAtUtc = request.RequestedAtUtc,
            Status = request.Status
        };
    }
}
