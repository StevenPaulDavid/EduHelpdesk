using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The school's own help: articles it adds to the help centre, and a local note on any built-in article (HelpCentre).
// The built-in articles come with each release and can't be edited here, so a release never overwrites the school's
// words. Changing either needs Settings: Edit (checked by the help pages).
public sealed partial class HelpdeskStore
{
    public const int MaxHelpTitleLength = 120;
    public const int MaxHelpSummaryLength = 300;
    public const int MaxHelpBodyLength = 20_000;
    public const int MaxHelpNoteLength = 2_000;
    // A per-account preference: set once the welcome note has been dismissed.
    public const string HelpWelcomePreference = "help.welcome";

    public IReadOnlyList<SchoolHelpArticle> SchoolHelpArticles { get { lock (_sync) return _data.SchoolHelpArticles.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList(); } }
    public SchoolHelpArticle? GetSchoolHelpArticle(Guid id) { lock (_sync) return _data.SchoolHelpArticles.FirstOrDefault(x => x.Id == id); }
    public HelpNote? HelpNoteFor(string slug) { lock (_sync) return _data.HelpNotes.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase)); }

    // The school's articles in the help centre's own shape, for listing and search alongside the built-in ones.
    public static string SchoolSlug(Guid id) => $"school-{id:N}";
    public static Guid? SchoolId(string? slug) => slug is not null && slug.StartsWith("school-", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(slug[7..], "N", out var id) ? id : null;

    public IReadOnlyList<HelpCentre.Article> SchoolHelpAsArticles() => SchoolHelpArticles
        .Select(x => new HelpCentre.Article(SchoolSlug(x.Id), x.Title, x.Summary, "School articles", "task", [], [], [], 0, x.Body) { IsSchool = true, UpdatedAt = x.UpdatedAt })
        .ToList();

    public (bool Ok, string Message, Guid? Id) SaveSchoolHelpArticle(Guid? id, string? title, string? summary, string? body)
    {
        lock (_sync)
        {
            var index = id is { } given ? _data.SchoolHelpArticles.FindIndex(x => x.Id == given) : -1;
            if (id is not null && index < 0) return (false, "That article was not found.", null);
            var cleanTitle = (title ?? "").Trim();
            var cleanSummary = (summary ?? "").Trim();
            var cleanBody = (body ?? "").Replace("\r\n", "\n").Trim();
            if (cleanTitle.Length == 0) return (false, "Give the article a title.", null);
            if (cleanTitle.Length > MaxHelpTitleLength) return (false, $"Keep the title to {MaxHelpTitleLength} characters.", null);
            if (cleanSummary.Length > MaxHelpSummaryLength) return (false, $"Keep the summary to {MaxHelpSummaryLength} characters.", null);
            if (cleanBody.Length == 0) return (false, "Write the article.", null);
            if (cleanBody.Length > MaxHelpBodyLength) return (false, $"Keep the article under {MaxHelpBodyLength:N0} characters.", null);
            var article = new SchoolHelpArticle(id ?? Guid.NewGuid(), cleanTitle, cleanSummary, cleanBody) { UpdatedAt = DateTime.UtcNow, By = CurrentActor() };
            if (index >= 0)
            {
                var old = _data.SchoolHelpArticles[index];
                if (old.Title == article.Title && old.Summary == article.Summary && old.Body == article.Body) return (true, "Nothing changed.", old.Id);
                _data.SchoolHelpArticles[index] = article;
            }
            else _data.SchoolHelpArticles.Add(article);
            Save();
            return (true, index >= 0 ? $"\"{cleanTitle}\" saved." : $"\"{cleanTitle}\" added to the help centre.", article.Id);
        }
    }

    public (bool Ok, string Message) DeleteSchoolHelpArticle(Guid id)
    {
        lock (_sync)
        {
            var article = _data.SchoolHelpArticles.FirstOrDefault(x => x.Id == id);
            if (article is null) return (false, "That article was not found.");
            _data.SchoolHelpArticles.Remove(article);
            Save();
            return (true, $"\"{article.Title}\" deleted.");
        }
    }

    // A blank note removes it.
    public (bool Ok, string Message) SaveHelpNote(string slug, string? text)
    {
        lock (_sync)
        {
            var clean = (text ?? "").Replace("\r\n", "\n").Trim();
            if (clean.Length > MaxHelpNoteLength) return (false, $"Keep the note under {MaxHelpNoteLength:N0} characters.");
            var key = slug.Trim().ToLowerInvariant();
            var index = _data.HelpNotes.FindIndex(x => string.Equals(x.Slug, key, StringComparison.OrdinalIgnoreCase));
            if (clean.Length == 0)
            {
                if (index < 0) return (true, "Nothing changed.");
                _data.HelpNotes.RemoveAt(index);
                Save();
                return (true, "The school's note was removed.");
            }
            if (index >= 0 && _data.HelpNotes[index].Text == clean) return (true, "Nothing changed.");
            var note = new HelpNote(key, clean) { UpdatedAt = DateTime.UtcNow, By = CurrentActor() };
            if (index >= 0) _data.HelpNotes[index] = note; else _data.HelpNotes.Add(note);
            Save();
            return (true, "The school's note was saved.");
        }
    }

    // ---- Storage ----

    private static void EnsureHelpSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SchoolHelpArticles (Id TEXT PRIMARY KEY, Title TEXT NOT NULL, Summary TEXT NOT NULL DEFAULT '', Body TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL);
            CREATE TABLE IF NOT EXISTS HelpNotes (Slug TEXT PRIMARY KEY, Text TEXT NOT NULL, UpdatedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadHelp(SqliteConnection connection, StoreData data)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Title, Summary, Body, UpdatedAt, Actor, ActorId FROM SchoolHelpArticles ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.SchoolHelpArticles.Add(new SchoolHelpArticle(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3))
                    { UpdatedAt = Date(reader, 4), By = ReadActor(reader, 5, 6) });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Slug, Text, UpdatedAt, Actor, ActorId FROM HelpNotes ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.HelpNotes.Add(new HelpNote(reader.GetString(0), reader.GetString(1)) { UpdatedAt = Date(reader, 2), By = ReadActor(reader, 3, 4) });
        }
    }

    private static void WriteHelp(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        foreach (var article in data.SchoolHelpArticles)
            Execute(connection, transaction, "INSERT INTO SchoolHelpArticles (Id, Title, Summary, Body, UpdatedAt, Actor, ActorId) VALUES ($id,$title,$summary,$body,$updated,$actor,$actorid);",
                ("$id", article.Id.ToString()), ("$title", article.Title), ("$summary", article.Summary), ("$body", article.Body), ("$updated", Iso(article.UpdatedAt)),
                ("$actor", article.By?.Name), ("$actorid", article.By?.Id?.ToString()));
        foreach (var note in data.HelpNotes)
            Execute(connection, transaction, "INSERT INTO HelpNotes (Slug, Text, UpdatedAt, Actor, ActorId) VALUES ($slug,$text,$updated,$actor,$actorid);",
                ("$slug", note.Slug), ("$text", note.Text), ("$updated", Iso(note.UpdatedAt)), ("$actor", note.By?.Name), ("$actorid", note.By?.Id?.ToString()));
    }
}
