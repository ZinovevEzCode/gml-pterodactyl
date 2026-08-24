using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;

namespace Gml.Web.Proxy;

/// <summary>
/// Dashboard PUT /settings/platform sends a TimeSpan as a string. The GML API
/// rejects empty / ISO / unmatched Select values with 400, so the registration
/// checkbox never saves. Normalize the JSON body before it reaches Kestrel.
/// </summary>
internal static class SettingsPlatformPayloadNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryNormalize(string json, out string normalized)
    {
        normalized = json;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject obj)
            return false;

        foreach (var key in new[] { "storageHost", "storageLogin", "storagePassword", "curseForgeKey", "vkKey" })
        {
            if (obj[key] is null)
                obj[key] = string.Empty;
        }

        obj["textureProtocol"] = NormalizeProtocol(obj["textureProtocol"]);
        obj["storageType"] = NormalizeInt(obj["storageType"], fallback: 0);
        obj["sentryAutoClearPeriod"] = NormalizeTimeSpan(obj["sentryAutoClearPeriod"]);
        obj["registrationIsEnabled"] = NormalizeBool(obj["registrationIsEnabled"]);
        obj["sentryNeedAutoClear"] = NormalizeBool(obj["sentryNeedAutoClear"]);

        normalized = obj.ToJsonString(JsonOptions);
        return !string.Equals(json, normalized, StringComparison.Ordinal);
    }

    private static JsonNode NormalizeProtocol(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var number))
                return number is 0 or 1 ? number : 1;

            if (value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                    parsed is 0 or 1)
                    return parsed;

                if (text.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("https", StringComparison.OrdinalIgnoreCase))
                    return text.Equals("https", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
        }

        return 1;
    }

    private static JsonNode NormalizeInt(JsonNode? node, int fallback)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var number))
                return number;

            if (value.TryGetValue<string>(out var text) &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return fallback;
    }

    private static JsonNode NormalizeBool(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var flag))
                return flag;

            if (value.TryGetValue<int>(out var number))
                return number != 0;

            if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
                return parsed;
        }

        return false;
    }

    private static JsonNode NormalizeTimeSpan(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text) && TryParseTimeSpan(text, out var span))
                return Format(span);

            if (value.TryGetValue<long>(out var ticks) && ticks >= 0 && ticks < TimeSpan.MaxValue.Ticks)
                return Format(TimeSpan.FromTicks(ticks));
        }

        if (node is JsonObject obj && obj["ticks"] is JsonValue ticksValue &&
            ticksValue.TryGetValue<long>(out var objectTicks))
            return Format(TimeSpan.FromTicks(objectTicks));

        return "00:05:00";
    }

    internal static bool TryParseTimeSpan(string? text, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();

        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out span))
            return true;

        // "24:00:00" is invalid for TimeSpan (hours must be 0–23).
        if (text == "24:00:00")
        {
            span = TimeSpan.FromDays(1);
            return true;
        }

        if (text.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                span = XmlConvert.ToTimeSpan(text);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }

    private static string Format(TimeSpan span) => span.ToString("c", CultureInfo.InvariantCulture);
}
