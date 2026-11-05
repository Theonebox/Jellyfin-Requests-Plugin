using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Controllers;

[ApiController]
[Route("Requests/Web")]
public sealed class WebController : ControllerBase
{
    private static readonly Assembly Assembly = typeof(WebController).Assembly;

    [HttpGet("{fileName}")]
    public IActionResult GetFile(string fileName)
    {
        var allowedFiles = new Dictionary<string, (string Resource, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["requests.css"] = ($"{typeof(WebController).Namespace!.Replace(".Controllers", "")}.Web.requests.css", "text/css; charset=utf-8"),
            ["requests.js"] = ($"{typeof(WebController).Namespace!.Replace(".Controllers", "")}.Web.requests.js", "application/javascript; charset=utf-8")
        };

        if (!allowedFiles.TryGetValue(fileName, out var file))
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
        return File(bytes, file.ContentType);
    }
}
