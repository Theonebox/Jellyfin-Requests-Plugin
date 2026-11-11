using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Requests.Models;

/// <summary>A quality users can pick, with an optional description shown on the Requests page.</summary>
public sealed class QualityOption
{
    public QualityOption()
    {
    }

    public QualityOption(string name, string description)
    {
        Name = name;
        Description = description;
    }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
