using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace CS2SimpleNightvision;

public class CS2SimpleNightvisionConfig : BasePluginConfig
{
    [JsonPropertyName("Chat Prefix")]
    public string ChatPrefix { get; set; } = "[Nightvision]";

    [JsonPropertyName("Default Intensity")]
    public float DefaultIntensity { get; set; } = 3.0f;

    [JsonPropertyName("Minimum Intensity")]
    public float MinimumIntensity { get; set; } = 0.1f;

    [JsonPropertyName("Maximum Intensity")]
    public float MaximumIntensity { get; set; } = 5.0f;
}
