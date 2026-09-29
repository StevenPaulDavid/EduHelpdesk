using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace EduHelpdesk.Services;

// Passwords for new accounts and resets. Leave the password box empty and the helpdesk makes one up: three everyday
// words and a number, "Otter-Lantern-Maple-38", because the person copies it off a printed sheet. It only has to last
// until they sign in, since RequirePasswordChange makes them choose their own. A password someone types instead is
// theirs to hand over, and isn't made to change.
//
// The password just set, made up or typed, is also kept in memory for an hour, so the quick start guide printed
// straight afterwards (Pages/People/QuickStart) can show it. The database only ever holds the hash. Once the hour is up,
// the service restarts or the password changes, the guide leaves a line to write it on instead.
public sealed class TemporaryPasswords
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, (string Password, string Hash, DateTimeOffset At)> _recent = new();

    // `personal` is the account's name and email: a made-up password that happened to contain one would be refused by
    // PasswordRules, so another is drawn.
    public static string Generate(params string?[] personal)
    {
        while (true)
        {
            var password = string.Join("-", Enumerable.Range(0, 3).Select(_ => Capitalise(Words[RandomNumberGenerator.GetInt32(Words.Length)])))
                + "-" + RandomNumberGenerator.GetInt32(10, 100);
            if (PasswordRules.Problem(password, personal) is null) return password;
        }
    }

    public void Remember(Guid accountId, string password, string hash)
    {
        _recent[accountId] = (password, hash, DateTimeOffset.UtcNow);
        foreach (var old in _recent.Where(x => DateTimeOffset.UtcNow - x.Value.At > KeptFor).Select(x => x.Key).ToList()) _recent.TryRemove(old, out _);
    }

    // The password set in the last hour, while it is still the account's password.
    public string? Recall(Guid accountId, string? currentHash) =>
        _recent.TryGetValue(accountId, out var kept) && DateTimeOffset.UtcNow - kept.At <= KeptFor && kept.Hash == currentHash ? kept.Password : null;

    private static string Capitalise(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    // Short, concrete, easy to spell and hard to misread. None is a password guessing lists try first (PasswordRules).
    private static readonly string[] Words =
    [
        "acorn", "anchor", "apple", "arrow", "badger", "bamboo", "banjo", "barley", "basket", "beacon", "beaver", "berry",
        "biscuit", "blossom", "bramble", "breeze", "brook", "bucket", "butter", "cabin", "cactus", "camel", "candle", "canoe",
        "canyon", "carrot", "castle", "cedar", "cherry", "chimney", "cinder", "clover", "cobalt", "comet", "copper", "coral",
        "cotton", "crayon", "cricket", "crystal", "cushion", "daisy", "dolphin", "domino", "falcon", "feather", "fiddle", "firefly",
        "flannel", "forest", "fossil", "fountain", "gadget", "galaxy", "garden", "garnet", "ginger", "glacier", "goblet", "granite",
        "grape", "gravel", "harbour", "harvest", "hazel", "hedgehog", "heron", "hickory", "honey", "island", "ivory", "jasmine",
        "jelly", "jigsaw", "jungle", "kettle", "kitten", "koala", "ladder", "lagoon", "lantern", "lemon", "lichen", "lilac",
        "lobster", "locket", "magnet", "mango", "maple", "marble", "meadow", "melon", "meteor", "mitten", "mosaic", "muffin",
        "nectar", "needle", "nutmeg", "oasis", "ocean", "olive", "orbit", "orchid", "otter", "paddle", "panda", "parcel",
        "parrot", "peach", "pebble", "pepper", "pickle", "pigeon", "pillow", "planet", "plum", "pocket", "pollen", "poppy",
        "puffin", "pumpkin", "puzzle", "quartz", "quill", "rabbit", "radish", "raven", "ribbon", "river", "robin", "rocket",
        "saddle", "salmon", "satchel", "shell", "silver", "sketch", "slate", "sparrow", "spinach", "spruce", "squirrel", "starling",
        "sultana", "teapot", "thimble", "thistle", "thunder", "timber", "toffee", "tomato", "topaz", "tractor", "trumpet", "tulip",
        "tunnel", "turnip", "velvet", "violet", "walnut", "walrus", "wagon", "willow", "window", "yacht", "zebra", "meerkat",
        "almond", "compass", "dragonfly", "emerald", "glove", "hammock", "iceberg", "juniper", "kayak", "lighthouse", "marigold",
    ];
}
