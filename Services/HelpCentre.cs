using System.Security.Claims;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The help centre's built-in articles: Markdown files in wwwroot/help/articles, one per article, shipped with each
// release so the help always matches the version installed. Each starts with a few lines between --- marks:
//
//   ---
//   title: Working a ticket
//   summary: One line shown in lists and search results.
//   area: Tickets                      (one of Areas below)
//   kind: task                         (start = Getting started, task = "How do I...", page = a guide to one page)
//   pages: /Job, /PrintJobSheet        (the pages whose Help link opens this article)
//   needs: Tickets:Edit, Rounds.Record (who can use what it describes: a module, Module:Action, or a role tick)
//   keywords: close resolve reply      (extra words for search)
//   order: 2                           (its place within its area; lowest first)
//   ---
//
// Everyone can read every article; one about a part their role can't open is marked "needs permission" rather than
// hidden. The school's own articles and notes are kept in the database instead (HelpdeskStore.Help).
public sealed class HelpCentre
{
    public static readonly string[] Areas =
    [
        "Getting started", "Tickets", "Site and cleaning", "Assets and inventory", "Compliance", "Projects", "Onboarding",
        "People and roles", "Reports", "Settings", "Staff portal", "Your account", "School articles"
    ];
    public static readonly string[] Kinds = ["start", "task", "page"];

    public sealed record Article(string Slug, string Title, string Summary, string Area, string Kind, IReadOnlyList<string> Pages,
        IReadOnlyList<string> Needs, IReadOnlyList<string> Keywords, int Order, string Body)
    {
        public bool IsSchool { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public string KindLabel => IsSchool ? "School article" : Kind switch { "start" => "Getting started", "task" => "How to", _ => "Page guide" };
    }

    public sealed record Problem(string File, string Message);
    public sealed record Hit(Article Article, int Score);

    private readonly string _folder;
    private readonly object _sync = new();
    private IReadOnlyList<Article> _articles = [];
    private IReadOnlyList<Problem> _problems = [];
    private string _whatsNew = "";
    private string _portalHelp = "";
    private DateTime _loadedStamp = DateTime.MinValue;
    private DateTime _checkedAt = DateTime.MinValue;

    public HelpCentre(string folder)
    {
        _folder = folder;
        Load();
    }

    public string Folder => _folder;
    public IReadOnlyList<Article> Articles { get { Refresh(); lock (_sync) return _articles; } }
    // Articles that couldn't be read, for the tests and the help centre's own admin view.
    public IReadOnlyList<Problem> Problems { get { Refresh(); lock (_sync) return _problems; } }
    public string WhatsNew { get { Refresh(); lock (_sync) return _whatsNew; } }
    // The staff portal's own short help (wwwroot/help/portal.md, Pages/Portal/Help) - plain language for requesters,
    // kept out of the technicians' articles.
    public string PortalHelp { get { Refresh(); lock (_sync) return _portalHelp; } }
    // The helpdesk-side article about the portal; the school's note on it also heads the portal's help page.
    public const string PortalArticle = "staff-portal";

    public Article? Find(string? slug) => Articles.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));

    // The article a page's Help link opens. A page guide wins over a task that also names the page.
    public Article? ForPage(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var clean = path.TrimEnd('/');
        if (clean.EndsWith("/Index", StringComparison.OrdinalIgnoreCase)) clean = clean[..^6];
        if (clean.Length == 0) clean = "/";
        bool Matches(string page)
        {
            var p = page.EndsWith("/Index", StringComparison.OrdinalIgnoreCase) ? page[..^6] : page;
            return string.Equals(p.Length == 0 ? "/" : p, clean, StringComparison.OrdinalIgnoreCase);
        }
        return Articles.Where(a => a.Pages.Any(Matches)).OrderBy(a => a.Kind == "page" ? 0 : a.Kind == "start" ? 2 : 1).ThenBy(a => a.Order).FirstOrDefault();
    }

    // Plain word search over title (counted most), keywords, summary and text, built-in and school articles alike.
    public static IReadOnlyList<Hit> Search(IEnumerable<Article> articles, string? query)
    {
        var words = (query ?? "").ToLowerInvariant().Split([' ', ',', '.', '?', '!', '"', '\''], StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 1).Distinct().ToList();
        if (words.Count == 0) return [];
        var hits = new List<Hit>();
        foreach (var article in articles)
        {
            var title = article.Title.ToLowerInvariant();
            var keywords = string.Join(' ', article.Keywords).ToLowerInvariant();
            var summary = article.Summary.ToLowerInvariant();
            var body = HelpMarkdown.PlainText(article.Body).ToLowerInvariant();
            var score = 0;
            var all = true;
            foreach (var word in words)
            {
                var s = (title.Contains(word) ? 8 : 0) + (keywords.Contains(word) ? 5 : 0) + (summary.Contains(word) ? 3 : 0) + Math.Min(4, Count(body, word));
                if (s == 0) all = false;
                score += s;
            }
            if (score > 0) hits.Add(new Hit(article, all ? score * 2 : score));
        }
        return hits.OrderByDescending(x => x.Score).ThenBy(x => x.Article.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int Count(string text, string word)
    {
        var count = 0;
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0 && count < 10; at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    // ---- Who an article is for ----

    // True when this person can use at least one of the things the article describes (or it names none).
    // About a part of the helpdesk the school has switched off (Services/Features.cs): every page it covers is one of a
    // switched-off feature's. Left out of the help centre, so a school without assets reads nothing about them.
    public static bool SwitchedOff(Article article, HelpdeskStore store)
    {
        var off = store.FeaturesOff;
        return off.Count > 0 && article.Pages.Count > 0 && article.Pages.All(page => Features.ForPath(page, off) is not null);
    }

    public static bool CanUse(Article article, HelpdeskStore store, ClaimsPrincipal user) =>
        article.Needs.Count == 0 || article.Needs.Any(need => Meets(need, store, user));

    private static bool Meets(string need, HelpdeskStore store, ClaimsPrincipal user)
    {
        var parts = need.Split(':', 2);
        if (parts.Length == 2 && Enum.TryParse<ModulePermission>(parts[1], true, out var action)) return store.UserCan(user, parts[0], action);
        if (Modules.Flags.Find(need) is not null) return store.UserHasFlag(user, need);
        return store.UserCan(user, need, ModulePermission.Access);
    }

    // What a role would need, in the role editor's own words: "Tickets: Edit", "Record cleaning rounds".
    public static string NeedsLabel(Article article) => string.Join(" or ", article.Needs.Select(NeedLabel));

    public static string NeedLabel(string need)
    {
        var parts = need.Split(':', 2);
        if (Modules.Flags.Find(need) is { } flag) return flag.Label;
        var module = Modules.Find(parts[0])?.Label ?? parts[0];
        return parts.Length == 2 ? $"{module}: {parts[1]}" : module;
    }

    // A need is valid when it names a real module (with a real action) or a real role tick - checked by the tests.
    public static bool IsValidNeed(string need)
    {
        var parts = need.Split(':', 2);
        if (parts.Length == 2) return Modules.Find(parts[0]) is { } m && Enum.TryParse<ModulePermission>(parts[1], true, out var a) && m.Supports.HasFlag(a);
        return Modules.Flags.Find(need) is not null || Modules.Find(need) is not null;
    }

    // ---- Loading ----

    // Files are re-read when one changes (checked at most every few seconds), so an article edited on a running copy
    // shows on the next page load.
    private void Refresh()
    {
        if (DateTime.UtcNow - _checkedAt < TimeSpan.FromSeconds(3)) return;
        _checkedAt = DateTime.UtcNow;
        if (Stamp() != _loadedStamp) Load();
    }

    private DateTime Stamp()
    {
        if (!Directory.Exists(_folder)) return DateTime.MinValue;
        var files = Directory.EnumerateFiles(_folder, "*.md", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue);
        return files.Max().AddTicks(Directory.EnumerateFiles(_folder, "*.md", SearchOption.AllDirectories).Count());
    }

    private void Load()
    {
        var articles = new List<Article>();
        var problems = new List<Problem>();
        var whatsNew = "";
        var stamp = Stamp();
        var folder = Path.Combine(_folder, "articles");
        if (Directory.Exists(folder))
            foreach (var file in Directory.EnumerateFiles(folder, "*.md").Order(StringComparer.OrdinalIgnoreCase))
            {
                var (article, problem) = Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
                if (article is not null) articles.Add(article);
                if (problem is not null) problems.Add(new Problem(Path.GetFileName(file), problem));
            }
        var whatsNewFile = Path.Combine(_folder, "whats-new.md");
        if (File.Exists(whatsNewFile)) whatsNew = File.ReadAllText(whatsNewFile);
        var portalFile = Path.Combine(_folder, "portal.md");
        var portalHelp = File.Exists(portalFile) ? File.ReadAllText(portalFile) : "";
        lock (_sync)
        {
            _articles = articles.OrderBy(x => Array.IndexOf(Areas, x.Area)).ThenBy(x => x.Order).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList();
            _problems = problems;
            _whatsNew = whatsNew;
            _portalHelp = portalHelp;
            _loadedStamp = stamp;
        }
    }

    public static (Article? Article, string? Problem) Parse(string slug, string text)
    {
        text = text.Replace("\r\n", "\n").TrimStart('﻿');
        if (!text.StartsWith("---\n")) return (null, "It doesn't start with the --- block of title, area and so on.");
        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (null, "The --- block at the top isn't closed.");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var body = text[(end + 4)..].TrimStart('-').Trim('\n');
        static IReadOnlyList<string> List(string? value, params char[] separators) =>
            (value ?? "").Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var title = fields.GetValueOrDefault("title") ?? "";
        var area = fields.GetValueOrDefault("area") ?? "";
        var kind = (fields.GetValueOrDefault("kind") ?? "page").ToLowerInvariant();
        if (title.Length == 0) return (null, "It has no title.");
        if (!Areas.Contains(area) || area == "School articles") return (null, $"Its area, \"{area}\", isn't one of the help centre's areas.");
        if (!Kinds.Contains(kind)) return (null, $"Its kind, \"{kind}\", should be start, task or page.");
        var article = new Article(slug.ToLowerInvariant(), title, fields.GetValueOrDefault("summary") ?? "", area, kind,
            List(fields.GetValueOrDefault("pages"), ','), List(fields.GetValueOrDefault("needs"), ','), List(fields.GetValueOrDefault("keywords"), ' ', ','),
            int.TryParse(fields.GetValueOrDefault("order"), out var order) ? order : 100, body);
        var bad = article.Needs.FirstOrDefault(x => !IsValidNeed(x));
        return (article, bad is null ? null : $"\"{bad}\" in needs isn't a module or role tick.");
    }
}
