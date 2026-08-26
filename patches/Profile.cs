using System.Collections.Generic;
using Newtonsoft.Json;

namespace Gml.Dto.Minecraft.AuthLib;

public class Profile
{
    private string _id = string.Empty;

    [JsonProperty("id")]
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set => _id = MinecraftUuidFormat.Undashed(value);
    }

    [JsonProperty("name")]
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonProperty("properties")]
    [System.Text.Json.Serialization.JsonPropertyName("properties")]
    public List<ProfileProperties> Properties { get; set; }
}

internal static class MinecraftUuidFormat
{
    public static string Undashed(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid))
        {
            return uuid ?? string.Empty;
        }

        return uuid.Replace("-", string.Empty).ToLowerInvariant();
    }
}
