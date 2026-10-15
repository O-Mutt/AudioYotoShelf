using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace AudioYotoShelf.Infrastructure.Services;

/// <summary>Proof Key for Code Exchange (RFC 7636), S256 method.</summary>
internal static class Pkce
{
    private const int VerifierBytes = 32;

    // 32 random bytes encode to 43 base64url characters: the RFC's minimum verifier length.
    public static string CreateVerifier() => RandomUrlSafeToken(VerifierBytes);

    public static string S256Challenge(string codeVerifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    public static string RandomUrlSafeToken(int byteCount) =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(byteCount));
}
