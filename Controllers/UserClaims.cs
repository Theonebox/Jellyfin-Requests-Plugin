using System.Security.Claims;

namespace Jellyfin.Plugin.Requests.Controllers;

/// <summary>Reads the Jellyfin user from the claims set by Jellyfin's authentication handler.</summary>
internal static class UserClaims
{
    private const string UserIdClaim = "Jellyfin-UserId";

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole("Administrator") || user.HasClaim(ClaimTypes.Role, "Administrator");

    public static Guid? GetUserId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst(UserIdClaim)?.Value, out var id) ? id : null;

    public static string? GetUserName(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
}
