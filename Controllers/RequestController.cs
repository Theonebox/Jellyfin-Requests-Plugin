using System.Text.Json.Serialization;
using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

/// <summary>
/// Request endpoints. Every signed-in user can create, list and cancel their own (pending)
/// requests; listing everything and changing status is administrator-only.
/// </summary>
[ApiController]
[Authorize]
[Route("Requests")]
public sealed class RequestController : ControllerBase
{
    private readonly RequestService _requests;
    private readonly IUserManager _userManager;

    public RequestController(IUserManager userManager)
    {
        _requests = Plugin.Requests
            ?? throw new InvalidOperationException("The Requests plugin service has not been initialized.");
        _userManager = userManager;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<MediaRequest>> GetAll()
    {
        if (!User.IsAdministrator())
        {
            return Forbidden("Administrator access is required.");
        }

        return Ok(_requests.GetAll());
    }

    [HttpGet("my")]
    public ActionResult<IReadOnlyList<MediaRequest>> GetMine()
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new { error = "Authentication is required." });
        }

        return Ok(_requests.GetAll().Where(request => IsOwner(request, userId.Value)).ToList());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<MediaRequest> GetById(Guid id)
    {
        var request = _requests.GetById(id);
        if (request is null)
        {
            return NotFound();
        }

        var userId = CurrentUserId();
        if (!User.IsAdministrator() && (userId is null || !IsOwner(request, userId.Value)))
        {
            return Forbidden("You can only view your own requests.");
        }

        return Ok(request);
    }

    [HttpPost]
    public ActionResult<MediaRequest> Create([FromBody] CreateRequestModel model)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.Title))
        {
            return BadRequest(new { error = "A title is required." });
        }

        var userId = CurrentUserId();
        var userName = User.GetUserName();
        if (userId is null || string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized(new { error = "Jellyfin authentication is required to submit a request." });
        }

        var category = model.MediaCategory
            ?? (model.MediaType?.StartsWith("TV", StringComparison.OrdinalIgnoreCase) == true ? MediaCategory.TVShows : MediaCategory.Movies);
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is not null
            && ((category == MediaCategory.Movies && !configuration.EnableMovies)
                || (category == MediaCategory.TVShows && !configuration.EnableTvShows)))
        {
            return BadRequest(new { error = "Requests for this media type are currently disabled." });
        }

        var request = new MediaRequest
        {
            UserId = userId.Value.ToString("N"),
            UserName = userName,
            MediaId = model.MediaId?.Trim() ?? string.Empty,
            Title = model.Title.Trim(),
            MediaType = category == MediaCategory.TVShows ? "TV Show" : "Movie",
            Overview = model.Overview?.Trim() ?? string.Empty,
            PosterUrl = model.PosterUrl?.Trim() ?? string.Empty,
            ProviderId = model.ProviderId?.Trim() ?? string.Empty,
            ProviderIds = model.ProviderIds ?? new Dictionary<string, string>(),
            ProductionYear = model.ProductionYear,
            SourceProvider = model.SourceProvider?.Trim() ?? string.Empty,
            MediaCategory = category,
            RequestScope = model.RequestScope ?? (category == MediaCategory.TVShows ? RequestScope.EntireSeries : RequestScope.EntireMovie),
            RequestedSeasonNumbers = model.RequestedSeasonNumbers ?? new List<int>(),
            RequestedEpisodeIds = model.RequestedEpisodeIds ?? new List<string>(),
            RequestedEpisodeNames = model.RequestedEpisodeNames ?? new List<string>(),
            Note = model.Note ?? string.Empty,
            MediaQuality = model.MediaQuality ?? string.Empty
        };

        try
        {
            var created = _requests.Add(request);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DuplicateRequestException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        var existing = _requests.GetById(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!User.IsAdministrator())
        {
            var userId = CurrentUserId();
            if (userId is null || !IsOwner(existing, userId.Value))
            {
                return Forbidden("You can only cancel your own requests.");
            }

            if (!string.Equals(existing.Status, RequestService.InitialStatusId, StringComparison.Ordinal))
            {
                return Conflict(new { error = "Only requests that have not been processed yet can be cancelled." });
            }
        }

        return _requests.Delete(id) ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/status")]
    public ActionResult<MediaRequest> UpdateStatus(Guid id, [FromBody] UpdateStatusModel model)
    {
        if (!User.IsAdministrator())
        {
            return Forbidden("Administrator access is required.");
        }

        var validIds = Plugin.Instance?.Configuration.GetStatusOptions().Select(option => option.Id) ?? Array.Empty<string>();
        if (model is null || string.IsNullOrWhiteSpace(model.Status) || !validIds.Contains(model.Status, StringComparer.Ordinal))
        {
            return BadRequest(new { error = "A valid request status is required." });
        }

        var updated = _requests.UpdateStatus(id, model.Status);
        return updated is null ? NotFound() : Ok(updated);
    }

    private ObjectResult Forbidden(string message) => StatusCode(StatusCodes.Status403Forbidden, new { error = message });

    private Guid? CurrentUserId()
    {
        var userId = User.GetUserId();
        if (userId is null && User.GetUserName() is { Length: > 0 } name)
        {
            userId = _userManager.GetUserByName(name)?.Id;
        }

        return userId;
    }

    // Stored IDs may be in "N" or "D" format (older versions), so compare as GUIDs.
    private static bool IsOwner(MediaRequest request, Guid userId)
        => Guid.TryParse(request.UserId, out var owner) && owner == userId;
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

    [JsonPropertyName("mediaCategory")]
    public MediaCategory? MediaCategory { get; set; }

    [JsonPropertyName("requestScope")]
    public RequestScope? RequestScope { get; set; }

    [JsonPropertyName("requestedSeasonNumbers")]
    public List<int>? RequestedSeasonNumbers { get; set; }

    [JsonPropertyName("requestedEpisodeIds")]
    public List<string>? RequestedEpisodeIds { get; set; }

    [JsonPropertyName("requestedEpisodeNames")]
    public List<string>? RequestedEpisodeNames { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("mediaQuality")]
    public string? MediaQuality { get; set; }
}

public sealed class UpdateStatusModel
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
