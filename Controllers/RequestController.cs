using System.Security.Claims;
using Jellyfin.Plugin.Requests.Models;
using Jellyfin.Plugin.Requests.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[AllowAnonymous]
[Route("Requests")]
public sealed class RequestController : ControllerBase
{
    private readonly RequestService _requestService;

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
            SourceProvider = model.SourceProvider?.Trim() ?? string.Empty
        };

        var created = _requestService.Add(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
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
            Status = model.Status ?? existing.Status
        });

        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        return _requestService.Delete(id) ? NoContent() : NotFound();
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
}

public sealed class CreateRequestModel
{
    public string? MediaId { get; set; }

    public string? Title { get; set; }

    public string? MediaType { get; set; }

    public string? Overview { get; set; }

    public string? ProviderId { get; set; }

    public Dictionary<string, string>? ProviderIds { get; set; }

    public int? ProductionYear { get; set; }

    public string? SourceProvider { get; set; }
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
}
