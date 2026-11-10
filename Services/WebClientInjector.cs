using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Services;

/// <summary>
/// Adds the Requests header-button script to Jellyfin's web client index.html.
/// </summary>
/// <remarks>
/// Two independent mechanisms use this, so the button shows up on as many setups as possible:
/// <list type="bullet">
/// <item><see cref="NavigationStartupFilter"/> rewrites index.html as it is served.</item>
/// <item><see cref="PatchIndexFile"/> writes the tag into index.html on disk at start-up, which also
/// covers reverse proxies / clients that cache the page and setups where the middleware does not run.</item>
/// </list>
/// The script tag is idempotent (marked with <c>data-requests-plugin</c>) and the script itself guards
/// against running twice.
/// </remarks>
public static partial class WebClientInjector
{
    private const string Marker = "data-requests-plugin";

    public static string ScriptTag =>
        $"<script {Marker} defer src=\"../Requests/Web/requests-navigation.js?v={Version}\"></script>";

    private static string Version => typeof(WebClientInjector).Assembly.GetName().Version?.ToString() ?? "1";

    /// <summary>Returns <paramref name="html"/> with exactly one current Requests script tag before &lt;/body&gt;.</summary>
    public static string Inject(string html, out bool changed)
    {
        var cleaned = ExistingTagRegex().Replace(html, string.Empty);
        var bodyClose = cleaned.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClose < 0)
        {
            changed = false;
            return html;
        }

        var result = cleaned.Insert(bodyClose, ScriptTag);
        changed = !string.Equals(result, html, StringComparison.Ordinal);
        return result;
    }

    public static string Remove(string html) => ExistingTagRegex().Replace(html, string.Empty);

    public static void PatchIndexFile(string? webPath, ILogger logger)
    {
        var indexPath = GetIndexPath(webPath);
        if (indexPath is null)
        {
            logger.LogInformation("Requests: web client index.html not found; relying on response injection for the header button.");
            return;
        }

        try
        {
            var original = File.ReadAllText(indexPath, Encoding.UTF8);
            var updated = Inject(original, out var changed);
            if (changed)
            {
                File.WriteAllText(indexPath, updated, new UTF8Encoding(false));
                logger.LogInformation("Requests: added the header button script to {Path}.", indexPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogInformation(
                "Requests: index.html at {Path} is read-only ({Message}); relying on response injection for the header button.",
                indexPath,
                exception.Message);
        }
    }

    public static void UnpatchIndexFile(string? webPath, ILogger? logger)
    {
        var indexPath = GetIndexPath(webPath);
        if (indexPath is null)
        {
            return;
        }

        try
        {
            var original = File.ReadAllText(indexPath, Encoding.UTF8);
            var updated = Remove(original);
            if (!string.Equals(original, updated, StringComparison.Ordinal))
            {
                File.WriteAllText(indexPath, updated, new UTF8Encoding(false));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Requests: could not remove the header button script from {Path}: {Message}", indexPath, exception.Message);
        }
    }

    private static string? GetIndexPath(string? webPath)
    {
        if (string.IsNullOrWhiteSpace(webPath))
        {
            return null;
        }

        var indexPath = Path.Combine(webPath, "index.html");
        return File.Exists(indexPath) ? indexPath : null;
    }

    [GeneratedRegex(@"<script[^>]*data-requests-plugin[^>]*>\s*</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ExistingTagRegex();
}
