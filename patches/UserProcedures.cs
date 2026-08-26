using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Gml.Core.Launcher;
using Gml.Models.Converters;
using Gml.Models.Sessions;
using Gml.Models.User;
using GmlCore.Interfaces.Launcher;
using GmlCore.Interfaces.Procedures;
using GmlCore.Interfaces.Storage;
using GmlCore.Interfaces.User;

namespace Gml.Core.Helpers.User;

public class UserProcedures : IUserProcedures
{
    private readonly GmlManager _gmlManager;
    private readonly IGmlSettings _settings;
    private readonly IStorageService _storage;

    public UserProcedures(IGmlSettings settings, IStorageService storage, GmlManager gmlManager)
    {
        _settings = settings;
        _storage = storage;
        _gmlManager = gmlManager;
    }

    public async Task<IUser> GetAuthData(string login,
        string password,
        string device,
        string protocol,
        IPAddress? address,
        string? customUuid,
        string? hwid,
        bool isSlim)
    {
        var authUser = await _storage.GetUserAsync<AuthUser>(login, new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        }) ?? new AuthUser
        {
            Name = login
        };

        authUser.AuthHistory.Add(AuthUserHistory.Create(device, protocol, hwid, address?.ToString()));
        authUser.Uuid = customUuid ?? UsernameToUuid(login);
        authUser.ExpiredDate = DateTime.Now + TimeSpan.FromDays(30);
        authUser.Manager = _gmlManager;
        authUser.IsSlim = isSlim;

        await _storage.SetUserAsync(login, authUser.Uuid, authUser);

        return authUser;
    }

    public async Task<IUser?> GetUserByUuid(string uuid)
    {
        var user = await _storage.GetUserByUuidAsync<AuthUser>(uuid, new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        });

        if (user is not null) user.Manager = _gmlManager;

        return user;
    }

    public async Task<IUser?> GetUserByName(string userName)
    {
        return await _storage.GetUserByNameAsync<AuthUser>(userName, new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        });
    }

    public async Task<IUser?> GetUserBySkinGuid(string guid)
    {
        return await _storage.GetUserBySkinAsync<AuthUser>(guid, new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        });
    }

    public async Task<IUser?> GetUserByCloakGuid(string guid)
    {
        return await _storage.GetUserByCloakAsync<AuthUser>(guid, new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        });
    }

    public async Task<bool> ValidateUser(string userUuid, string serverUuid, string accessToken)
    {
        if (!Guid.TryParse(userUuid, out var profileId))
            return false;

        var user = await GetUserByUuid(profileId.ToString().ToUpper())
                   ?? await GetUserByUuid(profileId.ToString("N").ToUpper());
        if (user is not AuthUser authUser)
            return false;

        if (authUser.IsBanned)
            return false;

        if (string.IsNullOrEmpty(authUser.AccessToken)
            || string.IsNullOrEmpty(accessToken)
            || !FixedTimeEqualsUtf8(authUser.AccessToken, accessToken))
            return false;

        if (!TryValidateHs256Jwt(accessToken, authUser.Name, profileId))
            return false;

        authUser.ServerUuid = serverUuid;
        authUser.ServerExpiredDate = DateTime.Now.AddMinutes(1);
        authUser.ServerJoinHistory ??= new List<ServerJoinHistory>();
        authUser.ServerJoinHistory.Add(new ServerJoinHistory(serverUuid, DateTime.Now));

        await UpdateUser(authUser);
        return true;
    }

    public async Task<bool> CanJoinToServer(IUser user, string serverId)
    {
        var isSuccess = user.ServerUuid == serverId && DateTime.Now <= user.ServerExpiredDate;

        if (isSuccess)
        {
            user.ServerExpiredDate = DateTime.MinValue;
            user.ServerUuid = string.Empty;
            await UpdateUser(user);
        }

        return isSuccess;
    }

    public async Task<IReadOnlyCollection<IUser>> GetUsers()
    {
        var users = await _storage.GetUsersAsync<AuthUser>(new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        });

        foreach (var user in users) user.Manager = _gmlManager;

        return [..users];
    }

    public async Task<IReadOnlyCollection<IUser>> GetUsers(int take, int offset, string findName)
    {
        var authUsers = await _storage.GetUsersAsync<AuthUser>(new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        }, take, offset, findName).ConfigureAwait(false);

        foreach (var user in authUsers)
        {
            user.Manager = _gmlManager;
        }

        return authUsers.ToArray();
    }

    public async Task<IReadOnlyCollection<IUser>> GetUsers(IEnumerable<string> userUuids)
    {
        var users = await _storage.GetUsersAsync<AuthUser>(new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        }, userUuids).ConfigureAwait(false);

        return users.ToArray();
    }

    public Task UpdateUser(IUser user)
    {
        return _storage.SetUserAsync(user.Name, user.Uuid, (AuthUser)user);
    }

    public Task RemoveUser(IUser user)
    {
        return _storage.RemoveUserByUuidAsync(user.Uuid);
    }

    public Task StartSession(IUser user)
    {
        user.Sessions.Add(new GameSession());

        return UpdateUser(user);
    }

    public Task EndSession(IUser user)
    {
        user.Sessions.Last().EndDate = DateTimeOffset.Now;

        return UpdateUser(user);
    }

    public Task<Stream> GetSkin(IUser user)
    {
        return _gmlManager.Integrations.TextureProvider.GetSkinStream(user.TextureSkinUrl);
    }

    public Task<Stream> GetCloak(IUser user)
    {
        return _gmlManager.Integrations.TextureProvider.GetCloakStream(user.TextureCloakUrl);
    }

    public Task<Stream> GetHead(IUser user)
    {
        return _gmlManager.Integrations.TextureProvider.GetHeadByNameStream(user.Name);
    }

    public async Task<IUser?> GetUserByAccessToken(string accessToken)
    {
        var user = await _storage.GetUserByAccessToken<AuthUser>(accessToken, new JsonSerializerOptions
        {
            Converters = { new SessionConverter() }
        });

        if (user is not null) user.Manager = _gmlManager;

        return user;
    }

    public async Task BlockHardware(IEnumerable<string?> hwids)
    {
        foreach (var hwid in hwids) await _storage.AddLockedHwid(new Hardware(hwid));
    }

    public async Task UnblockHardware(IEnumerable<string?> hwids)
    {
        foreach (var hwid in hwids) await _storage.RemoveLockedHwid(new Hardware(hwid));
    }

    public Task<bool> CheckContainsHardware(IHardware hardware)
    {
        return _storage.ContainsLockedHwid(hardware);
    }

    /// <summary>
    /// HS256 + iss/aud/exp, same contract as Gml.Web.Api JwtBearer
    /// (SECURITY_KEY, JWT_ISSUER=gml-api, JWT_AUDIENCE=gml-clients).
    /// Implemented without System.IdentityModel.Tokens.Jwt so the overlaid
    /// Gml.Core.dll does not 500 join when that assembly version is missing.
    /// </summary>
    private bool TryValidateHs256Jwt(string token, string expectedName, Guid expectedUuid)
    {
        var key = _settings.SecurityKey;
        if (string.IsNullOrEmpty(key))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 3 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]) ||
            string.IsNullOrEmpty(parts[2]))
            return false;

        try
        {
            using var headerDoc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
            if (!headerDoc.RootElement.TryGetProperty("alg", out var alg)
                || !string.Equals(alg.GetString(), "HS256", StringComparison.Ordinal))
                return false;

            byte[] actual;
            byte[] expected;
            try
            {
                actual = Base64UrlDecode(parts[2]);
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
                expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]));
            }
            catch
            {
                return false;
            }

            if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
                return false;

            using var payloadDoc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            var payload = payloadDoc.RootElement;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const long skew = 60;

            if (payload.TryGetProperty("nbf", out var nbf) && nbf.TryGetInt64(out var nbfUnix) && now + skew < nbfUnix)
                return false;
            if (!payload.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expUnix) || now - skew >= expUnix)
                return false;

            var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
            if (string.IsNullOrWhiteSpace(issuer))
                issuer = "gml-api";
            if (!payload.TryGetProperty("iss", out var iss) || iss.GetString() != issuer)
                return false;

            var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
            if (string.IsNullOrWhiteSpace(audience))
                audience = "gml-clients";
            if (!payload.TryGetProperty("aud", out var aud) || aud.GetString() != audience)
                return false;

            var name = ReadClaim(payload,
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
                "unique_name",
                "name");
            if (!string.Equals(name, expectedName, StringComparison.Ordinal))
                return false;

            var subject = ReadClaim(payload,
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
                "sub");
            return Guid.TryParse(subject, out var tokenUuid) && tokenUuid == expectedUuid;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadClaim(JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetProperty(key, out var value))
            {
                var text = value.GetString();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }

        return null;
    }

    private static bool FixedTimeEqualsUtf8(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
    }

    private string UsernameToUuid(string username)
    {
        return GetOfflinePlayerUuid(username);
    }

    private string GetOfflinePlayerUuid(string username)
    {
        //new GameProfile(UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(Charsets.UTF_8)), name));
        var rawresult = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes($"OfflinePlayer:{username}"));
        //set the version to 3 -> Name based md5 hash
        rawresult[6] = (byte)((rawresult[6] & 0x0f) | 0x30);
        //IETF variant
        rawresult[8] = (byte)((rawresult[8] & 0x3f) | 0x80);
        //convert to string and remove any - if any
        var finalresult = BitConverter.ToString(rawresult).Replace("-", "");
        //formatting
        finalresult = finalresult.Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-");
        return finalresult;
    }
}
