using System.Globalization;
using EduHelpdesk.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using static EduHelpdesk.Services.ProposalPdf;

namespace EduHelpdesk.Services;

// One ticket as a PDF report, everything about it in the order someone reading it later needs:
//   the ticket      - number, title, every field, the description and custom fields;
//   what it touched - linked assets, parts used, related tickets;
//   the conversation, oldest first, and the history of every change - the internal notes and the history only in the
//                     staff copy, which the requester copy (no internal notes) leaves out along with IT-only files;
//   attachments     - a list, then the files themselves: PDFs page by page, pictures one to a page, text files written
//                     out. Word, Excel and anything else that can't go into a PDF is listed with the reason instead.
// Laid out like the project proposal (ProposalPdf, whose helpers it uses): MigraDoc writes the report, the attachments
// are added after it with PDFsharp, and the report is laid out a second time once their page numbers are known.
public static class TicketReportPdf
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");
    private static readonly Color Ink = new(0x1F, 0x2A, 0x2E);
    private static readonly Color Grey = new(0x5D, 0x6B, 0x70);
    private static readonly Color Rule = new(0xC9, 0xD3, 0xD5);
    private static readonly Color NoteShade = new(0xFD, 0xF3, 0xDC);
    private const double ContentWidthCm = 17;
    // Text files are written into the report; past this they are cut short with a note.
    private const int MaxTextCharacters = 60_000;

    public static byte[] Build(TicketReportInput input)
    {
        if (!PdfFonts.IsAvailable)
            throw new ProposalException("There is no font on this server to write the PDF with. Put a TrueType font family in the app's Fonts folder as regular.ttf, bold.ttf, italic.ttf and bolditalic.ttf.");
        var files = new List<FileEntry>();
        var opened = new List<IDisposable>();
        try
        {
            // Every file is opened first: how many pages each takes decides the page numbers in the list.
            var index = 0;
            foreach (var attachment in input.Attachments) files.Add(Load(input, attachment, ++index, opened));

            var generated = Render(Compose(input, files, 0, 0)).PageCount;
            var pdf = Render(Compose(input, files, generated + 1, generated + files.Sum(x => x.Pages)));
            if (pdf.PageCount != generated)
            {
                // The numbers changed a line break somewhere - lay it out once more with the corrected count.
                generated = pdf.PageCount;
                pdf = Render(Compose(input, files, generated + 1, generated + files.Sum(x => x.Pages)));
            }
            var total = pdf.PageCount + files.Sum(x => x.Pages);
            foreach (var file in files)
            {
                if (file.Pdf is { } source)
                    foreach (var page in source.Pages) pdf.AddPage(page);
                else if (file.Image is { } image)
                    DrawPicture(pdf, input, file, image, total);
            }
            pdf.Info.Title = Title(input.Ticket);
            pdf.Info.Author = input.OrganisationName;
            pdf.Info.Creator = "EduHelpdesk";
            using var output = new MemoryStream();
            pdf.Save(output, false);
            return output.ToArray();
        }
        finally
        {
            foreach (var x in opened) x.Dispose();
        }
    }

    public static string FileName(TicketRecord ticket) => $"Ticket {ticket.Number} report.pdf";

    private static string Title(TicketRecord ticket) => $"Ticket #{ticket.Number} {ticket.Title} - report";

    // ---- The attachments -------------------------------------------------------------------------------------------

    private sealed class FileEntry(TicketAttachment attachment, int number)
    {
        public TicketAttachment Attachment { get; } = attachment;
        public int Number { get; } = number;
        public PdfDocument? Pdf { get; init; }
        public XImage? Image { get; init; }
        // A text file's words, written into the report itself rather than added as pages.
        public string? Text { get; init; }
        public bool TextCut { get; init; }
        // Why it isn't in the PDF, when it isn't.
        public string? Left { get; init; }
        public int Pages => Pdf?.PageCount ?? (Image is null ? 0 : 1);
        // Where it starts in the finished PDF: set while the report is composed.
        public int StartPage { get; set; }
    }

    private static FileEntry Load(TicketReportInput input, TicketAttachment attachment, int number, List<IDisposable> opened)
    {
        if (!input.FilePaths.TryGetValue(attachment.Id, out var path))
            return new FileEntry(attachment, number) { Left = "The file is missing from the server." };
        switch (attachment.ContentType)
        {
            case "application/pdf":
                try
                {
                    var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                    opened.Add(source);
                    return source.PageCount == 0
                        ? new FileEntry(attachment, number) { Left = "The PDF has no pages." }
                        : new FileEntry(attachment, number) { Pdf = source };
                }
                catch (Exception)
                {
                    return new FileEntry(attachment, number) { Left = "This PDF couldn't be read to merge it - it may be password-protected or damaged. It is kept with the ticket in EduHelpdesk." };
                }
            case "image/png" or "image/jpeg" or "image/bmp" or "image/gif":
                try
                {
                    // Held in memory until the PDF is saved: PDFsharp reads the picture when it writes it out.
                    var stream = new MemoryStream(File.ReadAllBytes(path));
                    opened.Add(stream);
                    var image = XImage.FromStream(stream);
                    opened.Add(image);
                    return new FileEntry(attachment, number) { Image = image };
                }
                catch (Exception)
                {
                    return new FileEntry(attachment, number) { Left = "This picture couldn't be placed in the PDF. It is kept with the ticket in EduHelpdesk." };
                }
            case "text/plain" or "text/csv":
                try
                {
                    var text = File.ReadAllText(path);
                    var cut = text.Length > MaxTextCharacters;
                    return new FileEntry(attachment, number) { Text = cut ? text[..MaxTextCharacters] : text, TextCut = cut };
                }
                catch (Exception)
                {
                    return new FileEntry(attachment, number) { Left = "This file couldn't be read. It is kept with the ticket in EduHelpdesk." };
                }
            default:
                return new FileEntry(attachment, number) { Left = $"{Kind(attachment.ContentType)} can't be merged into a PDF. It is kept with the ticket in EduHelpdesk." };
        }
    }

    private static string Kind(string contentType) => contentType switch
    {
        "application/msword" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/vnd.oasis.opendocument.text" or "application/rtf" => "A Word document",
        "application/vnd.ms-excel" or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or "application/vnd.oasis.opendocument.spreadsheet" => "A spreadsheet",
        "application/vnd.ms-powerpoint" or "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "A presentation",
        "image/webp" or "image/heic" => "This kind of picture",
        "application/vnd.ms-outlook" or "message/rfc822" => "An email",
        _ => "This kind of file"
    };

    // ---- The report (MigraDoc) -------------------------------------------------------------------------------------

    private static Document Compose(TicketReportInput input, List<FileEntry> files, int firstFilePage, int totalPages)
    {
        var ticket = input.Ticket;
        var brand = ParseColor(input.PrimaryColor);
        var document = new Document();
        document.Info.Title = Title(ticket);
        document.Info.Author = input.OrganisationName;
        DefineStyles(document, brand);

        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.PageWidth = Unit.FromMillimeter(210);
        setup.PageHeight = Unit.FromMillimeter(297);
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = Unit.FromCentimeter(2);
        setup.FooterDistance = Unit.FromCentimeter(1);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Style = "Footer";
        footer.AddText(FooterText(input) + " · Page ");
        footer.AddPageField();
        if (totalPages > 0) footer.AddText($" of {totalPages}");

        var page = firstFilePage;
        foreach (var file in files.Where(x => x.Pages > 0))
        {
            file.StartPage = page;
            page += file.Pages;
        }

        Heading(section, input, brand);
        Details(section, input);
        Description(section, input);
        Linked(section, input);
        Conversation(section, input);
        History(section, input);
        Attachments(section, input, files, firstFilePage > 0);
        return document;
    }

    private static void DefineStyles(Document document, Color brand)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Body"; // PdfFonts answers every family with the one font it found
        normal.Font.Size = 10;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var h1 = document.Styles[StyleNames.Heading1]!;
        h1.Font.Size = 14;
        h1.Font.Bold = true;
        h1.Font.Color = brand;
        h1.ParagraphFormat.SpaceBefore = Unit.FromPoint(16);
        h1.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
        h1.ParagraphFormat.KeepWithNext = true;

        var h2 = document.Styles[StyleNames.Heading2]!;
        h2.Font.Size = 11;
        h2.Font.Bold = true;
        h2.Font.Color = Ink;
        h2.ParagraphFormat.SpaceBefore = Unit.FromPoint(10);
        h2.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
        h2.ParagraphFormat.KeepWithNext = true;

        Add(document, "Eyebrow", x => { x.Font.Size = 8.5; x.Font.Bold = true; x.Font.Color = brand; });
        Add(document, "ReportTitle", x => { x.Font.Size = 20; x.Font.Bold = true; x.Font.Color = Ink; x.ParagraphFormat.SpaceBefore = Unit.FromPoint(4); });
        Add(document, "Muted", x => { x.Font.Size = 9.5; x.Font.Color = Grey; });
        Add(document, "Small", x => { x.Font.Size = 8.5; x.Font.Color = Grey; x.ParagraphFormat.SpaceAfter = 0; });
        Add(document, "Footer", x => { x.Font.Size = 8; x.Font.Color = Grey; });
        Add(document, "Plain", x => { x.Font.Size = 8.5; x.ParagraphFormat.SpaceAfter = 0; });
    }

    private static void Add(Document document, string name, Action<Style> setup) => setup(document.Styles.AddStyle(name, StyleNames.Normal));

    private static void Heading(Section section, TicketReportInput input, Color brand)
    {
        var ticket = input.Ticket;
        if (input.LogoPath is { } logo && CanLoadPicture(logo))
        {
            var image = section.AddImage(logo);
            image.Height = Unit.FromCentimeter(1.4);
            image.LockAspectRatio = true;
        }
        section.AddParagraph($"{input.OrganisationName.ToUpperInvariant()} · TICKET REPORT", "Eyebrow").Format.SpaceBefore = Unit.FromPoint(8);
        section.AddParagraph($"#{ticket.Number}  {ticket.Title}", "ReportTitle");
        var line = section.AddParagraph();
        line.Style = "Muted";
        line.AddFormattedText(ticket.Status, TextFormat.Bold).Color = brand;
        line.AddText($" · {ticket.Priority} priority · {ticket.Type} · prepared {Moment(input.PreparedAt)}{(input.PreparedBy is { } by ? $" by {by}" : "")}");
        if (input.IncludeInternal)
            section.AddParagraph("Staff copy: includes internal notes, which the requester never sees.", "Muted");
    }

    private static void Details(Section section, TicketReportInput input)
    {
        var ticket = input.Ticket;
        section.AddParagraph("Details", StyleNames.Heading1);
        var facts = Table(section, 4.5, ContentWidthCm - 4.5);
        var requester = input.Requester;
        Fact(facts, "Requester", requester is null ? "Unknown"
            : string.Join(" · ", new[] { requester.Name, requester.Email, requester.Department, requester.Location }.Where(x => !string.IsNullOrWhiteSpace(x))));
        Fact(facts, "Assigned to", input.TechnicianName ?? "Not assigned");
        Fact(facts, "Team", ticket.TeamName ?? "No team");
        Fact(facts, "Category", ticket.Category + (string.IsNullOrWhiteSpace(ticket.SubCategory) ? "" : $" › {ticket.SubCategory}"));
        Fact(facts, "Location", string.IsNullOrWhiteSpace(ticket.Location) ? "Not given" : ticket.Location);
        Fact(facts, "Status", ticket.Status + (ticket.IsSlaPaused ? " (SLA paused)" : ""));
        Fact(facts, "Priority", ticket.Priority);
        Fact(facts, "Type", ticket.Type);
        Fact(facts, "SLA", input.Sla is { } sla ? $"{sla.Name} ({sla.Duration} {sla.DurationUnit})" : "No SLA");
        Fact(facts, "Raised", Moment(ticket.CreatedAt.ToLocalTime()));
        Fact(facts, "Due", ticket.DueDate is { } due ? Moment(due.ToLocalTime()) + (ticket.DueDateOverridden ? " (set by hand)" : "") : "No due date");
        Fact(facts, "Closed", ticket.ClosedAt is { } closed ? Moment(closed.ToLocalTime()) : "Still open");
        Fact(facts, "Last updated", Moment(ticket.LastModifiedAt.ToLocalTime()));
    }

    private static void Description(Section section, TicketReportInput input)
    {
        section.AddParagraph("Description", StyleNames.Heading1);
        if (string.IsNullOrWhiteSpace(input.Ticket.Description)) section.AddParagraph("No description was given.", "Muted");
        else MultiLine(section, input.Ticket.Description.Trim());

        if (input.Attributes.Count == 0) return;
        section.AddParagraph("Custom fields", StyleNames.Heading2);
        var table = Table(section, 4.5, ContentWidthCm - 4.5);
        foreach (var (name, value) in input.Attributes) Fact(table, name, string.IsNullOrWhiteSpace(value) ? "—" : value);
    }

    private static void Linked(Section section, TicketReportInput input)
    {
        section.AddParagraph("Assets, parts and related tickets", StyleNames.Heading1);

        section.AddParagraph("Linked assets", StyleNames.Heading2);
        if (input.Assets.Count == 0) section.AddParagraph("None linked.", "Muted");
        else
        {
            var table = Table(section, 2.6, 4.8, 2.6, 3.4, 3.6);
            Header(table, "Asset tag", "Make and model", "Type", "Serial number", "Location");
            foreach (var asset in input.Assets)
                Cells(table.AddRow(), asset.AssetTag, $"{asset.Make} {asset.Model}".Trim(), asset.Type, asset.SerialNumber, asset.Location);
        }

        section.AddParagraph("Parts used", StyleNames.Heading2);
        if (input.Parts.Count == 0) section.AddParagraph("None.", "Muted");
        else
        {
            var table = Table(section, 9, 5, 3);
            Header(table, "Part", "SKU", "Quantity");
            foreach (var (part, quantity) in input.Parts)
                Cells(table.AddRow(), part.Name, part.Sku ?? "", quantity.ToString(CultureInfo.InvariantCulture));
        }

        section.AddParagraph("Related tickets", StyleNames.Heading2);
        if (input.Relations.Count == 0) section.AddParagraph("None.", "Muted");
        else
        {
            var table = Table(section, 2, 9, 3, 3);
            Header(table, "Ticket", "Title", "Status", "Link");
            foreach (var relation in input.Relations)
                Cells(table.AddRow(), $"#{relation.Number}", relation.Title, relation.Status, relation.Label);
        }
    }

    private static void Conversation(Section section, TicketReportInput input)
    {
        var comments = input.Ticket.Comments.Where(x => input.IncludeInternal || !x.IsInternal).OrderBy(x => x.CreatedAt).ToList();
        var internals = input.Ticket.Comments.Count(x => x.IsInternal);
        section.AddParagraph($"Conversation ({comments.Count})", StyleNames.Heading1);
        if (!input.IncludeInternal && internals > 0)
            section.AddParagraph($"{Plural(internals, "internal note")} left out of this copy.", "Muted");
        if (comments.Count == 0)
        {
            section.AddParagraph("No comments yet.", "Muted");
            return;
        }
        foreach (var comment in comments)
        {
            var table = Table(section, ContentWidthCm);
            table.Borders.Bottom.Visible = false;
            table.KeepTogether = comment.Text.Length < 1500;
            var head = table.AddRow();
            head.KeepWith = 1;
            var who = head.Cells[0].AddParagraph();
            who.Style = "Small";
            who.AddFormattedText(Actor.Label(comment.By), TextFormat.Bold).Color = Ink;
            who.AddText($" · {Moment(comment.CreatedAt.ToLocalTime())}");
            if (comment.IsInternal) who.AddFormattedText(" · INTERNAL NOTE", TextFormat.Bold);
            if (comment.FromRequester) who.AddText(" · from the staff portal");
            var body = table.AddRow();
            body.Borders.Bottom.Visible = true;
            body.Borders.Bottom.Width = 0.5;
            body.Borders.Bottom.Color = Rule;
            body.BottomPadding = Unit.FromPoint(6);
            var text = body.Cells[0].AddParagraph();
            text.Format.Font.Size = 10;
            AddLines(text, comment.Text.Trim());
            if (comment.IsInternal) { head.Shading.Color = NoteShade; body.Shading.Color = NoteShade; }
        }
    }

    private static void History(Section section, TicketReportInput input)
    {
        // The IT team's own record - who it was assigned to, every field changed, the names of files kept back - so the
        // copy that leaves the team doesn't carry it.
        if (!input.IncludeInternal) return;
        var history = input.Ticket.History.OrderBy(x => x.CreatedAt).ToList();
        section.AddParagraph($"History ({history.Count})", StyleNames.Heading1);
        if (history.Count == 0)
        {
            section.AddParagraph("No changes recorded.", "Muted");
            return;
        }
        var table = Table(section, 3.4, 3.2, 3.6, 6.8);
        Header(table, "When", "Who", "What", "Details");
        foreach (var entry in history)
            Cells(table.AddRow(), Moment(entry.CreatedAt.ToLocalTime()), Actor.Label(entry.By), entry.Action, entry.Details);
    }

    private static void Attachments(Section section, TicketReportInput input, List<FileEntry> files, bool numbered)
    {
        section.AddParagraph($"Attachments ({files.Count})", StyleNames.Heading1);
        if (input.AttachmentsLeftOut > 0)
            section.AddParagraph($"{Plural(input.AttachmentsLeftOut, "file")} kept for the IT team only {(input.AttachmentsLeftOut == 1 ? "is" : "are")} left out of this copy.", "Muted");
        if (files.Count == 0)
        {
            if (input.AttachmentsLeftOut == 0) section.AddParagraph("No files are attached to this ticket.", "Muted");
            return;
        }
        section.AddParagraph("PDFs follow this report page by page and pictures one to a page; text files are written out below. Anything else is listed with the reason it isn't included.", "Muted");
        var table = Table(section, 0.8, 6.4, 1.8, 3.4, 4.6);
        Header(table, "", "File", "Size", "Added", "In this report");
        foreach (var file in files)
        {
            var attachment = file.Attachment;
            var row = table.AddRow();
            row.Cells[0].AddParagraph(file.Number.ToString(CultureInfo.InvariantCulture));
            var name = row.Cells[1].AddParagraph();
            name.AddFormattedText(attachment.FileName, TextFormat.Bold);
            var who = row.Cells[1].AddParagraph(attachment.FromRequester ? "From the requester" : attachment.VisibleToRequester ? "Shared with the requester" : "IT team only");
            who.Style = "Small";
            row.Cells[2].AddParagraph(Size(attachment.Size));
            row.Cells[3].AddParagraph(Moment(attachment.UploadedAt.ToLocalTime()));
            row.Cells[4].AddParagraph(file.Left is not null ? "Not included - see below"
                : file.Text is not null ? "Written out below"
                : !numbered ? "" : file.Pages == 1 ? $"Page {file.StartPage}" : $"Pages {file.StartPage}-{file.StartPage + file.Pages - 1}");
        }

        var left = files.Where(x => x.Left is not null).ToList();
        if (left.Count > 0)
        {
            section.AddParagraph("Not included in this PDF", StyleNames.Heading2);
            foreach (var file in left)
            {
                var line = section.AddParagraph();
                line.AddFormattedText($"{file.Number}. {file.Attachment.FileName}", TextFormat.Bold);
                line.AddText($": {file.Left}");
            }
        }

        foreach (var file in files.Where(x => x.Text is not null))
        {
            section.AddParagraph($"{file.Number}. {file.Attachment.FileName}", StyleNames.Heading2);
            var text = section.AddParagraph();
            text.Style = "Plain";
            AddLines(text, file.Text!.TrimEnd());
            if (file.TextCut) section.AddParagraph($"Cut short here: the first {MaxTextCharacters:N0} characters are shown. The whole file is kept with the ticket in EduHelpdesk.", "Muted").Format.SpaceBefore = Unit.FromPoint(4);
        }
    }

    // Keeps the writer's own line breaks.
    private static void AddLines(Paragraph paragraph, string text)
    {
        var first = true;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (!first) paragraph.AddLineBreak();
            paragraph.AddText(line.Replace("\t", "    "));
            first = false;
        }
    }

    // ---- Pictures, drawn directly (PDFsharp) -----------------------------------------------------------------------

    private const double Margin = 56.69; // 2 cm in points

    private static void DrawPicture(PdfDocument pdf, TicketReportInput input, FileEntry file, XImage image, int total)
    {
        var page = pdf.AddPage();
        page.Size = PageSize.A4;
        var number = pdf.PageCount;
        using var gfx = XGraphics.FromPdfPage(page);
        var width = page.Width.Point - 2 * Margin;
        var top = Write(gfx, $"Attachment {file.Number} · {file.Attachment.FileName}", Font(9, true), new XSolidBrush(ToX(Grey)), Margin, width) + 10;
        var box = new XRect(Margin, top, width, page.Height.Point - Margin - top - 10);
        var ratio = image.PixelHeight == 0 ? 1 : (double)image.PixelWidth / image.PixelHeight;
        // Never blown up past its own size, so a small screenshot stays sharp.
        var natural = image.PointWidth > 0 ? image.PointWidth : box.Width;
        var drawWidth = Math.Min(Math.Min(box.Width, box.Height * ratio), natural);
        var drawHeight = drawWidth / ratio;
        gfx.DrawImage(image, box.X + (box.Width - drawWidth) / 2, box.Y, drawWidth, drawHeight);
        gfx.DrawString($"{FooterText(input)} · Page {number} of {total}", Font(8), new XSolidBrush(ToX(Grey)),
            new XRect(Margin, page.Height.Point - Unit.FromCentimeter(1).Point - 14.5, width, 10), XStringFormats.TopLeft);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------

    private static string FooterText(TicketReportInput input)
    {
        var title = input.Ticket.Title.Length > 60 ? input.Ticket.Title[..57].TrimEnd() + "…" : input.Ticket.Title;
        return $"Ticket #{input.Ticket.Number} · {title}" + (input.IncludeInternal ? " · Staff copy" : "");
    }

    private static string Moment(DateTime value) => value.ToString("d MMM yyyy, HH:mm", Uk);
    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.0} MB"
    };
}
