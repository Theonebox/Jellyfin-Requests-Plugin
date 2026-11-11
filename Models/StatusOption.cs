using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Requests.Models;

/// <summary>
/// A request status the administrator can rename, recolor and give an icon. Requests store
/// <see cref="Id"/>, which never changes, so renaming a status never orphans existing requests.
/// </summary>
public sealed class StatusOption
{
    public StatusOption()
    {
    }

    public StatusOption(string id, string name, string color, string icon)
    {
        Id = id;
        Name = name;
        Color = color;
        Icon = icon;
    }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>CSS hex color, e.g. #66bb6a.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#bdbdbd";

    /// <summary>A key from <see cref="StatusIcons"/>.</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;
}
