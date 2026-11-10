using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

/// <summary>
/// Serves the static files for the public Requests page. These are anonymous because they are
/// loaded by the web client before/independently of the API session; all data endpoints they
/// call require an authenticated Jellyfin user.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("Requests/Web")]
public sealed class WebController : ControllerBase
{
    private static readonly Assembly Assembly = typeof(WebController).Assembly;
    private static readonly string Root = typeof(Plugin).Namespace!;

    private static readonly Dictionary<string, (string Resource, string ContentType)> Files =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["requests.html"] = ($"{Root}.Web.requests.html", "text/html; charset=utf-8"),
            ["requests.css"] = ($"{Root}.Web.requests.css", "text/css; charset=utf-8"),
            ["requests.js"] = ($"{Root}.Web.requests.js", "application/javascript; charset=utf-8"),
            ["requests-navigation.js"] = ($"{Root}.Navigation.Requests-navigation.js", "application/javascript; charset=utf-8")
        };

    [HttpGet("{fileName}")]
    public IActionResult GetFile(string fileName)
    {
        if (!Files.TryGetValue(fileName, out var file))
        {
            return NotFound();
        }

        using var stream = Assembly.GetManifestResourceStream(file.Resource);
        if (stream is null)
        {
            return NotFound();
        }

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        // URLs carry ?v=<plugin version>, so a long cache is safe and a plugin update busts it.
        Response.Headers.CacheControl = Request.Query.ContainsKey("v")
            ? "public, max-age=86400"
            : "no-cache";
        return File(bytes, file.ContentType);
    }
}
