using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sunshine.Models;

namespace Sunshine.Services;

public sealed record DeviceCodeInfo(string UserCode, string VerificationUri);

public sealed class MicrosoftAuthException(string message) : Exception(message);

/// <summary>
/// Official Microsoft account sign-in for Minecraft: OAuth device-code flow against the
/// Microsoft identity platform, then Xbox Live -> XSTS -> Minecraft services.
/// Requires an Azure app registration (public client flows enabled) that Mojang has
/// approved for the Minecraft API; its client ID is read from settings.
/// </summary>
public sealed class MicrosoftAuth(string clientId)
{
    private const string Scope = "XboxLive.signin offline_access";
    private const string OAuthBase = "https://login.microsoftonline.com/consumers/oauth2/v2.0";

    private static readonly HttpClient Http = CreateClient();

    public async Task<MicrosoftAccount> SignInAsync(Action<DeviceCodeInfo> onCode, CancellationToken ct)
    {
        using var dc = await PostFormAsync($"{OAuthBase}/devicecode", new()
        {
            ["client_id"] = clientId,
            ["scope"] = Scope,
        }, ct);
        var codeRoot = dc.RootElement;
        ThrowIfOAuthError(codeRoot);

        var deviceCode = RequireString(codeRoot, "device_code");
        var interval = codeRoot.TryGetProperty("interval", out var i) ? i.GetInt32() : 5;
        var expiresAt = DateTime.UtcNow.AddSeconds(codeRoot.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 900);
        onCode(new DeviceCodeInfo(RequireString(codeRoot, "user_code"), RequireString(codeRoot, "verification_uri")));

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            if (DateTime.UtcNow > expiresAt)
                throw new MicrosoftAuthException("The sign-in code expired. Try again.");

            using var tok = await PostFormAsync($"{OAuthBase}/token", new()
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = clientId,
                ["device_code"] = deviceCode,
            }, ct);
            var root = tok.RootElement;

            switch (GetString(root, "error"))
            {
                case null:
                    return await CompleteAsync(RequireString(root, "access_token"), RequireString(root, "refresh_token"), ct);
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "authorization_declined":
                    throw new MicrosoftAuthException("Sign-in was declined.");
                case "expired_token":
                    throw new MicrosoftAuthException("The sign-in code expired. Try again.");
                default:
                    ThrowIfOAuthError(root);
                    break;
            }
        }
    }

    /// <summary>Gets a fresh Minecraft token using the stored refresh token.</summary>
    public async Task<MicrosoftAccount> RefreshAsync(MicrosoftAccount account, CancellationToken ct)
    {
        string refreshToken;
        try { refreshToken = Unprotect(account.ProtectedRefreshToken); }
        catch { throw new MicrosoftAuthException($"Saved login for {account.Name} can't be read. Sign in again."); }

        using var tok = await PostFormAsync($"{OAuthBase}/token", new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        }, ct);
        var root = tok.RootElement;
        if (GetString(root, "error") == "invalid_grant")
            throw new MicrosoftAuthException($"Login for {account.Name} expired. Sign in again.");
        ThrowIfOAuthError(root);

        // Microsoft may or may not rotate the refresh token.
        return await CompleteAsync(RequireString(root, "access_token"), GetString(root, "refresh_token") ?? refreshToken, ct);
    }

    public static string GetAccessToken(MicrosoftAccount account) => Unprotect(account.ProtectedAccessToken);

    private static async Task<MicrosoftAccount> CompleteAsync(string msAccessToken, string msRefreshToken, CancellationToken ct)
    {
        // 1. Xbox Live user token.
        using var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msAccessToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        }, ct, "Xbox Live sign-in failed");
        var xblToken = RequireString(xbl.RootElement, "Token");
        var userHash = xbl.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;

        // 2. XSTS token for Minecraft services.
        using var xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        }, ct, "Xbox authorization failed", DescribeXstsError);
        var xstsToken = RequireString(xsts.RootElement, "Token");

        // 3. Minecraft access token.
        using var mc = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new Dictionary<string, string> { ["identityToken"] = $"XBL3.0 x={userHash};{xstsToken}" },
            ct, "Minecraft login failed", DescribeMinecraftLoginError);
        var mcToken = RequireString(mc.RootElement, "access_token");
        var expiresIn = mc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 86400;

        // 4. Profile (404 if the account doesn't own Java Edition).
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new MicrosoftAuthException("This Microsoft account doesn't own Minecraft: Java Edition (or has no profile name yet).");
        if (!resp.IsSuccessStatusCode)
            throw new MicrosoftAuthException($"Couldn't load Minecraft profile (HTTP {(int)resp.StatusCode}).");
        using var profile = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        return new MicrosoftAccount
        {
            Id = RequireString(profile.RootElement, "id"),
            Name = RequireString(profile.RootElement, "name"),
            Xuid = ReadJwtClaim(mcToken, "xuid") ?? "",
            ProtectedRefreshToken = Protect(msRefreshToken),
            ProtectedAccessToken = Protect(mcToken),
            AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn),
        };
    }

    private static string? DescribeXstsError(HttpStatusCode status, JsonElement? body)
    {
        if (body is not { } b || !b.TryGetProperty("XErr", out var xerr) || !xerr.TryGetInt64(out var code))
            return null;
        return code switch
        {
            2148916227 => "This Xbox account is banned.",
            2148916233 => "This Microsoft account has no Xbox profile. Sign in once at minecraft.net to create one.",
            2148916235 => "Xbox Live isn't available in this account's country/region.",
            2148916236 or 2148916237 => "This account needs adult verification on Xbox (South Korea).",
            2148916238 => "This is a child account. An adult must add it to a Microsoft family first.",
            _ => null,
        };
    }

    private static string? DescribeMinecraftLoginError(HttpStatusCode status, JsonElement? body) =>
        status == HttpStatusCode.Forbidden
            ? "Minecraft rejected this app's client ID. The Azure app must be approved by Mojang for the Minecraft API (see README)."
            : null;

    private static async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await Http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new MicrosoftAuthException($"Unexpected response from Microsoft (HTTP {(int)resp.StatusCode})."); }
    }

    private static async Task<JsonDocument> PostJsonAsync(string url, object payload, CancellationToken ct, string failPrefix,
        Func<HttpStatusCode, JsonElement?, string?>? describeError = null)
    {
        // Xbox endpoints expect PascalCase property names, so use default (non-web) serializer options.
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        JsonDocument? doc = null;
        try { if (!string.IsNullOrWhiteSpace(body)) doc = JsonDocument.Parse(body); }
        catch (JsonException) { }

        if (resp.IsSuccessStatusCode && doc != null)
            return doc;

        var message = describeError?.Invoke(resp.StatusCode, doc?.RootElement) ?? $"{failPrefix} (HTTP {(int)resp.StatusCode}).";
        doc?.Dispose();
        throw new MicrosoftAuthException(message);
    }

    private static void ThrowIfOAuthError(JsonElement root)
    {
        if (GetString(root, "error") is not { } error)
            return;
        var description = GetString(root, "error_description")?.Split('\n')[0].Trim();
        throw new MicrosoftAuthException(error switch
        {
            "invalid_client" or "unauthorized_client" =>
                "Microsoft rejected the client ID. Check MsaClientId in settings and that public client flows are enabled on the Azure app.",
            _ => $"Microsoft sign-in failed: {description ?? error}",
        });
    }

    private static string? ReadJwtClaim(string jwt, string claim)
    {
        try
        {
            var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty(claim, out var v) ? v.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Protect(string value) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string value) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));

    private static string RequireString(JsonElement el, string property) =>
        GetString(el, property) ?? throw new MicrosoftAuthException($"Unexpected response: missing '{property}'.");

    private static string? GetString(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sunshine/1.0");
        return client;
    }
}
