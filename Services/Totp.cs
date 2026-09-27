using System.Security.Cryptography;
using System.Text;

namespace EduHelpdesk.Services;

// Authenticator-app codes (TOTP, RFC 6238): the six digits Microsoft Authenticator, Google Authenticator and the rest
// show, a new one every 30 seconds, worked out from a secret shared once through a QR code. SHA-1, 6 digits and 30
// seconds are what every app assumes when a QR code doesn't say otherwise.
public static class Totp
{
    public const int Digits = 6;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);
    // One step either side of now, for a phone clock that is a little out.
    private const int Window = 1;

    public static string NewSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    // What the QR code holds. The issuer names the helpdesk in the app's list; the account is the technician's email.
    public static string SetupUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={(int)Step.TotalSeconds}";

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    // The step the code belongs to, if it is right for now (give or take a step) and newer than the last one used - a
    // code seen once, over someone's shoulder or in a log, can't be used again.
    public static long? Verify(string secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != Digits || Base32.Decode(secret) is not { } key) return null;
        var current = StepAt(now);
        for (var step = current - Window; step <= current + Window; step++)
            if (step > lastUsedStep && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(key, step)), Encoding.ASCII.GetBytes(digits)))
                return step;
        return null;
    }

    public static string Code(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xff); step >>= 8; }
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    // One-time codes for when the phone is lost: ten of them, shown once, stored only as hashes. Random enough that a
    // plain SHA-256 is as good as a slow password hash.
    public static IReadOnlyList<string> NewRecoveryCodes(int count = 10) =>
        Enumerable.Range(0, count).Select(_ =>
        {
            var text = Base32.Encode(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
            return $"{text[..4]}-{text[4..]}";
        }).ToList();

    public static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormaliseRecoveryCode(code))));

    public static string NormaliseRecoveryCode(string? code) =>
        new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // Authenticator codes are six digits; recovery codes are eight characters.
    public static bool LooksLikeRecoveryCode(string? code) => NormaliseRecoveryCode(code).Length == 8;
}

// RFC 4648 base32, the alphabet authenticator apps expect secrets in.
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    public static byte[]? Decode(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
