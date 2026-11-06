using System.Security.Claims;
using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[AllowAnonymous]
[Route("Requests")]
public sealed class RequestController : ControllerBase
{
    private readonly IRequestService _requestService;

    public RequestController()
    {
        _requestService = Plugin.Requests
            ?? throw new InvalidOperationException("The Requests plugin service has not been initialized.");
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<MediaRequest>> GetAll()
    {
        return Ok(_requestService.GetAll());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<MediaRequest> GetById(Guid id)
    {
        var request = _requestService.GetById(id);
        return request is null ? NotFound() : Ok(request);
    }

    [HttpPost]
    public ActionResult<MediaRequest> Create([FromBody] CreateRequestModel model)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.Title))
        {
            return BadRequest("A title is required.");
        }

        var request = new MediaRequest
        {
            UserId = GetUserId(),
            UserName = GetUserName(),
            MediaId = model.MediaId?.Trim() ?? string.Empty,
            Title = model.Title.Trim(),
            MediaType = model.MediaType?.Trim() ?? string.Empty,
            Overview = model.Overview?.Trim() ?? string.Empty,
            ProviderId = model.ProviderId?.Trim() ?? string.Empty,
            ProviderIds = model.ProviderIds is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(model.ProviderIds, StringComparer.OrdinalIgnoreCase),
            ProductionYear = model.ProductionYear,
            SourceProvider = model.SourceProvider?.Trim() ?? string.Empty,
            MediaCategory = model.MediaCategory ?? InferMediaCategory(model.MediaType),
            RequestScope = model.RequestScope ?? InferRequestScope(model.MediaCategory ?? InferMediaCategory(model.MediaType)),
            RequestKind = model.RequestKind ?? InferRequestKind(model.RequestScope, model.MediaCategory ?? InferMediaCategory(model.MediaType)),
            RequestedSeasonNumbers = model.RequestedSeasonNumbers?.Distinct().OrderBy(value => value).ToList() ?? new List<int>(),
            RequestedEpisodeIds = model.RequestedEpisodeIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            RequestedEpisodeNames = model.RequestedEpisodeNames?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            Note = model.Note?.Trim() ?? string.Empty
        };
        try
        {
            var created = _requestService.Add(request);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DuplicateRequestException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public ActionResult<MediaRequest> Update(Guid id, [FromBody] UpdateRequestModel model)
    {
        if (model is null)
        {
            return BadRequest();
        }

        var existing = _requestService.GetById(id);
        if (existing is null)
        {
            return NotFound();
        }

        var updated = _requestService.Update(new MediaRequest
        {
            Id = id,
            UserId = model.UserId ?? existing.UserId,
            UserName = model.UserName ?? existing.UserName,
            MediaId = model.MediaId ?? existing.MediaId,
            Title = model.Title ?? existing.Title,
            MediaType = model.MediaType ?? existing.MediaType,
            Overview = model.Overview ?? existing.Overview,
            ProviderId = model.ProviderId ?? existing.ProviderId,
            ProviderIds = model.ProviderIds ?? existing.ProviderIds,
            ProductionYear = model.ProductionYear ?? existing.ProductionYear,
            SourceProvider = model.SourceProvider ?? existing.SourceProvider,
            RequestedAtUtc = existing.RequestedAtUtc,
            Status = model.Status ?? existing.Status,
            MediaCategory = model.MediaCategory ?? existing.MediaCategory,
            RequestScope = model.RequestScope ?? existing.RequestScope,
            RequestKind = model.RequestKind ?? existing.RequestKind,
            RequestedSeasonNumbers = model.RequestedSeasonNumbers?.Distinct().OrderBy(value => value).ToList() ?? existing.RequestedSeasonNumbers,
            RequestedEpisodeIds = model.RequestedEpisodeIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? existing.RequestedEpisodeIds,
            RequestedEpisodeNames = model.RequestedEpisodeNames?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? existing.RequestedEpisodeNames,
            Note = model.Note ?? existing.Note
        });

        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        return _requestService.Delete(id) ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/status")]
    public ActionResult<MediaRequest> UpdateStatus(Guid id, [FromBody] UpdateStatusModel model)
    {
        if (model is null || !Enum.IsDefined(model.Status))
        {
            return BadRequest("A valid request status is required.");
        }

        var updated = _requestService.UpdateStatus(id, model.Status);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("duplicate-check")]
    public ActionResult<object> CheckDuplicate([FromBody] CreateRequestModel model)
    {
        if (model is null)
        {
            return BadRequest();
        }

        var candidate = new MediaRequest
        {
            MediaId = model.MediaId?.Trim() ?? string.Empty,
            ProviderId = model.ProviderId?.Trim() ?? string.Empty,
            MediaCategory = model.MediaCategory ?? InferMediaCategory(model.MediaType),
            RequestScope = model.RequestScope ?? InferRequestScope(model.MediaCategory ?? InferMediaCategory(model.MediaType)),
            RequestedSeasonNumbers = model.RequestedSeasonNumbers?.ToList() ?? new List<int>(),
            RequestedEpisodeIds = model.RequestedEpisodeIds?.ToList() ?? new List<string>()
        };

        return Ok(new { duplicate = _requestService.IsDuplicate(candidate) });
    }

    private string GetUserId()
    {
        var claims = User.Claims;
        foreach (var claim in claims)
        {
            if ((claim.Type == ClaimTypes.NameIdentifier || claim.Type == "UserId" || claim.Type == "uid")
                && !string.IsNullOrWhiteSpace(claim.Value))
            {
                return claim.Value.Trim();
            }
        }

        return string.Empty;
    }

    private string GetUserName()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("UserName")?.Value
            ?? string.Empty;
    }

    private static MediaCategory InferMediaCategory(string? mediaType)
    {
        return string.Equals(mediaType, "TV Show", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "TV", StringComparison.OrdinalIgnoreCase)
            ? MediaCategory.TVShows
            : MediaCategory.Movies;
    }

    private static RequestScope InferRequestScope(MediaCategory category)
    {
        return category == MediaCategory.TVShows ? RequestScope.EntireSeries : RequestScope.EntireMovie;
    }

    private static RequestKind InferRequestKind(RequestScope? scope, MediaCategory category)
    {
        if (category == MediaCategory.TVShows)
        {
            return scope switch
            {
                RequestScope.SeasonSelection => RequestKind.Season,
                RequestScope.EpisodeSelection => RequestKind.Episode,
                _ => RequestKind.Series
            };
        }

        return RequestKind.Movie;
    }
}

public sealed class CreateRequestModel
{
    [JsonPropertyName("mediaId")]
    public string? MediaId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("providerId")]
    public string? ProviderId { get; set; }

    [JsonPropertyName("providerIds")]
    public Dictionary<string, string>? ProviderIds { get; set; }

    [JsonPropertyName("productionYear")]
    public int? ProductionYear { get; set; }

    [JsonPropertyName("sourceProvider")]
    public string? SourceProvider { get; set; }

    public MediaCategory? MediaCategory { get; set; }

    public RequestScope? RequestScope { get; set; }

    public RequestKind? RequestKind { get; set; }

    public List<int>? RequestedSeasonNumbers { get; set; }

    public List<string>? RequestedEpisodeIds { get; set; }

    public List<string>? RequestedEpisodeNames { get; set; }

    public string? Note { get; set; }
}

public sealed class UpdateRequestModel
{
    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public string? MediaId { get; set; }

    public string? Title { set; get; }

    public string? MediaType { get; set; }

    public string? Overview { get; set; }

    public string? ProviderId { get; set; }

    public Dictionary<string, string>? ProviderIds { get; set; }

    public int? ProductionYear { get; set; }

    public string? SourceProvider { get; set; }

    public RequestStatus? Status { get; set; }

    public MediaCategory? MediaCategory { get; set; }

    public RequestScope? RequestScope { get; set; }

    public RequestKind? RequestKind { get; set; }

    public List<int>? RequestedSeasonNumbers { get; set; }

    public List<string>? RequestedEpisodeIds { get; set; }

    public List<string>? RequestedEpisodeNames { get; set; }

    public string? Note { get; set; }
}

public sealed class UpdateStatusModel
{
    public RequestStatus Status { get; set; }
}
