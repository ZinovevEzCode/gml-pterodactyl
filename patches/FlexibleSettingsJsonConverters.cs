using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GmlCore.Interfaces.Enums;

namespace Gml.Dto.Settings;

public sealed class FlexibleTimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
            case JsonTokenType.None:
                return TimeSpan.Zero;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var ticks))
                    return TimeSpan.FromTicks(ticks);
                break;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return TimeSpan.Zero;
                if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span))
                    return span;
                if (text == "24:00:00")
                    return TimeSpan.FromDays(1);
                break;
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    if (doc.RootElement.TryGetProperty("ticks", out var ticksProp) &&
                        ticksProp.TryGetInt64(out var objectTicks))
                        return TimeSpan.FromTicks(objectTicks);
                }

                return TimeSpan.Zero;
        }

        return TimeSpan.Zero;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
    }
}

public sealed class FlexibleTextureProtocolConverter : JsonConverter<TextureProtocol>
{
    public override TextureProtocol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var number) && number is 0 or 1)
                    return (TextureProtocol)number;
                return TextureProtocol.Https;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return TextureProtocol.Https;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                    parsed is 0 or 1)
                    return (TextureProtocol)parsed;
                if (text.Equals("http", StringComparison.OrdinalIgnoreCase))
                    return TextureProtocol.Http;
                if (text.Equals("https", StringComparison.OrdinalIgnoreCase))
                    return TextureProtocol.Https;
                return TextureProtocol.Https;
            default:
                return TextureProtocol.Https;
        }
    }

    public override void Write(Utf8JsonWriter writer, TextureProtocol value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}
