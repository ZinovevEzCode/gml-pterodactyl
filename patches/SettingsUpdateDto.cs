using System;
using System.Text.Json.Serialization;
using GmlCore.Interfaces.Enums;

namespace Gml.Dto.Settings;

public class SettingsUpdateDto
{
    public bool RegistrationIsEnabled { get; set; }
    public int StorageType { get; set; }
    public string StorageHost { get; set; } = string.Empty;
    public string StorageLogin { get; set; } = string.Empty;
    public string CurseForgeKey { get; set; } = string.Empty;
    public string VkKey { get; set; } = string.Empty;
    public string StoragePassword { get; set; } = string.Empty;

    [JsonConverter(typeof(FlexibleTextureProtocolConverter))]
    public TextureProtocol TextureProtocol { get; set; }

    public bool SentryNeedAutoClear { get; set; }

    [JsonConverter(typeof(FlexibleTimeSpanConverter))]
    public TimeSpan SentryAutoClearPeriod { get; set; }
}
