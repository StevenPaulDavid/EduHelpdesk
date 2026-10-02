using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Attachments and links between tickets.
public sealed partial class HelpdeskStore
{
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
    public const int MaxAttachmentsPerUpload = 5;

    // Only these kinds of file are accepted. Nothing that can run or that a browser would render as a page.
    private static readonly Dictionary<string, string> AttachmentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".heic"] = "image/heic",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".log"] = "text/plain", [".csv"] = "text/csv", [".rtf"] = "application/rtf",
        [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint", [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text", [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".msg"] = "application/vnd.ms-outlook", [".eml"] = "message/rfc822"
    };
    // Kinds a browser can safely show in the page as a picture.
    private static readonly HashSet<string> InlineImageTypes = ["image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp"];

    public static string AllowedAttachmentExtensions => string.Join(", ", AttachmentTypes.Keys.OrderBy(x => x));
    public static string AttachmentAcceptAttribute => string.Join(",", AttachmentTypes.Keys);
    public static bool IsInlineImage(TicketAttachment attachment) => InlineImageTypes.Contains(attachment.ContentType);

    private string AttachmentsPath => Path.Combine(Path.GetDirectoryName(_path)!, "attachments");
    private string AttachmentFile(Guid id) => Path.Combine(AttachmentsPath, id.ToString("N"));

    public IReadOnlyList<TicketAttachment> GetTicketAttachments(int number)
    {
        lock (_sync) return _data.TicketAttachments.Where(x => x.TicketNumber == number).OrderByDescending(x => x.UploadedAt).ToList();
    }

    // The attachment and where its file is, or null if it does not belong to that ticket or the file has gone missing.
    public (TicketAttachment Attachment, string Path)? FindAttachment(int number, Guid id)
    {
        lock (_sync)
        {
            var attachment = _data.TicketAttachments.FirstOrDefault(x => x.Id == id && x.TicketNumber == number);
            if (attachment is null) return null;
            var path = AttachmentFile(id);
            return File.Exists(path) ? (attachment, path) : null;
        }
    }

    // Saves one uploaded file to a ticket. The error is a sentence for the user. A requester's own uploads (from the
    // portal) are always visible to them; a technician's only when they choose to share it.
    public string? AddTicketAttachment(int number, string? fileName, Stream content, long length, bool visibleToRequester = false, bool fromRequester = false)
    {
        var name = CleanFileName(fileName);
        if (name.Length == 0) return "A file with no name could not be attached.";
        var extension = Path.GetExtension(name);
        if (!AttachmentTypes.TryGetValue(extension, out var contentType))
            return $"{name} was not attached: {(extension.Length == 0 ? "it has no file type" : $"{extension} files are not accepted")}.";
        if (length <= 0) return $"{name} was not attached because it is empty.";
        if (length > MaxAttachmentBytes) return $"{name} was not attached because it is larger than {MaxAttachmentBytes / (1024 * 1024)} MB.";

        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return "Ticket was not found.";
            Directory.CreateDirectory(AttachmentsPath);
            var id = Guid.NewGuid();
            long written;
            try
            {
                using var file = File.Create(AttachmentFile(id));
                content.CopyTo(file);
                written = file.Length;
            }
            catch (IOException ex) { return $"{name} could not be saved ({ex.Message})."; }
            if (written > MaxAttachmentBytes)
            {
                File.Delete(AttachmentFile(id));
                return $"{name} was not attached because it is larger than {MaxAttachmentBytes / (1024 * 1024)} MB.";
            }

            _data.TicketAttachments.Add(new TicketAttachment(id, number, name, contentType, written, DateTime.UtcNow)
            {
                VisibleToRequester = visibleToRequester || fromRequester,
                FromRequester = fromRequester
            });
            var history = _data.Tickets[index].History.ToList();
            history.Add(new("Attachment added", $"{name} ({FormatSize(written)}) was attached{(fromRequester ? " from the staff portal" : visibleToRequester ? " and shared with the requester" : "")}.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = _data.Tickets[index] with { History = history };
            Save();
            return null;
        }
    }

    public string? RemoveTicketAttachment(int number, Guid id)
    {
        lock (_sync)
        {
            var attachment = _data.TicketAttachments.FirstOrDefault(x => x.Id == id && x.TicketNumber == number);
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (attachment is null || index < 0) return "Attachment was not found.";
            _data.TicketAttachments.Remove(attachment);
            TryDelete(AttachmentFile(id));
            var history = _data.Tickets[index].History.ToList();
            history.Add(new("Attachment removed", $"{attachment.FileName} was removed.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = _data.Tickets[index] with { History = history };
            Save();
            return null;
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        // Backup zips can get this big; attachments never do.
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB"
    };

    // Just the file name: no folders, no control or reserved characters, and not absurdly long.
    private static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(c => !char.IsControl(c) && !invalid.Contains(c)).ToArray()).Trim().Trim('.');
        if (name.Length <= 150) return name;
        var extension = Path.GetExtension(name);
        return name[..(150 - extension.Length)] + extension;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // Removes every attachment file, for a factory reset.
    private void DeleteAllAttachmentFiles()
    {
        try { if (Directory.Exists(AttachmentsPath)) Directory.Delete(AttachmentsPath, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- Links between tickets ----

    // A ticket linked to another, from the point of view of the ticket being looked at.
    public sealed record TicketRelation(int Number, string Title, string Status, string Label);

    public IReadOnlyList<TicketRelation> GetTicketRelations(int number)
    {
        lock (_sync)
        {
            var relations = new List<TicketRelation>();
            foreach (var link in _data.TicketLinks.Where(x => x.TicketNumber == number || x.LinkedNumber == number))
            {
                var otherNumber = link.TicketNumber == number ? link.LinkedNumber : link.TicketNumber;
                if (_data.Tickets.FirstOrDefault(x => x.Number == otherNumber) is not { } other) continue;
                var label = link.Kind == "follow-up" ? (link.TicketNumber == number ? "Follow-up" : "Follow-up to") : "Related";
                relations.Add(new(other.Number, other.Title, other.Status, label));
            }
            // Where this ticket came from first, then related tickets, then its own follow-ups.
            return relations.OrderBy(x => x.Label == "Follow-up to" ? 0 : x.Label == "Related" ? 1 : 2).ThenBy(x => x.Number).ToList();
        }
    }

    public string? LinkRelatedTickets(int number, int otherNumber)
    {
        lock (_sync)
        {
            if (number == otherNumber) return "A ticket cannot be linked to itself.";
            var a = _data.Tickets.FindIndex(x => x.Number == number);
            var b = _data.Tickets.FindIndex(x => x.Number == otherNumber);
            if (a < 0) return "Ticket was not found.";
            if (b < 0) return $"There is no ticket #{otherNumber}.";
            if (AreLinked(number, otherNumber)) return $"#{number} and #{otherNumber} are already linked.";
            _data.TicketLinks.Add(new TicketLink(number, otherNumber, "related"));
            AddLinkHistory(a, "Related ticket linked", $"Linked to #{otherNumber} - {_data.Tickets[b].Title}.");
            AddLinkHistory(b, "Related ticket linked", $"Linked to #{number} - {_data.Tickets[a].Title}.");
            Save();
            return null;
        }
    }

    public string? UnlinkTickets(int number, int otherNumber)
    {
        lock (_sync)
        {
            var removed = _data.TicketLinks.RemoveAll(x => (x.TicketNumber == number && x.LinkedNumber == otherNumber) || (x.TicketNumber == otherNumber && x.LinkedNumber == number));
            if (removed == 0) return "Those tickets are not linked.";
            foreach (var (ticket, other) in new[] { (number, otherNumber), (otherNumber, number) })
            {
                var index = _data.Tickets.FindIndex(x => x.Number == ticket);
                if (index >= 0) AddLinkHistory(index, "Ticket link removed", $"No longer linked to #{other}.");
            }
            Save();
            return null;
        }
    }

    // Creates a new ticket for the same requester, assets, category and priority, linked to this one as its follow-up.
    // The new ticket is open, with a due date worked out from its own start. Returns its number, or null with an error.
    public (int? Number, string? Error) CreateFollowUpTicket(int number)
    {
        lock (_sync)
        {
            var original = _data.Tickets.FirstOrDefault(x => x.Number == number);
            if (original is null) return (null, "Ticket was not found.");
            var now = DateTime.UtcNow;
            var sla = SlaFor(original.Priority, original.Category);
            var followUp = AddTicket(new TicketRecord(0, $"Follow-up: {original.Title}", $"Follow-up to #{original.Number}: {original.Title}.",
                original.RequesterId, original.AssetIds.ToList(), original.TechnicianId, original.Priority, _data.Statuses.FirstOrDefault() ?? "Open",
                original.Category, now, null, sla, CalculateDueDate(sla, now), false, false, original.TeamName) { Type = original.Type, SubCategory = original.SubCategory });
            _data.TicketLinks.Add(new TicketLink(number, followUp, "follow-up"));
            AddLinkHistory(_data.Tickets.FindIndex(x => x.Number == number), "Follow-up created", $"Follow-up ticket #{followUp} was created.");
            AddLinkHistory(_data.Tickets.FindIndex(x => x.Number == followUp), "Follow-up", $"Created as a follow-up to #{number} - {original.Title}.");
            Save();
            return (followUp, null);
        }
    }

    private bool AreLinked(int a, int b) =>
        _data.TicketLinks.Any(x => (x.TicketNumber == a && x.LinkedNumber == b) || (x.TicketNumber == b && x.LinkedNumber == a));

    private void AddLinkHistory(int ticketIndex, string action, string details)
    {
        if (ticketIndex < 0) return;
        var history = _data.Tickets[ticketIndex].History.ToList();
        history.Add(new(action, details, DateTime.UtcNow) { By = CurrentActor() });
        _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };
    }

    // When a ticket is deleted, its attachments and links go with it.
    private void RemoveTicketExtras(int number)
    {
        foreach (var attachment in _data.TicketAttachments.Where(x => x.TicketNumber == number).ToList())
            TryDelete(AttachmentFile(attachment.Id));
        _data.TicketAttachments.RemoveAll(x => x.TicketNumber == number);
        _data.TicketLinks.RemoveAll(x => x.TicketNumber == number || x.LinkedNumber == number);
        RemoveProjectTicketLinks(number);
        RemoveOnboarding(number);
    }

    // When a ticket is merged into another, its attachments move across and its links point at the ticket it was merged into.
    private void MoveTicketExtras(int sourceNumber, int targetNumber)
    {
        for (var i = 0; i < _data.TicketAttachments.Count; i++)
            if (_data.TicketAttachments[i].TicketNumber == sourceNumber)
                _data.TicketAttachments[i] = _data.TicketAttachments[i] with { TicketNumber = targetNumber };
        foreach (var link in _data.TicketLinks.Where(x => x.TicketNumber == sourceNumber || x.LinkedNumber == sourceNumber).ToList())
        {
            _data.TicketLinks.Remove(link);
            var a = link.TicketNumber == sourceNumber ? targetNumber : link.TicketNumber;
            var b = link.LinkedNumber == sourceNumber ? targetNumber : link.LinkedNumber;
            if (a == b || AreLinked(a, b)) continue;
            _data.TicketLinks.Add(link with { TicketNumber = a, LinkedNumber = b });
        }
        MoveProjectTicketLinks(sourceNumber, targetNumber);
    }
}
