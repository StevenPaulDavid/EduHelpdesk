namespace EduHelpdesk.Services;

// What a new password has to be, wherever one is chosen: a technician changing their own, an administrator setting a
// temporary one, a technician giving a requester a portal password, or a requester changing theirs in the portal.
// Length over complexity, as the NCSC recommends: no "one capital, one symbol" rules, which people meet with
// "Password1!", but the passwords every guessing list tries first are turned away, whatever is tacked onto the end.
public static class PasswordRules
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 128;

    // Shown next to password boxes.
    public const string Hint = "At least 8 characters. Three random words together is easy to remember and hard to guess.";

    // The stem of the passwords tried first by anyone guessing - compared after stripping digits and symbols from both
    // ends, so "Password123!", "!!welcome2025" and "Teacher1" are all caught.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passw0rd", "pass", "pa55word", "qwerty", "qwertyuiop", "asdfgh", "azerty", "abc", "abcd", "abcdef",
        "letmein", "welcome", "hello", "iloveyou", "monkey", "dragon", "football", "sunshine", "princess", "shadow",
        "master", "superman", "batman", "trustno1", "admin", "administrator", "changeme", "secret", "default", "login",
        "school", "teacher", "student", "pupil", "staff", "office", "helpdesk", "eduhelpdesk", "support", "computer",
        "summer", "winter", "autumn", "spring", "january", "february", "september", "october", "november", "december"
    };

    // A reason the password can't be used, in words to show the person, or null if it's fine. `personal` is the
    // account's name and email, which make obvious guesses.
    public static string? Problem(string? password, params string?[] personal)
    {
        var value = password ?? "";
        if (value.Length < MinimumLength) return $"Choose a password of at least {MinimumLength} characters.";
        if (value.Length > MaximumLength) return $"Keep the password under {MaximumLength} characters.";
        if (value.Distinct().Count() < 4) return "That password repeats too few characters to be safe - choose something less predictable.";
        var stem = value.Trim(Symbols);
        if (stem.Length == 0) return "A password made only of numbers or symbols is too easy to guess - add some words.";
        if (Common.Contains(stem) || Common.Contains(stem.TrimEnd('s'))) return "That password is one of the first anyone would try - choose something less common.";
        foreach (var part in personal.SelectMany(Parts))
            if (part.Length >= 4 && value.Contains(part, StringComparison.OrdinalIgnoreCase))
                return "Don't use your name or email address in your password.";
        return null;
    }

    private static readonly char[] Symbols = "0123456789!\"£$%^&*()-_=+[]{};:'@#~,.<>/?\\|`¬ ".ToCharArray();

    // "Jo Bloggs" and "jo.bloggs@school.org" give "bloggs" - each word of the name, and each piece of the email's
    // address part. The domain is left out, since a school's name in a password is a separate (and lesser) problem.
    private static IEnumerable<string> Parts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var local = value.Contains('@') ? value[..value.IndexOf('@')] : value;
        return local.Split([' ', '.', '_', '-', '\''], StringSplitOptions.RemoveEmptyEntries);
    }
}
