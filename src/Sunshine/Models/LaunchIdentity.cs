namespace Sunshine.Models;

/// <summary>
/// Who the game is launched as. Offline identities use a derived UUID and a dummy token;
/// Microsoft identities carry a real Minecraft access token.
/// </summary>
public sealed record LaunchIdentity(
    string Username,
    string Uuid,
    string AccessToken,
    string UserType,
    string Xuid = "",
    string ClientId = "");
