using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace EduHelpdesk.Tests;

// The help centre (stage 1): the safe Markdown, the built-in articles and their links, the ? tips, and the school's
// own articles and notes.
public class HelpCentreTests
{
    private static string ProjectRoot([CallerFilePath] string here = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(here)!); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EduHelpdesk.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Couldn't find the project folder above " + here);
    }

    private static readonly HelpMarkdown.Links Links = new((slug, anchor) => $"/Help/Article/{slug}{(anchor is null ? "" : "#" + anchor)}", path => path, file => "/help/images/" + file);
    private static string Html(string markdown) => HelpMarkdown.Render(markdown, Links).Html;
    private static HelpCentre Help() => new(Path.Combine(ProjectRoot(), "wwwroot", "help"));

    [Fact]
    public void Markdown_never_lets_markup_or_script_through()
    {
        var html = Html("""
            ## Hello <script>alert(1)</script>
            A <b>bold</b> claim & a [bad](javascript:alert(1)) link, an [onclick](x" onclick="y) one, and `<code>`.
            ![x](../secret.png)
            ![x](javascript:alert(1).png)
            > **Tip:** <img src=x onerror=alert(1)>
            | a | <i>b</i> |
            | --- | --- |
            | <svg> | ok |
            """);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<svg", html);
        Assert.DoesNotContain("<i>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("onclick=\"", html);
        Assert.DoesNotContain("secret.png", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<code>&lt;code&gt;</code>", html);
    }

    [Fact]
    public void Markdown_turns_the_supported_shapes_into_html()
    {
        var result = HelpMarkdown.Render("""
            ## Closing a ticket
            Use **Close** and *then* see [the list](/Jobs?view=mine), [rounds](getting-started-cleaning#ticking-off-a-round) or [web](https://example.org).

            - one
              still one
            - two

            1. first
            2. second

            > **Important:** careful

            ![The list](tickets-list.png)

            ## Closing a ticket
            """, Links);
        var html = result.Html;
        Assert.Contains("<h2 id=\"closing-a-ticket\">", html);
        Assert.Contains("<h2 id=\"closing-a-ticket-2\">", html);
        Assert.Equal(["closing-a-ticket", "closing-a-ticket-2"], result.Headings.Select(x => x.Id));
        Assert.Contains("<strong>Close</strong>", html);
        Assert.Contains("<em>then</em>", html);
        Assert.Contains("<a href=\"/Jobs?view=mine\">the list</a>", html);
        Assert.Contains("<a href=\"/Help/Article/getting-started-cleaning#ticking-off-a-round\">rounds</a>", html);
        Assert.Contains("href=\"https://example.org\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains("<li>one still one</li>", html);
        Assert.Contains("<ol>\n<li>first</li>", html);
        Assert.Contains("help-callout-important", html);
        Assert.Contains("<img src=\"/help/images/tickets-list.png\" alt=\"The list\"", html);
    }

    [Fact]
    public void Every_built_in_article_is_well_formed_and_its_links_lead_somewhere()
    {
        var help = Help();
        var root = ProjectRoot();
        Assert.Empty(help.Problems.Select(x => $"{x.File}: {x.Message}"));
        Assert.NotEmpty(help.Articles);
        var slugs = help.Articles.Select(x => x.Slug).ToHashSet();
        var images = Path.Combine(root, "wwwroot", "help", "images");
        foreach (var article in help.Articles.Append(new HelpCentre.Article("whats-new", "", "", "", "", [], [], [], 0, help.WhatsNew)))
        {
            foreach (var page in article.Pages)
            {
                var file = page == "/" ? "Index" : page.TrimStart('/');
                Assert.True(File.Exists(Path.Combine(root, "Pages", file + ".cshtml")) || File.Exists(Path.Combine(root, "Pages", file, "Index.cshtml")), $"{article.Slug}: no page {page}");
            }
            foreach (Match link in Regex.Matches(article.Body, @"\]\(([^)\s]+)\)"))
            {
                var target = link.Groups[1].Value;
                if (target.StartsWith("http") || target.StartsWith('/') || Regex.IsMatch(target, @"\.(png|jpg|jpeg|gif|webp)$")) continue;
                var slug = target.Replace("help:", "").Split('#')[0];
                Assert.True(slugs.Contains(slug), $"{article.Slug} links to a missing article: {target}");
            }
            // The help's Markdown has no nested lists: an indented "- " or "1." would break the list it sits in.
            Assert.False(Regex.IsMatch(article.Body, @"(?m)^[ \t]+([-*]|\d+[.)])\s"), $"{article.Slug} has a nested list, which the help doesn't support");
            foreach (Match image in Regex.Matches(article.Body, @"!\[[^\]]*\]\(([^)\s]+)\)"))
                Assert.True(File.Exists(Path.Combine(images, image.Groups[1].Value)), $"{article.Slug}: missing screenshot {image.Groups[1].Value}");
        }
        // Each area's Getting started is there for the main kinds of account.
        Assert.Equal(5, help.Articles.Count(x => x.Kind == "start"));
    }

    [Fact]
    public void Every_tip_points_at_a_real_section_and_every_tip_used_exists()
    {
        var help = Help();
        foreach (var (key, tip) in HelpTips.All)
        {
            var article = help.Find(tip.Article);
            Assert.True(article is not null, $"Tip {key} points at a missing article {tip.Article}");
            if (tip.Section is { } section)
                Assert.True(HelpMarkdown.Render(article!.Body, Links).Headings.Any(h => h.Id == section), $"Tip {key}: {tip.Article} has no section {section}");
        }
        var used = Directory.EnumerateFiles(Path.Combine(ProjectRoot(), "Pages"), "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "<help-tip key=\"([^\"]+)\"").Select(m => (File: Path.GetFileName(file), Key: m.Groups[1].Value)))
            .ToList();
        Assert.NotEmpty(used);
        Assert.Empty(used.Where(x => HelpTips.Find(x.Key) is null).Select(x => $"{x.File}: {x.Key}"));
    }

    [Fact]
    public void Pages_find_their_guide_and_search_finds_the_right_article()
    {
        var help = Help();
        Assert.Equal("getting-started-cleaning", help.ForPage("/Rounds")?.Slug);
        Assert.Equal("getting-started-cleaning", help.ForPage("/Settings/Rounds/")?.Slug);
        // A page guide wins over a Getting started article that names the same page.
        Assert.Equal("onboarding-list", help.ForPage("/Onboarding")?.Slug);
        Assert.Equal("getting-started-technician", help.ForPage("/")?.Slug);
        Assert.Null(help.ForPage("/Nowhere"));
        Assert.Equal("getting-started-cleaning", HelpCentre.Search(help.Articles, "cleaning round spot check").First().Article.Slug);
        Assert.Empty(HelpCentre.Search(help.Articles, "zzqx"));

        var (bad, problem) = HelpCentre.Parse("x", "---\ntitle: X\narea: Tickets\nneeds: Nonsense:Edit\n---\nBody");
        Assert.NotNull(bad);
        Assert.Contains("Nonsense", problem);
        Assert.Null(HelpCentre.Parse("x", "No front matter").Article);
        Assert.Null(HelpCentre.Parse("x", "---\ntitle: X\narea: Nowhere\n---\nBody").Article);
    }

    [Fact]
    public void An_article_is_marked_for_whoever_can_use_it()
    {
        using var test = new TestStore();
        var help = Help();
        var cleaning = help.Find("getting-started-cleaning")!;
        var admin = Principal(StaffRoles.Administrator);
        var technician = Principal(StaffRoles.DefaultRole);
        Assert.True(HelpCentre.CanUse(cleaning, test.Store, admin));
        Assert.False(HelpCentre.CanUse(cleaning, test.Store, technician));
        Assert.True(HelpCentre.CanUse(help.Find("using-help")!, test.Store, technician));
        Assert.Contains("Record cleaning rounds", HelpCentre.NeedsLabel(cleaning));
        Assert.Contains("Settings: Edit", HelpCentre.NeedsLabel(cleaning));
    }

    [Fact]
    public void The_schools_own_articles_and_notes_are_saved_and_audited()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.False(store.SaveSchoolHelpArticle(null, " ", "", "Body").Ok);
        Assert.False(store.SaveSchoolHelpArticle(null, "Title", "", " ").Ok);
        var (ok, _, id) = store.SaveSchoolHelpArticle(null, "Who to call out of hours", "Numbers for emergencies", "## Leaks\nRing the site office on 214.");
        Assert.True(ok);
        Assert.Equal("Nothing changed.", store.SaveSchoolHelpArticle(id, "Who to call out of hours", "Numbers for emergencies", "## Leaks\nRing the site office on 214.").Message);
        Assert.Equal(HelpdeskStore.SchoolSlug(id!.Value), store.SchoolHelpAsArticles().Single().Slug);
        Assert.Equal(id, HelpdeskStore.SchoolId(HelpdeskStore.SchoolSlug(id.Value)));
        Assert.Contains(HelpCentre.Search(store.SchoolHelpAsArticles(), "site office"), x => x.Article.IsSchool);

        Assert.True(store.SaveHelpNote("Getting-Started-Technician", "Ring **214** for leaks.").Ok);
        Assert.Equal("Ring **214** for leaks.", store.HelpNoteFor("getting-started-technician")!.Text);
        Assert.False(store.SaveHelpNote("x", new string('a', HelpdeskStore.MaxHelpNoteLength + 1)).Ok);
        Assert.Null(store.CompareDatabaseWithMemory());

        var reopened = test.Reopen();
        Assert.Equal("Who to call out of hours", reopened.SchoolHelpArticles.Single().Title);
        Assert.NotNull(reopened.HelpNoteFor("getting-started-technician"));
        Assert.Contains(reopened.GetAuditEntries(), x => x.EntityType == "Help article" && x.Action == "Created");
        Assert.Contains(reopened.GetAuditEntries(), x => x.EntityType == "Help note" && x.Action == "Created");

        Assert.True(reopened.SaveHelpNote("getting-started-technician", "  ").Ok);
        Assert.Null(reopened.HelpNoteFor("getting-started-technician"));
        Assert.True(reopened.DeleteSchoolHelpArticle(id.Value).Ok);
        Assert.Empty(reopened.SchoolHelpArticles);
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    private static ClaimsPrincipal Principal(string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Name, "Test"), new Claim(ClaimTypes.Role, role)], "Test"));
}
