using System.Reflection;
using Jellyfin.Plugin.Requests.Models;

namespace Jellyfin.Plugin.Requests.Services;

public static class NavigationTransformation
{
    public static string IndexHtml(PatchRequestPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(payload.Contents)
            || payload.Contents.Contains("requests-navigation-link", StringComparison.Ordinal))
        {
            return payload.Contents ?? string.Empty;
        }

        using var stream = typeof(NavigationTransformation).Assembly.GetManifestResourceStream(
            $"{typeof(Plugin).Namespace}.Navigation.Requests-navigation.js")
            ?? throw new InvalidOperationException("The Requests navigation script resource is missing.");
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        return payload.Contents.Replace(
            "</body>",
            $"<script defer>{script}</script></body>",
            StringComparison.OrdinalIgnoreCase);
    }
}
