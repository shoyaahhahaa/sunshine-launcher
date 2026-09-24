namespace Sunshine.Models;

/// <summary>
/// A signed-in Microsoft account's Minecraft profile. Tokens are stored DPAPI-encrypted
/// (current Windows user only), never in plain text.
/// </summary>
public sealed class MicrosoftAccount
{
    /// <summary>Minecraft profile UUID, 32 hex chars without dashes.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Xuid { get; set; } = "";
    public string ProtectedRefreshToken { get; set; } = "";
    public string ProtectedAccessToken { get; set; } = "";
    public DateTime AccessTokenExpiresUtc { get; set; }
}
