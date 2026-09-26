using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EduHelpdesk.Tests;

// Conventions the pages rely on, checked in the source. The security policy refuses inline script, so a handler
// written into a page would silently do nothing in the browser; this catches it before anyone has to notice.
public class PageConventionTests
{
    // Found from this source file's own path, so it works wherever the tests were built to.
    private static string ProjectRoot([CallerFilePath] string here = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(here)!); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "EduHelpdesk.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Couldn't find the project folder above " + here);
    }

    private static IEnumerable<(string File, int Line, string Text)> Matches(string pattern)
    {
        var root = ProjectRoot();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Pages"), "*.cshtml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase))
                    yield return (Path.GetRelativePath(root, file), i + 1, lines[i].Trim());
        }
    }

    [Fact]
    public void No_page_has_an_inline_event_handler()
    {
        var found = Matches(@"<[a-z][^>]*\son[a-z]+\s*=\s*[""']").ToList();
        Assert.True(found.Count == 0, "Use the data- attributes in wwwroot/js/actions.js instead:\n" + string.Join("\n", found.Select(x => $"{x.File}:{x.Line}: {x.Text}")));
    }

    [Fact]
    public void No_page_has_an_inline_script_block()
    {
        var found = Matches(@"<script(?![^>]*\bsrc=)(?![^>]*application/json)[^>]*>").ToList();
        Assert.True(found.Count == 0, "Move the script into wwwroot/js/pages/:\n" + string.Join("\n", found.Select(x => $"{x.File}:{x.Line}: {x.Text}")));
    }

    [Fact]
    public void No_page_uses_a_javascript_url() =>
        Assert.Empty(Matches(@"href\s*=\s*[""']\s*javascript:"));
}
