using System.Text.Json.Serialization;

namespace Andline.Economy;

public sealed class ResponseEnvelope<T>
{
    public string Status { get; set; } = "OK";
    public int StatusCode { get; set; } = 200;
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

public sealed class PlayerEconomy
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Souls { get; set; }
    public int Rubles { get; set; }
    public List<ServerPrivilege> Privileges { get; set; } = [];
}

public sealed class ServerPrivilege
{
    public string Server { get; set; } = string.Empty;
    public string Code { get; set; } = "none";
    public string Label { get; set; } = "Игрок";
    public int Coins { get; set; }
}

public sealed class ShopWallet
{
    public int Souls { get; set; }
    public int Rubles { get; set; }

    [JsonPropertyName("privileges")]
    public List<ShopPrivilege>? Privileges { get; set; }
}

public sealed class ShopPrivilege
{
    public string? Server { get; set; }
    public string? Profile { get; set; }
    public string? Code { get; set; }
    public string? Label { get; set; }
    public int Coins { get; set; }
}
