using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Dustline.Market;

internal sealed record PasswordDigest(string Salt, string Hash)
{
    private const int Iterations = 600_000;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public static PasswordDigest Create(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        return new(Convert.ToBase64String(salt), Convert.ToBase64String(Derive(password, salt)));
    }

    public bool Verify(string password) => CryptographicOperations.FixedTimeEquals(
        Convert.FromBase64String(Hash), Derive(password, Convert.FromBase64String(Salt)));

    public bool IsValid() => Valid(Salt, SaltLength) && Valid(Hash, HashLength);

    private static bool Valid(string value, int length) => value != null &&
        Convert.TryFromBase64String(value, new byte[length], out int written) && written == length;

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashLength);
}

internal static class Credentials
{
    public static string Account(string? account)
    {
        if (account is not { Length: >= 3 and <= 32 } ||
            !account.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw MarketError.Invalid("InvalidAccount");
        return account.ToLowerInvariant();
    }

    public static string Password(string? password)
    {
        if (password is not { Length: >= 8 and <= 128 }) throw MarketError.Invalid("InvalidPassword");
        return password;
    }
}

internal sealed class Sessions
{
    private sealed record Session(string Account, DateTime Expires);

    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private const int SessionsPerAccount = 8;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public string Issue(string account)
    {
        var now = DateTime.UtcNow;
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (gate)
        {
            foreach (string expired in sessions.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
                sessions.Remove(expired);
            foreach (string oldest in sessions.Where(pair => pair.Value.Account == account)
                .OrderByDescending(pair => pair.Value.Expires).Skip(SessionsPerAccount - 1).Select(pair => pair.Key).ToArray())
                sessions.Remove(oldest);
            sessions.Add(Key(token), new(account, now + Lifetime));
        }
        return token;
    }

    public string Authenticate(string? header)
    {
        if (AuthenticationHeaderValue.TryParse(header, out var auth) &&
            auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) &&
            auth.Parameter is { Length: 64 } token && token.All(char.IsAsciiHexDigit))
        {
            string key = Key(token);
            lock (gate)
            {
                if (sessions.TryGetValue(key, out var session))
                {
                    if (session.Expires > DateTime.UtcNow) return session.Account;
                    sessions.Remove(key);
                }
            }
        }
        throw new MarketError(StatusCodes.Status401Unauthorized, "Unauthorized");
    }

    private static string Key(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
