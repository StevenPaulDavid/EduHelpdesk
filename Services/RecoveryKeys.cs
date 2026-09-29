using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EduHelpdesk.Services;

// A technician's recovery key: 24 characters in six groups, "K7QP-M4XR-2DTA-...", made from 15 random bytes (120 bits).
// It resets a forgotten password once, without waiting for an administrator (Pages/ForgotPassword). It is kept as a
// printed or saved recovery file, or written down, and only its SHA-256 is stored. With that much randomness a slow
// hash adds nothing, and guessing is also held back by SignInThrottle.
//
// It only replaces the password: two-step sign-in, where it's on, is still asked for when they sign in.
public static partial class RecoveryKeys
{
    public const int Length = 24;
    // Also the throttle's name for the reset form, counted apart from the sign-in forms.
    public const string ThrottleForm = "recovery";

    public static string NewKey() => Format(Base32.Encode(RandomNumberGenerator.GetBytes(15)));

    // In groups of four, however it was typed.
    public static string Format(string key) => string.Join("-", Normalise(key).Chunk(4).Select(x => new string(x)));

    // Capitals, and no spaces or dashes. Base32 has no 0, 1 or 8, so one typed for O, I or B is read as that letter.
    public static string Normalise(string? key) =>
        new string((key ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).Select(c => c switch { '0' => 'O', '1' => 'I', '8' => 'B', _ => c }).ToArray());

    public static bool LooksValid(string? key) => Normalise(key) is { Length: Length } normalised && normalised.All(c => c is >= 'A' and <= 'Z' or >= '2' and <= '7');

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(Normalise(key))));

    public static bool Matches(string? key, string? storedHash) =>
        storedHash is not null && LooksValid(key)
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(key!)), Encoding.ASCII.GetBytes(storedHash));

    // The key inside an uploaded recovery file (RecoveryFile), or any text holding one.
    public static string? FindIn(string text) => KeyPattern().Match(text.ToUpperInvariant()) is { Success: true } match ? match.Value : null;

    [GeneratedRegex("[A-Z2-7]{4}(?:[- ]?[A-Z2-7]{4}){5}")]
    private static partial Regex KeyPattern();

    // The recovery file: plain text, readable on its own, which the reset page also accepts as an upload.
    public static string RecoveryFile(string brandName, string email, string key, string? address, DateTime createdAt) => $"""
        {brandName} - helpdesk recovery key
        ====================================

        Account:       {email}
        Recovery key:  {key}
        Made:          {createdAt.ToLocalTime():d MMMM yyyy, HH:mm}

        If you forget your helpdesk password, choose "Forgotten your password?" on the
        sign-in page{(address is null ? "" : $" ({address}ForgotPassword)")}, then type this key or upload this file.

        - The key works once. After using it, make a new one from "Recovery key" in your
          account menu.
        - Anyone with this key and your email can change your password, so keep it
          somewhere only you can get to: a password manager, or printed and locked away.
          Not on a shared drive or in an email.
        - If you think someone else has seen it, make a new one. The old one stops working.
        """;
}
