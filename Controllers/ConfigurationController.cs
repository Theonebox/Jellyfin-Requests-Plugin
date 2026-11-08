using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[AllowAnonymous]
[Route("Requests/Configuration")]
public sealed class ConfigurationController : ControllerBase
{
    [HttpGet("QualityOptions")]
    public ActionResult<object> GetQualityOptions()
    {
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

        var options = Plugin.Instance?.Configuration.MediaQualityOptions
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string> { "720p", "1080p", "4K" };

        return Ok(new { options });
    }

    [HttpPut("QualityOptions")]
    public IActionResult UpdateQualityOptions([FromBody] QualityOptionsModel model)
    {
        if (!IsAdministrator())
        {
            return StatusCode(403, new { error = "Administrator access is required." });
        }

        if (model?.Options is null)
        {
            return BadRequest(new { error = "At least one media quality option is required." });
        }

        var options = model.Options
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return BadRequest(new { error = "At least one media quality option is required." });
        }

        if (Plugin.Instance is null)
        {
            return StatusCode(500, new { error = "Plugin configuration is unavailable." });
        }

        Plugin.Instance.Configuration.MediaQualityOptions = options;
        Plugin.Instance.SaveConfiguration();
        return Ok(new { options });
    }

    private bool IsAdministrator()
    {
        if (User.IsInRole("Administrator"))
        {
            return true;
        }

        foreach (var claim in User.Claims)
        {
            if ((claim.Type == ClaimTypes.Role || claim.Type == "IsAdministrator" || claim.Type == "UserRole")
                && (string.Equals(claim.Value, "Administrator", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Value, "True", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class QualityOptionsModel
{
    public List<string>? Options { get; set; }
}
