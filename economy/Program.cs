using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Andline.Economy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var securityKey = Environment.GetEnvironmentVariable("SECURITY_KEY") ?? string.Empty;
if (string.IsNullOrWhiteSpace(securityKey))
{
    Console.Error.WriteLine("SECURITY_KEY is required.");
    return 1;
}

var issuer = FirstNonEmpty(Environment.GetEnvironmentVariable("JWT_ISSUER"), "gml-api");
var audience = FirstNonEmpty(Environment.GetEnvironmentVariable("JWT_AUDIENCE"), "gml-clients");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddHttpClient<ShopWalletClient>();
builder.Services.AddHealthChecks();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityKey));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Player", policy => policy.RequireRole("Player"));
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");

app.MapGet("/api/v1/users/me", async (
        ClaimsPrincipal user,
        ShopWalletClient shop,
        CancellationToken cancellationToken) =>
    {
        var uuid = FirstNonEmpty(
            user.FindFirstValue(ClaimTypes.NameIdentifier),
            user.FindFirstValue(JwtRegisteredClaimNames.Sub),
            user.FindFirstValue("sub"));
        var name = FirstNonEmpty(
            user.FindFirstValue(ClaimTypes.Name),
            user.FindFirstValue(JwtRegisteredClaimNames.UniqueName),
            user.Identity?.Name);

        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
        {
            return Results.Json(new ResponseEnvelope<object>
            {
                Status = "Unauthorized",
                StatusCode = 401,
                Message = "В токене нет uuid или имени игрока"
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var shopWallet = await shop.GetAsync(uuid, name, cancellationToken);
        var economy = new PlayerEconomy
        {
            Uuid = uuid,
            Name = name,
            Souls = shopWallet?.Souls ?? 0,
            Rubles = shopWallet?.Rubles ?? 0,
            Privileges = MapPrivileges(shopWallet)
        };

        return Results.Json(new ResponseEnvelope<PlayerEconomy>
        {
            Status = "OK",
            StatusCode = 200,
            Message = shop.IsConfigured && shopWallet is null
                ? "Магазин недоступен, показан пустой кошелёк"
                : string.Empty,
            Data = economy
        });
    })
    .RequireAuthorization("Player");

app.Run();
return 0;

static List<ServerPrivilege> MapPrivileges(ShopWallet? shopWallet)
{
    if (shopWallet?.Privileges is not { Count: > 0 })
        return [];

    return shopWallet.Privileges
        .Select(item =>
        {
            var server = FirstNonEmpty(item.Server, item.Profile);
            return new ServerPrivilege
            {
                Server = server,
                Code = string.IsNullOrWhiteSpace(item.Code) ? "none" : item.Code.Trim(),
                Label = string.IsNullOrWhiteSpace(item.Label) ? "Игрок" : item.Label.Trim(),
                Coins = item.Coins
            };
        })
        .Where(item => !string.IsNullOrWhiteSpace(item.Server))
        .ToList();
}

static string FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
    }

    return string.Empty;
}
