using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace Nightvision;

public class NightvisionConfig : BasePluginConfig
{
    [JsonPropertyName("Default Intensity")]
    public float DefaultIntensity { get; set; } = 3.0f;

    [JsonPropertyName("Minimum Intensity")]
    public float MinimumIntensity { get; set; } = 0.1f;

    [JsonPropertyName("Maximum Intensity")]
    public float MaximumIntensity { get; set; } = 5.0f;
}
