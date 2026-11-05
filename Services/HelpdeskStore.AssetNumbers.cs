using System.Globalization;

namespace EduHelpdesk.Services;

// Built-in asset numbers (Settings → Asset, part & loan rules): what the asset import offers a row that has no asset tag
// of its own - the prefix and a padded running number, AST-00001, AST-00002. Numbering carries on from the highest one
// in use (or ever handed out, LastAssetNumber), so a number is never given to two assets.
public sealed partial class HelpdeskStore
{
    public const string DefaultAssetTagPrefix = "AST-";
    public const int DefaultAssetTagDigits = 5;
    public const int MinAssetTagDigits = 1;
    public const int MaxAssetTagDigits = 9;
    private const int MaxAssetTagPrefixLength = 12;

    public (string Prefix, int Digits) AssetNumbering { get { lock (_sync) return (_data.AssetTagPrefix, _data.AssetTagDigits); } }

    // The number the next asset with no tag would get, for the settings page and the import's offer.
    public string NextAssetTag { get { lock (_sync) return NewAssetNumbering().Peek(); } }

    // Letters, digits, dashes, dots, underscores and slashes, so it reads well on a label and in a barcode. May be blank.
    private static string? CheckAssetTagPrefix(string? prefix)
    {
        var text = prefix ?? string.Empty;
        if (text.Length > MaxAssetTagPrefixLength) return $"Keep the prefix to {MaxAssetTagPrefixLength} characters or fewer.";
        if (text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '/'))) return "Use only letters, numbers, dashes, dots, underscores or slashes in the prefix.";
        return null;
    }

    public string SetAssetNumbering(string? prefix, int digits)
    {
        lock (_sync)
        {
            var clean = (prefix ?? string.Empty).Trim();
            if (CheckAssetTagPrefix(clean) is { } problem) return problem;
            if (digits is < MinAssetTagDigits or > MaxAssetTagDigits) return $"Choose between {MinAssetTagDigits} and {MaxAssetTagDigits} digits.";
            _data.AssetTagPrefix = clean;
            _data.AssetTagDigits = digits;
            Save();
            return $"Asset numbers saved. The next one will be {NewAssetNumbering().Peek()}.";
        }
    }

    // A counter for one run of the import. extraTags: tags already in the file, so a generated number never repeats one
    // typed further down it. Called with the lock held.
    private AssetNumberer NewAssetNumbering(IEnumerable<string>? extraTags = null)
    {
        var highest = _data.LastAssetNumber;
        foreach (var tag in _data.Assets.Select(x => x.AssetTag).Concat(extraTags ?? []))
            if (AssetNumberer.NumberIn(tag, _data.AssetTagPrefix) is { } n && n > highest) highest = n;
        return new AssetNumberer(_data.AssetTagPrefix, _data.AssetTagDigits, highest);
    }

    private sealed class AssetNumberer(string prefix, int digits, int highest)
    {
        public int Highest { get; private set; } = highest;
        public string Peek() => Format(Highest + 1);
        public string Take() => Format(++Highest);
        private string Format(int number) => prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');

        // The running number in a tag made by this prefix (AST-00042 → 42), whatever its padding; null for any other tag.
        public static int? NumberIn(string? tag, string prefix)
        {
            var text = (tag ?? string.Empty).Trim();
            if (text.Length <= prefix.Length || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var rest = text[prefix.Length..];
            return rest.Length <= 9 && rest.All(char.IsAsciiDigit) && int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }
    }
}
