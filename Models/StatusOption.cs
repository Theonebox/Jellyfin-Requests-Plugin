using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Requests.Models;

/// <summary>
/// A request status the administrator can rename and recolor. Requests store <see cref="Id"/>,
/// which never changes, so renaming a status never orphans existing requests.
/// </summary>
public sealed class StatusOption
{
    public StatusOption()
    {
    }

    public StatusOption(string id, string name, string color)
    {
        Id = id;
        Name = name;
        Color = color;
    }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>CSS hex color, e.g. #66bb6a.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#bdbdbd";
}
