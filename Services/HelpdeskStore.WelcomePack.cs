using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;
using PdfSharp.Pdf.IO;

namespace EduHelpdesk.Services;

// The welcome pack a new starter is handed (Services/WelcomePackPdf.cs): the school's IT information, and the PDFs
// uploaded in Settings → Onboarding checklists that each template - and then each onboarding - chooses to include.
public sealed partial class HelpdeskStore
{
    // Handbooks and policies run longer than a ticket's screenshot, so these get more room than an attachment.
    public const long MaxWelcomePackDocumentBytes = 25 * 1024 * 1024;
    public const int MaxOnboardingDocuments = 30;
    public const int MaxOnboardingItInfoLength = 4000;

    public IReadOnlyList<OnboardingDocument> OnboardingDocuments { get { lock (_sync) return _data.OnboardingDocuments.ToList(); } }

    // The document and where its file is, or null if either has gone.
    public (OnboardingDocument Document, string Path)? FindOnboardingDocument(Guid id)
    {
        lock (_sync)
            return _data.OnboardingDocuments.FirstOrDefault(x => x.Id == id) is { } document && File.Exists(AttachmentFile(id)) ? (document, AttachmentFile(id)) : null;
    }
    public string OnboardingItInfo { get { lock (_sync) return _data.OnboardingItInfo; } }

    public (bool Ok, string Message) SetOnboardingItInfo(string? text)
    {
        var value = (text ?? "").Replace("\r\n", "\n").Trim();
        if (value.Length > MaxOnboardingItInfoLength) return (false, $"Keep the IT information under {MaxOnboardingItInfoLength} characters.");
        lock (_sync)
        {
            if (_data.OnboardingItInfo == value) return (true, "Nothing had changed.");
            _data.OnboardingItInfo = value;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Onboarding", null, null, "Welcome pack", "IT information changed",
                value.Length == 0 ? "The school IT information was cleared." : $"The school IT information was updated ({value.Length} characters)."));
            Save();
            return (true, "IT information saved. It goes on the cover of every welcome pack.");
        }
    }

    // A PDF for the back of welcome packs. It is opened before it is kept: one PDFsharp can't read (password-protected
    // or damaged) would otherwise only fail when somebody tried to print a pack.
    public (bool Ok, string Message) AddOnboardingDocument(string? name, string? fileName, Stream content, long length)
    {
        var file = CleanFileName(fileName);
        if (file.Length == 0) return (false, "Choose a PDF to upload.");
        if (!string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase)) return (false, $"{file} isn't a PDF. Save it as a PDF first - Word and most other programs can.");
        if (length <= 0) return (false, $"{file} is empty.");
        if (length > MaxWelcomePackDocumentBytes) return (false, $"{file} is larger than {MaxWelcomePackDocumentBytes / (1024 * 1024)} MB.");
        var title = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name.Trim();
        if (title.Length > 100) return (false, "Keep the document's name under 100 characters.");
        lock (_sync)
            if (_data.OnboardingDocuments.Count >= MaxOnboardingDocuments) return (false, $"There can be up to {MaxOnboardingDocuments} welcome pack documents.");

        var id = Guid.NewGuid();
        int pages;
        try
        {
            Directory.CreateDirectory(AttachmentsPath);
            using (var target = File.Create(AttachmentFile(id))) content.CopyTo(target);
            if (new FileInfo(AttachmentFile(id)).Length > MaxWelcomePackDocumentBytes)
            {
                TryDelete(AttachmentFile(id));
                return (false, $"{file} is larger than {MaxWelcomePackDocumentBytes / (1024 * 1024)} MB.");
            }
            try
            {
                using var pdf = PdfReader.Open(AttachmentFile(id), PdfDocumentOpenMode.Import);
                pages = pdf.PageCount;
            }
            catch (Exception)
            {
                TryDelete(AttachmentFile(id));
                return (false, $"{file} couldn't be read as a PDF - it may be password-protected or damaged. Save a fresh copy and try again.");
            }
            if (pages == 0)
            {
                TryDelete(AttachmentFile(id));
                return (false, $"{file} has no pages.");
            }
        }
        catch (IOException ex)
        {
            TryDelete(AttachmentFile(id));
            return (false, $"{file} couldn't be saved ({ex.Message}).");
        }

        lock (_sync)
        {
            _data.OnboardingDocuments.Add(new OnboardingDocument(id, title, file, new FileInfo(AttachmentFile(id)).Length, pages, DateTime.UtcNow) { By = CurrentActor() });
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Onboarding", null, null, "Welcome pack", "Document added", $"{title} ({file}, {Plural(pages, "page")})."));
            Save();
        }
        return (true, $"{title} added. Tick the templates whose welcome pack should include it.");
    }

    public (bool Ok, string Message) RenameOnboardingDocument(Guid id, string? name)
    {
        var title = (name ?? "").Trim();
        if (title.Length == 0) return (false, "Give the document a name.");
        if (title.Length > 100) return (false, "Keep the document's name under 100 characters.");
        lock (_sync)
        {
            var index = _data.OnboardingDocuments.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "That document couldn't be found.");
            var before = _data.OnboardingDocuments[index].Name;
            if (before == title) return (true, "Nothing had changed.");
            _data.OnboardingDocuments[index] = _data.OnboardingDocuments[index] with { Name = title };
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Onboarding", null, null, "Welcome pack", "Document renamed", $"{before} → {title}."));
            Save();
            return (true, "Document renamed.");
        }
    }

    // Takes it out of every template and every onboarding's pack too. The file goes once the record is gone.
    public (bool Ok, string Message) DeleteOnboardingDocument(Guid id)
    {
        OnboardingDocument? document;
        lock (_sync)
        {
            document = _data.OnboardingDocuments.FirstOrDefault(x => x.Id == id);
            if (document is null) return (false, "That document couldn't be found.");
            _data.OnboardingDocuments.Remove(document);
            for (var i = 0; i < _data.OnboardingTemplates.Count; i++)
                if (_data.OnboardingTemplates[i].DocumentIds.Contains(id))
                    _data.OnboardingTemplates[i] = _data.OnboardingTemplates[i] with { DocumentIds = _data.OnboardingTemplates[i].DocumentIds.Where(x => x != id).ToList() };
            for (var i = 0; i < _data.Onboardings.Count; i++)
                if (_data.Onboardings[i].PackDocumentIds.Contains(id))
                    _data.Onboardings[i] = _data.Onboardings[i] with { PackDocumentIds = _data.Onboardings[i].PackDocumentIds.Where(x => x != id).ToList() };
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Onboarding", null, null, "Welcome pack", "Document deleted", $"{document.Name} ({document.FileName})."));
            Save();
        }
        TryDelete(AttachmentFile(id));
        return (true, $"{document.Name} deleted. It's no longer in any welcome pack.");
    }

    public (bool Ok, string Message) SetOnboardingTemplateDocuments(Guid templateId, IEnumerable<Guid>? documentIds) => EditTemplate(templateId, template =>
    {
        var chosen = (documentIds ?? []).ToHashSet();
        var ordered = _data.OnboardingDocuments.Where(x => chosen.Contains(x.Id)).Select(x => x.Id).ToList();
        if (ordered.SequenceEqual(template.DocumentIds)) return (null, "Nothing had changed.");
        return (template with { DocumentIds = ordered }, ordered.Count == 0 ? $"{template.Name}'s welcome pack has no extra documents now." : $"{template.Name}'s welcome pack includes {Plural(ordered.Count, "document")}.");
    });

    // For one person: the template's choice, changed for them alone.
    public (bool Ok, string Message) SetOnboardingPackDocuments(int number, IEnumerable<Guid>? documentIds) => EditOnboarding(number, record =>
    {
        var chosen = (documentIds ?? []).ToHashSet();
        var ordered = _data.OnboardingDocuments.Where(x => chosen.Contains(x.Id)).Select(x => x.Id).ToList();
        if (ordered.SequenceEqual(record.PackDocumentIds)) return (null, "Nothing had changed.", []);
        var names = _data.OnboardingDocuments.Where(x => chosen.Contains(x.Id)).Select(x => x.Name).ToList();
        return (record with { PackDocumentIds = ordered }, "Welcome pack documents saved.",
            [Line("Welcome pack changed", names.Count == 0 ? "No extra documents in the welcome pack." : "The welcome pack includes: " + string.Join(", ", names) + ".")]);
    }, allowCancelled: true);

    // Everything the welcome pack PDF is made from, gathered under the lock as one moment. `password` is the temporary
    // one if the caller may show it and it is still to hand (TemporaryPasswords); `address` is the site's address.
    public WelcomePackInput? WelcomePackFor(int number, string? password, string address)
    {
        lock (_sync)
        {
            var record = _data.Onboardings.FirstOrDefault(x => x.TicketNumber == number);
            var starter = record is null ? null : _data.Users.FirstOrDefault(x => x.Id == record.StarterId);
            if (record is null || starter is null) return null;
            var documents = record.PackDocumentIds
                .Select(id => _data.OnboardingDocuments.FirstOrDefault(x => x.Id == id)).OfType<OnboardingDocument>()
                .Select(x => (x, AttachmentFile(x.Id))).ToList();
            var equipment = _data.Assets.Where(x => x.AssignedUserId == starter.Id && !IsDisposed(x))
                .OrderBy(x => x.Type, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AssetTag, StringComparer.OrdinalIgnoreCase).ToList();
            return new WelcomePackInput(
                _data.Branding.BrandName, _data.Branding.PrimaryColor, LogoFile, starter, record,
                record.LineManagerId is { } manager ? _data.Users.FirstOrDefault(x => x.Id == manager)?.Name : null,
                address, starter.PasswordHash is not null, password, equipment, _data.OnboardingItInfo, documents, DateTime.Now);
        }
    }

    // ---- Storage ----

    private static void EnsureWelcomePackSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS OnboardingDocuments (Id TEXT PRIMARY KEY, Position INTEGER NOT NULL, Name TEXT NOT NULL, FileName TEXT NOT NULL,
                Size INTEGER NOT NULL, Pages INTEGER NOT NULL, UploadedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL);
            CREATE TABLE IF NOT EXISTS OnboardingTemplateDocuments (TemplateId TEXT NOT NULL, DocumentId TEXT NOT NULL, Position INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS OnboardingPackDocuments (TicketNumber INTEGER NOT NULL, DocumentId TEXT NOT NULL, Position INTEGER NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadWelcomePack(SqliteConnection connection, StoreData data)
    {
        data.OnboardingItInfo = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'OnboardingItInfo';") as string ?? "";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, FileName, Size, Pages, UploadedAt, Actor, ActorId FROM OnboardingDocuments ORDER BY Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.OnboardingDocuments.Add(new OnboardingDocument(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt32(4), Date(reader, 5)) { By = ReadActor(reader, 6, 7) });
        }
        var known = data.OnboardingDocuments.Select(x => x.Id).ToHashSet();
        var templates = data.OnboardingTemplates.ToDictionary(x => x.Id);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TemplateId, DocumentId FROM OnboardingTemplateDocuments ORDER BY TemplateId, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (templates.TryGetValue(Guid.Parse(reader.GetString(0)), out var template) && Guid.Parse(reader.GetString(1)) is var id && known.Contains(id))
                    template.DocumentIds.Add(id);
        }
        var records = data.Onboardings.ToDictionary(x => x.TicketNumber);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, DocumentId FROM OnboardingPackDocuments ORDER BY TicketNumber, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (records.TryGetValue(reader.GetInt32(0), out var record) && Guid.Parse(reader.GetString(1)) is var id && known.Contains(id))
                    record.PackDocumentIds.Add(id);
        }
    }

    private static void WriteWelcomePack(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "OnboardingItInfo", data.OnboardingItInfo);
        var position = 0;
        foreach (var document in data.OnboardingDocuments)
            Execute(connection, transaction, "INSERT INTO OnboardingDocuments (Id, Position, Name, FileName, Size, Pages, UploadedAt, Actor, ActorId) VALUES ($id,$position,$name,$file,$size,$pages,$uploaded,$actor,$actorid);",
                ("$id", document.Id.ToString()), ("$position", position++), ("$name", document.Name), ("$file", document.FileName), ("$size", document.Size),
                ("$pages", document.Pages), ("$uploaded", Iso(document.UploadedAt)), ("$actor", document.By?.Name), ("$actorid", document.By?.Id?.ToString()));
        foreach (var template in data.OnboardingTemplates)
        {
            position = 0;
            foreach (var id in template.DocumentIds)
                Execute(connection, transaction, "INSERT INTO OnboardingTemplateDocuments (TemplateId, DocumentId, Position) VALUES ($template,$document,$position);",
                    ("$template", template.Id.ToString()), ("$document", id.ToString()), ("$position", position++));
        }
        var ticketNumbers = data.Tickets.Select(x => x.Number).ToHashSet();
        foreach (var record in data.Onboardings.Where(x => ticketNumbers.Contains(x.TicketNumber)))
        {
            position = 0;
            foreach (var id in record.PackDocumentIds)
                Execute(connection, transaction, "INSERT INTO OnboardingPackDocuments (TicketNumber, DocumentId, Position) VALUES ($number,$document,$position);",
                    ("$number", record.TicketNumber), ("$document", id.ToString()), ("$position", position++));
        }
    }
}

public sealed record WelcomePackInput(
    string OrganisationName,
    string PrimaryColor,
    string? LogoPath,
    UserRecord Starter,
    OnboardingRecord Record,
    string? LineManagerName,
    string SiteAddress,
    bool HasPortalAccount,
    string? Password,
    IReadOnlyList<AssetRecord> Equipment,
    string ItInformation,
    IReadOnlyList<(OnboardingDocument Document, string Path)> Documents,
    DateTime PreparedAt);
