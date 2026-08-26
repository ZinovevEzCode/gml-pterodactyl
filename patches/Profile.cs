using System;
using System.Collections.Generic;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gml.Dto.Minecraft.AuthLib;

public class Profile
{
    private string _id = string.Empty;

    [JsonProperty("id")]
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    [JsonConverter(typeof(UndashedUuidNewtonsoftConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(UndashedUuidStjConverter))]
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

/// <summary>
/// Yggdrasil profile ids are 32 lowercase hex chars with no hyphens.
/// BungeeCord/NullCordX parse that with Util.getUUID; a dashed UUID throws
/// NumberFormatException on the first hyphen (substring(0, 16) of
/// "3ea1d2ee-d472-32..." is "3ea1d2ee-d472-32").
/// </summary>
public sealed class UndashedUuidStjConverter : System.Text.Json.Serialization.JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return MinecraftUuidFormat.Undashed(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(MinecraftUuidFormat.Undashed(value));
    }
}

public sealed class UndashedUuidNewtonsoftConverter : Newtonsoft.Json.JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(string);

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        return MinecraftUuidFormat.Undashed(JToken.Load(reader)?.ToString());
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteValue(MinecraftUuidFormat.Undashed(value as string));
    }
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
