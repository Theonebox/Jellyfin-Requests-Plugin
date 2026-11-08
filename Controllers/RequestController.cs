using System.Security.Claims;
using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[AllowAnonymous]
[Route("Requests")]
public sealed class RequestController : ControllerBase
{
    private readonly IRequestService _requestService;
    private readonly IUserManager _userManager;

    public RequestController(IUserManager userManager)
    {
        _requestService = Plugin.Requests
            ?? throw new InvalidOperationException("The Requests plugin service has not been initialized.");
        _userManager = userManager;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<MediaRequest>> GetAll()
    {
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

        var requests = _requestService.GetAll();
        return Ok(requests);
    }

    [HttpGet("my")]
    public ActionResult<IReadOnlyList<MediaRequest>> GetMyRequests()
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "Authentication is required." });
        }

        var requests = _requestService.GetAll()
            .Where(request => string.Equals(request.UserId, userId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Ok(requests);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<MediaRequest> GetById(Guid id)
    {
        if (!IsAdministrator() && string.IsNullOrWhiteSpace(GetUserId()))
        {
            return Unauthorized();
        }

        var request = _requestService.GetById(id);
        if (request is null)
        {
            return NotFound();
        }

        var userId = GetUserId();
        if (!IsAdministrator() && !string.Equals(request.UserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new { error = "You can only view your own requests." });
        }

        return Ok(request);
    }

    [HttpPost]
    public ActionResult<MediaRequest> Create([FromBody] CreateRequestModel model)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.Title))
        {
            return BadRequest("A title is required.");
        }

        var userId = GetUserId();
        var userName = GetUserName();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized(new { error = "Jellyfin authentication is required to submit a request." });
        }

        var request = new MediaRequest
        {
            UserId = userId,
            UserName = userName,
            MediaId = model.MediaId?.Trim() ?? string.Empty,
            Title = model.Title.Trim(),
            MediaType = model.MediaType?.Trim() ?? string.Empty,
            Overview = model.Overview?.Trim() ?? string.Empty,
            PosterUrl = model.PosterUrl?.Trim() ?? string.Empty,
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
            Note = model.Note?.Trim() ?? string.Empty,
            MediaQuality = model.MediaQuality?.Trim() ?? string.Empty
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
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

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
            UserId = existing.UserId,
            UserName = existing.UserName,
            MediaId = model.MediaId ?? existing.MediaId,
            Title = model.Title ?? existing.Title,
            MediaType = model.MediaType ?? existing.MediaType,
            Overview = model.Overview ?? existing.Overview,
            PosterUrl = model.PosterUrl ?? existing.PosterUrl,
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
            Note = model.Note ?? existing.Note,
            MediaQuality = model.MediaQuality?.Trim() ?? existing.MediaQuality
        });

        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        var existing = _requestService.GetById(id);
        if (existing is null)
        {
            return NotFound();
        }

        var userId = GetUserId();
        if (!IsAdministrator()
            && (string.IsNullOrWhiteSpace(userId)
                || !string.Equals(existing.UserId, userId, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(403, new { error = "You can only cancel your own requests." });
        }

        if (!IsAdministrator() && existing.Status != RequestStatus.Pending)
        {
            return StatusCode(409, new { error = "Only pending requests can be cancelled." });
        }

        return _requestService.Delete(id) ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/status")]
    public ActionResult<MediaRequest> UpdateStatus(Guid id, [FromBody] UpdateStatusModel model)
    {
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

        if (model is null || !Enum.IsDefined(model.Status))
        {
            return BadRequest("A valid request status is required.");
        }

        var updated = _requestService.UpdateStatus(id, model.Status);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("library-status")]
    public ActionResult<object> GetLibraryStatus(
        [FromQuery] string[]? providerIds,
        [FromQuery] string? providerId)
    {
        var availability = Plugin.LibraryAvailability;
        var ids = (providerIds ?? Array.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(providerId) ? Array.Empty<string>() : new[] { providerId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var inLibrary = availability?.IsInLibrary(ids) ?? false;
        return Ok(new { inLibrary, status = inLibrary ? "In Library" : "" });
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
            ProviderIds = model.ProviderIds is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(model.ProviderIds, StringComparer.OrdinalIgnoreCase),
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
            if ((claim.Type == ClaimTypes.NameIdentifier || claim.Type == "UserId" || claim.Type == "uid"
                    || claim.Type.EndsWith("/nameidentifier", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(claim.Value))
            {
                return claim.Value.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(User.Identity?.Name))
        {
            var user = _userManager.GetUserByName(User.Identity.Name);
            return user?.Id.ToString() ?? string.Empty;
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

        var claimName = User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("UserName")?.Value
            ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(claimName))
        {
            return claimName;
        }

        var userId = GetUserId();
        if (Guid.TryParse(userId, out var parsedUserId))
        {
            var user = _userManager.GetUserById(parsedUserId);
            return user?.Username ?? string.Empty;
        }

        return string.Empty;
    }

    private bool IsAdministrator()
    {
        if (User.IsInRole("Administrator"))
        {
            return true;
        }

        foreach (var claim in User.Claims)
        {
            if ((claim.Type == ClaimTypes.Role || claim.Type == "IsAdministrator"
                    || claim.Type == "UserRole")
                && (string.Equals(claim.Value, "Administrator", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Value, "True", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
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

    [JsonPropertyName("posterUrl")]
    public string? PosterUrl { get; set; }

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

    public string? MediaQuality { get; set; }
}

public sealed class UpdateRequestModel
{
    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public string? MediaId { get; set; }

    public string? Title { set; get; }

    public string? MediaType { get; set; }

    public string? Overview { get; set; }

    public string? PosterUrl { get; set; }

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

    public string? MediaQuality { get; set; }
}

public sealed class UpdateStatusModel
{
    public RequestStatus Status { get; set; }
}
