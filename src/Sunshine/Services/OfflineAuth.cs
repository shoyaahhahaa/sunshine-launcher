using System.Security.Cryptography;
using System.Text;

namespace Sunshine.Services;

/// <summary>
/// Reproduces vanilla Minecraft's offline-mode UUID derivation
/// (Java's UUID.nameUUIDFromBytes("OfflinePlayer:" + username)) so the same
/// username always maps to the same UUID, matching how offline-mode servers
/// identify players.
/// </summary>
public static class OfflineAuth
{
    public static Guid OfflineUuid(string username)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));

        // Set version (3) and variant bits per RFC 4122, as UUID.nameUUIDFromBytes does.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        // Java's UUID reads the 16 bytes big-endian; .NET's Guid(byte[]) constructor expects
        // the first three fields little-endian, so reorder them.
        Span<byte> reordered = stackalloc byte[16];
        reordered[0] = hash[3]; reordered[1] = hash[2]; reordered[2] = hash[1]; reordered[3] = hash[0];
        reordered[4] = hash[5]; reordered[5] = hash[4];
        reordered[6] = hash[7]; reordered[7] = hash[6];
        for (int i = 8; i < 16; i++)
            reordered[i] = hash[i];

        return new Guid(reordered);
    }
}
