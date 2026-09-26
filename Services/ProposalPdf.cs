using System.Globalization;
using EduHelpdesk.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EduHelpdesk.Services;

// A project's proposal as one PDF, in the order the reader needs it:
//   front page      - what, for whom, who prepared it, the headline totals and the spending band;
//   contents        - with page numbers, the quote documents included;
//   1 Summary       - what was asked for, the chosen quote for every item (£0.00 where there isn't one), the band;
//   2 Items         - every received quote compared, item by item, with the chosen one marked and its payment lines;
//   3 Quote documents, then the documents themselves - each quote behind its own cover page, PDFs page by page and
//                     pictures one to a page. Word, Excel and anything else that can't go into a PDF is listed instead.
// MigraDoc lays out the written part. The quote documents are added after it with PDFsharp, so their page numbers are
// known once the written part has been laid out once; it is then laid out again with the real numbers in place.
public static class ProposalPdf
{
    private const string FontName = "Body"; // PdfFonts answers every family with the one font it found
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");
    private static readonly Color Ink = new(0x1F, 0x2A, 0x2E);
    private static readonly Color Grey = new(0x5D, 0x6B, 0x70);
    private static readonly Color Rule = new(0xC9, 0xD3, 0xD5);
    private static readonly Color Warning = new(0xA3, 0x3B, 0x20);
    private static readonly Color DraftShade = new(0xFD, 0xF3, 0xDC);
    private const double ContentWidthCm = 17;

    public static byte[] Build(ProposalInput input)
    {
        if (!PdfFonts.IsAvailable)
            throw new ProposalException("There is no font on this server to write the PDF with. Put a TrueType font family in the app's Fonts folder as regular.ttf, bold.ttf, italic.ttf and bolditalic.ttf.");
        var appendices = new List<Appendix>();
        var opened = new List<IDisposable>();
        try
        {
            // Every quote file is opened first: how many pages each takes decides the page numbers in the contents.
            foreach (var item in input.Project.Items)
                foreach (var quote in Received(item).Where(x => x.Documents.Count > 0))
                {
                    var appendix = new Appendix(Letter(appendices.Count), item, quote, SupplierName(input, quote.SupplierId), item.ChosenSupplierId == quote.SupplierId);
                    foreach (var document in quote.Documents) Load(input, appendix, document, opened);
                    appendices.Add(appendix);
                }

            var generated = Render(Compose(input, appendices, 0, 0)).PageCount;
            var pdf = Render(Compose(input, appendices, generated + 1, generated + appendices.Sum(x => x.PageCount)));
            if (pdf.PageCount != generated)
            {
                // The numbers changed a line break somewhere - lay it out once more with the corrected count.
                generated = pdf.PageCount;
                pdf = Render(Compose(input, appendices, generated + 1, generated + appendices.Sum(x => x.PageCount)));
            }
            var total = pdf.PageCount + appendices.Sum(x => x.PageCount);
            var brand = ToX(ParseColor(input.PrimaryColor));
            foreach (var appendix in appendices)
            {
                DrawCover(pdf, input, appendix, total, brand);
                foreach (var part in appendix.Parts)
                {
                    if (part.Pdf is { } source)
                        foreach (var page in source.Pages) pdf.AddPage(page);
                    else if (part.Image is { } image)
                        DrawPicture(pdf, input, appendix, part, image, total);
                }
            }
            pdf.Info.Title = $"{input.Project.Reference} {input.Project.Title} - proposal";
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

    public static string FileName(ProjectRecord project) => $"{project.Reference} proposal.pdf";

    // ---- The quote files -------------------------------------------------------------------------------------------

    private sealed class Appendix(string letter, ProjectItem item, ItemSupplier quote, string supplier, bool chosen)
    {
        public string Letter { get; } = letter;
        public ProjectItem Item { get; } = item;
        public ItemSupplier Quote { get; } = quote;
        public string Supplier { get; } = supplier;
        public bool Chosen { get; } = chosen;
        public List<Part> Parts { get; } = [];
        public List<(QuoteDocument Document, string Reason)> Left { get; } = [];
        public int PageCount => 1 + Parts.Sum(x => x.Pages);
        // Where it starts in the finished PDF: set while the contents are composed.
        public int StartPage { get; set; }
    }

    private sealed record Part(QuoteDocument Document, PdfDocument? Pdf, XImage? Image)
    {
        public int Pages => Pdf?.PageCount ?? 1;
    }

    private static void Load(ProposalInput input, Appendix appendix, QuoteDocument document, List<IDisposable> opened)
    {
        if (!input.FilePaths.TryGetValue(document.Id, out var path))
        {
            appendix.Left.Add((document, "The file is missing from the server."));
            return;
        }
        switch (document.ContentType)
        {
            case "application/pdf":
                try
                {
                    var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                    opened.Add(source);
                    if (source.PageCount == 0) appendix.Left.Add((document, "The PDF has no pages."));
                    else appendix.Parts.Add(new Part(document, source, null));
                }
                catch (Exception)
                {
                    appendix.Left.Add((document, "This PDF couldn't be read to merge it - it may be password-protected or damaged." + AskFor(input)));
                }
                break;
            case "image/png" or "image/jpeg" or "image/bmp" or "image/gif":
                try
                {
                    // Held in memory until the PDF is saved: PDFsharp reads the picture when it writes it out.
                    var stream = new MemoryStream(File.ReadAllBytes(path));
                    opened.Add(stream);
                    var image = XImage.FromStream(stream);
                    opened.Add(image);
                    appendix.Parts.Add(new Part(document, null, image));
                }
                catch (Exception)
                {
                    appendix.Left.Add((document, "This picture couldn't be placed in the PDF." + AskFor(input)));
                }
                break;
            default:
                appendix.Left.Add((document, $"{Kind(document)} can't be merged into a PDF." + AskFor(input)));
                break;
        }
    }

    private static string AskFor(ProposalInput input) =>
        $" It is kept with the project in EduHelpdesk{(input.TechnicianName is { } tech ? $" - ask {tech} for a copy" : "")}.";

    private static string Kind(QuoteDocument document) => document.ContentType switch
    {
        "application/msword" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/vnd.oasis.opendocument.text" or "application/rtf" => "A Word document",
        "application/vnd.ms-excel" or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or "application/vnd.oasis.opendocument.spreadsheet" or "text/csv" => "A spreadsheet",
        "application/vnd.ms-powerpoint" or "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "A presentation",
        "image/webp" or "image/heic" => "This kind of picture",
        "application/vnd.ms-outlook" or "message/rfc822" => "An email",
        "text/plain" => "A text file",
        _ => "This kind of file"
    };

    // A, B … Z, AA, AB …
    private static string Letter(int index) => index < 26 ? ((char)('A' + index)).ToString() : Letter(index / 26 - 1) + (char)('A' + index % 26);

    // The quotes worth comparing: every supplier who has sent one, the chosen quote first.
    private static IEnumerable<ItemSupplier> Received(ProjectItem item) =>
        item.Suppliers.Where(x => x.HasQuote).OrderByDescending(x => x.SupplierId == item.ChosenSupplierId);

    // ---- The written part (MigraDoc) -------------------------------------------------------------------------------

    private static PdfDocument Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        return renderer.PdfDocument;
    }

    private static Document Compose(ProposalInput input, List<Appendix> appendices, int firstAppendixPage, int totalPages)
    {
        var project = input.Project;
        var brand = ParseColor(input.PrimaryColor);
        var document = new Document();
        document.Info.Title = $"{project.Reference} {project.Title} - proposal";
        document.Info.Author = input.OrganisationName;
        DefineStyles(document, brand);

        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.PageWidth = Unit.FromMillimeter(210);
        setup.PageHeight = Unit.FromMillimeter(297);
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = Unit.FromCentimeter(2);
        setup.FooterDistance = Unit.FromCentimeter(1);
        setup.DifferentFirstPageHeaderFooter = true;
        var footer = section.Footers.Primary.AddParagraph();
        footer.Style = "Footer";
        footer.AddText(FooterText(input) + " · Page ");
        footer.AddPageField();
        if (totalPages > 0) footer.AddText($" of {totalPages}");

        var page = firstAppendixPage;
        foreach (var appendix in appendices)
        {
            appendix.StartPage = page;
            page += appendix.PageCount;
        }

        FrontPage(section, input, brand);
        section.AddPageBreak();
        Contents(section, input, appendices, firstAppendixPage > 0);
        section.AddPageBreak();
        Summary(section, input, brand);
        section.AddPageBreak();
        Items(section, input, brand);
        section.AddPageBreak();
        Documents(section, input, appendices, firstAppendixPage > 0);
        return document;
    }

    private static void DefineStyles(Document document, Color brand)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontName;
        normal.Font.Size = 10;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var h1 = document.Styles[StyleNames.Heading1]!;
        h1.Font.Size = 17;
        h1.Font.Bold = true;
        h1.Font.Color = brand;
        h1.ParagraphFormat.SpaceAfter = Unit.FromPoint(10);
        h1.ParagraphFormat.KeepWithNext = true;

        var h2 = document.Styles[StyleNames.Heading2]!;
        h2.Font.Size = 12.5;
        h2.Font.Bold = true;
        h2.Font.Color = Ink;
        h2.ParagraphFormat.SpaceBefore = Unit.FromPoint(14);
        h2.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        h2.ParagraphFormat.KeepWithNext = true;

        Add(document, "Cover", x => { x.Font.Size = 28; x.Font.Bold = true; x.Font.Color = brand; x.ParagraphFormat.SpaceBefore = Unit.FromCentimeter(0.3); });
        Add(document, "CoverTitle", x => { x.Font.Size = 18; x.Font.Bold = true; x.ParagraphFormat.SpaceBefore = Unit.FromPoint(6); });
        Add(document, "Eyebrow", x => { x.Font.Size = 8.5; x.Font.Bold = true; x.Font.Color = brand; });
        Add(document, "Muted", x => { x.Font.Size = 9.5; x.Font.Color = Grey; });
        Add(document, "Small", x => { x.Font.Size = 8.5; x.Font.Color = Grey; x.ParagraphFormat.SpaceAfter = 0; });
        Add(document, "Footer", x => { x.Font.Size = 8; x.Font.Color = Grey; });
        Add(document, "Toc1", x =>
        {
            x.Font.Size = 10.5;
            x.Font.Bold = true;
            x.ParagraphFormat.SpaceBefore = Unit.FromPoint(8);
            x.ParagraphFormat.TabStops.AddTabStop(Unit.FromCentimeter(ContentWidthCm), TabAlignment.Right, TabLeader.Dots);
        });
        Add(document, "Toc2", x =>
        {
            x.ParagraphFormat.LeftIndent = Unit.FromCentimeter(0.8);
            x.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);
            x.ParagraphFormat.TabStops.AddTabStop(Unit.FromCentimeter(ContentWidthCm), TabAlignment.Right, TabLeader.Dots);
        });
    }

    private static void Add(Document document, string name, Action<Style> setup) => setup(document.Styles.AddStyle(name, StyleNames.Normal));

    private static void FrontPage(Section section, ProposalInput input, Color brand)
    {
        var project = input.Project;
        if (input.LogoPath is { } logo && CanLoadPicture(logo))
        {
            var image = section.AddImage(logo);
            image.Height = Unit.FromCentimeter(2);
            image.LockAspectRatio = true;
        }
        var eyebrow = section.AddParagraph(input.OrganisationName.ToUpperInvariant(), "Eyebrow");
        eyebrow.Format.SpaceBefore = Unit.FromCentimeter(1);
        section.AddParagraph("Purchasing proposal", "Cover");
        section.AddParagraph(project.Title, "CoverTitle");
        section.AddParagraph(project.Reference, "Muted");

        if (IsDraft(project))
        {
            var banner = Table(section, ContentWidthCm);
            var cell = banner.AddRow().Cells[0];
            banner.Rows[0].Shading.Color = DraftShade;
            var text = cell.AddParagraph();
            text.AddFormattedText("Draft. ", TextFormat.Bold);
            text.AddText($"This project is still at \"{project.Status}\", so quotes and prices may change. The proposal is final once the technician marks it ready.");
            banner.Format.SpaceBefore = Unit.FromPoint(12);
        }

        Spacer(section, 14);
        var facts = Table(section, 4.5, ContentWidthCm - 4.5);
        Fact(facts, "Requested by", input.RequesterName);
        Fact(facts, "Prepared by", input.TechnicianName ?? "Not assigned yet");
        Fact(facts, "Priority", ProjectPriorities.Label(project.EffectivePriority) + (project.Priority is null ? " (suggested)" : ""));
        Fact(facts, "Proposal needed by", Day(project.DueDate));
        Fact(facts, "Status", project.Status + (project.Outcome is { } outcome ? $" · {outcome}" : "") + (project.ClosedAt is { } closed ? $" on {Day(closed)}" : ""));
        Fact(facts, "Purchasing requirements", HelpdeskStore.DescribeRequirements(project));
        Fact(facts, "Prepared on", input.PreparedAt.ToString("d MMMM yyyy, HH:mm", Uk));

        var totals = ChosenTotals(project);
        section.AddParagraph("Cost of the chosen quotes", StyleNames.Heading2);
        var figures = Table(section, 7, 5, 5);
        var head = figures.AddRow();
        head.Format.Font.Bold = true;
        head.Borders.Bottom.Width = 0.75;
        head.Borders.Bottom.Color = Rule;
        Cells(head, "", "Excluding VAT", "Including VAT");
        Cells(figures.AddRow(), "First year", Money(totals.FirstYearExVat), Money(totals.FirstYearIncVat));
        var whole = figures.AddRow();
        whole.Format.Font.Bold = true;
        Cells(whole, "Whole contract", Money(totals.TermExVat), Money(totals.TermIncVat));
        foreach (var row in figures.Rows.Cast<Row>()) { row.Cells[1].Format.Alignment = ParagraphAlignment.Right; row.Cells[2].Format.Alignment = ParagraphAlignment.Right; row.TopPadding = Unit.FromPoint(3); row.BottomPadding = Unit.FromPoint(3); }
        var priced = project.Items.Count(x => x.Chosen is not null);
        section.AddParagraph(project.Items.Count == 0 ? "No items have been added yet."
            : priced == project.Items.Count ? $"Every item has a chosen quote ({Plural(priced, "item")})."
            : $"{priced} of {Plural(project.Items.Count, "item")} {(priced == 1 ? "has" : "have")} a chosen quote. Items without one count as £0.00.", "Muted").Format.SpaceBefore = Unit.FromPoint(6);
        BandParagraph(section, input, brand);
    }

    private static void Contents(Section section, ProposalInput input, List<Appendix> appendices, bool numbered)
    {
        section.AddParagraph("Contents", StyleNames.Heading1);
        TocEntry(section, "Toc1", "1    Summary", "summary");
        TocEntry(section, "Toc1", "2    Quotes compared, item by item", "items");
        var index = 0;
        foreach (var item in input.Project.Items)
        {
            index++;
            TocEntry(section, "Toc2", $"2.{index}    {Describe(item)}", $"item{index}");
        }
        TocEntry(section, "Toc1", "3    Quote documents", "documents");
        foreach (var appendix in appendices)
        {
            var entry = section.AddParagraph();
            entry.Style = "Toc2";
            entry.AddText($"Appendix {appendix.Letter}    {appendix.Supplier} - {Describe(appendix.Item)}{(appendix.Chosen ? " (chosen)" : "")}");
            entry.AddTab();
            if (numbered) entry.AddText(appendix.StartPage.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void TocEntry(Section section, string style, string text, string bookmark)
    {
        var entry = section.AddParagraph();
        entry.Style = style;
        var link = entry.AddHyperlink(bookmark);
        link.AddText(text);
        link.AddTab();
        link.AddPageRefField(bookmark);
    }

    private static void Summary(Section section, ProposalInput input, Color brand)
    {
        var project = input.Project;
        var heading = section.AddParagraph("1    Summary", StyleNames.Heading1);
        heading.AddBookmark("summary");

        section.AddParagraph("What was asked for", StyleNames.Heading2);
        MultiLine(section, project.ItemsWanted);
        var requirements = section.AddParagraph();
        requirements.AddFormattedText("Purchasing requirements: ", TextFormat.Bold);
        requirements.AddText(HelpdeskStore.DescribeRequirements(project));

        section.AddParagraph("The chosen quote for each item", StyleNames.Heading2);
        var table = Table(section, 5, 4, 2, 2, 2, 2);
        Header(table, "Item", "Chosen quote", "First year\nex VAT", "First year\ninc VAT", "Whole term\nex VAT", "Whole term\ninc VAT");
        var sum = new QuoteTotals();
        foreach (var item in project.Items)
        {
            var chosen = item.Chosen;
            var totals = chosen is null ? new QuoteTotals() : QuoteTotals.Of(chosen.PaymentLines);
            sum += totals;
            var row = table.AddRow();
            row.Cells[0].AddParagraph(Describe(item));
            var who = row.Cells[1].AddParagraph();
            if (chosen is null) who.AddFormattedText("None chosen yet", TextFormat.Italic).Color = Grey;
            else who.AddText(SupplierName(input, chosen.SupplierId));
            MoneyCells(row, 2, totals);
        }
        if (project.Items.Count == 0)
        {
            var row = table.AddRow();
            row.Cells[0].MergeRight = 1;
            row.Cells[0].AddParagraph("No items yet").Format.Font.Italic = true;
            MoneyCells(row, 2, new QuoteTotals());
        }
        var total = table.AddRow();
        total.Format.Font.Bold = true;
        total.Borders.Top.Width = 1;
        total.Borders.Top.Color = Ink;
        total.Cells[0].MergeRight = 1;
        total.Cells[0].AddParagraph("Total");
        MoneyCells(total, 2, sum);

        section.AddParagraph("Spending band", StyleNames.Heading2);
        BandParagraph(section, input, brand);
        if (input.Bands.Count > 0)
        {
            var bands = Table(section, 3.2, 4.4, 1.8, 7.6);
            Header(bands, "Band", "Range", "Quotes needed", "Requirements");
            foreach (var band in input.Bands)
            {
                var row = bands.AddRow();
                var current = input.Band?.Id == band.Id;
                if (current) { row.Shading.Color = Tint(brand); row.Format.Font.Bold = true; }
                Cells(row, band.Name + (current ? " (this project)" : ""), HelpdeskStore.DescribeRange(band), band.QuotesNeeded.ToString(CultureInfo.InvariantCulture), band.Requirements);
            }
            var received = section.AddParagraph();
            received.Style = "Muted";
            received.Format.SpaceBefore = Unit.FromPoint(6);
            received.AddText(project.Items.Count == 0 ? "Quotes received: no items yet."
                : "Quotes received: " + string.Join(", ", project.Items.Select(x => $"{x.Name} {x.Suppliers.Count(s => s.HasQuote)}"))
                  + (input.Band is { } projectBand ? $" - the {projectBand.Name} band asks for {projectBand.QuotesNeeded}." : "."));
        }
    }

    private static void BandParagraph(Section section, ProposalInput input, Color brand)
    {
        var basis = input.BandsIncludeVat ? "including VAT" : "excluding VAT";
        var text = section.AddParagraph();
        text.Format.SpaceBefore = Unit.FromPoint(6);
        if (input.Project.Items.All(x => x.Chosen is null))
            text.AddText("No quotes have been chosen yet, so the project can't be placed in a spending band.");
        else if (input.Band is { } band)
        {
            text.AddText($"The whole-contract total of the chosen quotes, {Money(input.BandTotal)} {basis}, falls in the ");
            text.AddFormattedText(band.Name, TextFormat.Bold).Color = brand;
            text.AddText($" band ({HelpdeskStore.DescribeRange(band)}): {Plural(band.QuotesNeeded, "quote")} needed." + (string.IsNullOrWhiteSpace(band.Requirements) ? "" : $" {Sentence(band.Requirements)}"));
        }
        else if (input.Bands.Count == 0)
            text.AddText($"The whole-contract total of the chosen quotes is {Money(input.BandTotal)} {basis}. No spending bands are set up.");
        else
            text.AddText($"The whole-contract total of the chosen quotes, {Money(input.BandTotal)} {basis}, doesn't fall in any spending band.");
    }

    private static void Items(Section section, ProposalInput input, Color brand)
    {
        var project = input.Project;
        var heading = section.AddParagraph("2    Quotes compared, item by item", StyleNames.Heading1);
        heading.AddBookmark("items");
        section.AddParagraph("Every quote received is shown, with the chosen one first and shaded. First-year figures count a one-off payment whole plus up to a year of any recurring payments; whole-term figures count every payment.", "Muted");
        if (project.Items.Count == 0) section.AddParagraph("No items have been added yet.");
        var index = 0;
        foreach (var item in project.Items)
        {
            index++;
            var title = section.AddParagraph($"2.{index}    {Describe(item)}", StyleNames.Heading2);
            title.AddBookmark($"item{index}");
            if (item.SubItems.Count > 0)
            {
                var includes = section.AddParagraph();
                includes.AddFormattedText("Includes: ", TextFormat.Bold);
                includes.AddText(string.Join(", ", item.SubItems.Select(x => $"{x.Quantity} × {x.Name}")));
            }

            var table = Table(section, 4.4, 3.4, 2.3, 2.3, 2.3, 2.3);
            Header(table, "Supplier", "Quote", "First year\nex VAT", "First year\ninc VAT", "Whole term\nex VAT", "Whole term\ninc VAT");
            var quotes = Received(item).ToList();
            foreach (var quote in quotes)
            {
                var chosen = item.ChosenSupplierId == quote.SupplierId;
                var row = table.AddRow();
                row.KeepWith = 1;
                row.Borders.Bottom.Visible = false;
                var name = row.Cells[0].AddParagraph();
                name.AddFormattedText(SupplierName(input, quote.SupplierId), TextFormat.Bold);
                if (chosen)
                {
                    var tag = row.Cells[0].AddParagraph();
                    tag.AddFormattedText("CHOSEN", TextFormat.Bold).Color = brand;
                }
                row.Cells[1].AddParagraph(quote.Reference.Length > 0 ? quote.Reference : "No reference");
                var valid = row.Cells[1].AddParagraph();
                valid.Style = "Small";
                if (quote.ValidUntil is { } until)
                {
                    if (quote.IsExpired(DateOnly.FromDateTime(input.PreparedAt))) valid.AddFormattedText($"Expired {Day(until)}", TextFormat.Bold).Color = Warning;
                    else valid.AddText($"Valid until {Day(until)}");
                }
                else valid.AddText("No expiry date given");
                MoneyCells(row, 2, QuoteTotals.Of(quote.PaymentLines));

                var detail = table.AddRow();
                detail.Cells[0].MergeRight = 5;
                var lines = detail.Cells[0].AddParagraph();
                lines.Style = "Small";
                var received = quote.StatusHistory.LastOrDefault(x => x.Status is QuoteStatuses.Received or QuoteStatuses.UpdateReceived);
                var facts = new List<string>();
                if (received is not null) facts.Add($"{received.Status} {Day(received.At.ToLocalTime())}");
                if (quote.Status == QuoteStatuses.UpdateRequested) facts.Add("an update has been asked for");
                facts.Add(quote.PaymentLines.Count == 0 ? "no prices entered yet, so it counts as £0.00" : "payments: " + string.Join("; ", quote.PaymentLines.Select(HelpdeskStore.DescribeLine)));
                lines.AddText(Capitalise(string.Join(" · ", facts)) + ".");
                if (chosen) { row.Shading.Color = Tint(brand); detail.Shading.Color = Tint(brand); }
            }
            if (quotes.Count == 0)
            {
                var row = table.AddRow();
                row.Cells[0].MergeRight = 1;
                row.Cells[0].AddParagraph("No quotes received yet").Format.Font.Italic = true;
                MoneyCells(row, 2, new QuoteTotals());
            }

            var others = item.Suppliers.Where(x => !x.HasQuote).ToList();
            var note = item.Suppliers.Count == 0 ? "No suppliers have been asked to quote yet."
                : others.Count > 0 ? "Also asked: " + string.Join("; ", others.Select(x => $"{SupplierName(input, x.SupplierId)} ({Awaiting(x)})")) + "."
                : null;
            if (note is not null) section.AddParagraph(note, "Muted").Format.SpaceBefore = Unit.FromPoint(4);
        }
    }

    private static string Awaiting(ItemSupplier quote) => quote.Status switch
    {
        QuoteStatuses.Requested when quote.StatusSince is { } since => $"asked on {Day(since.ToLocalTime())}, no quote yet",
        QuoteStatuses.NotRequested => "not asked yet",
        QuoteStatuses.Declined => "declined or didn't respond",
        var other => other.ToLowerInvariant()
    };

    private static void Documents(Section section, ProposalInput input, List<Appendix> appendices, bool numbered)
    {
        var heading = section.AddParagraph("3    Quote documents", StyleNames.Heading1);
        heading.AddBookmark("documents");
        if (appendices.Count == 0)
        {
            section.AddParagraph("No quote documents have been uploaded.");
            return;
        }
        section.AddParagraph("Each supplier's quote follows as an appendix, behind a cover page: PDFs page by page, pictures one to a page. Earlier versions of updated quotes are not included.", "Muted");
        var table = Table(section, 2, 9, 4.6, 1.4);
        Header(table, "Appendix", "Supplier and item", "Files", "Page");
        foreach (var appendix in appendices)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(appendix.Letter);
            var who = row.Cells[1].AddParagraph();
            who.AddFormattedText(appendix.Supplier, TextFormat.Bold);
            who.AddText($" - {Describe(appendix.Item)}");
            if (appendix.Chosen) who.AddFormattedText(" (chosen)", TextFormat.Bold);
            row.Cells[2].AddParagraph(Plural(appendix.Parts.Count, "file") + " included" + (appendix.Left.Count > 0 ? $", {appendix.Left.Count} not" : ""));
            row.Cells[3].AddParagraph(numbered ? appendix.StartPage.ToString(CultureInfo.InvariantCulture) : "");
            row.Cells[3].Format.Alignment = ParagraphAlignment.Right;
        }
        var left = appendices.SelectMany(a => a.Left.Select(x => (Appendix: a, x.Document, x.Reason))).ToList();
        if (left.Count > 0)
        {
            section.AddParagraph("Not included in this PDF", StyleNames.Heading2);
            foreach (var (appendix, document, reason) in left)
            {
                var line = section.AddParagraph();
                line.AddFormattedText(document.FileName, TextFormat.Bold);
                line.AddText($" (appendix {appendix.Letter}, {appendix.Supplier}): {reason}");
            }
        }
    }

    // ---- Pages drawn directly (PDFsharp): the appendix covers and pictures -------------------------------------------

    private const double Margin = 56.69; // 2 cm in points

    private static void DrawCover(PdfDocument pdf, ProposalInput input, Appendix appendix, int total, XColor brand)
    {
        var page = pdf.AddPage();
        page.Size = PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);
        var width = page.Width.Point - 2 * Margin;
        var ink = new XSolidBrush(ToX(Ink));
        var grey = new XSolidBrush(ToX(Grey));
        var y = Margin + 10;

        y = Write(gfx, $"APPENDIX {appendix.Letter} · QUOTE DOCUMENTS", Font(8.5, true), new XSolidBrush(brand), y, width);
        y = Write(gfx, appendix.Supplier, Font(22, true), ink, y + 8, width);
        y = Write(gfx, $"Quote for {Describe(appendix.Item)}", Font(13), ink, y + 2, width);
        if (appendix.Chosen)
        {
            var label = Font(9, true);
            var size = gfx.MeasureString("CHOSEN QUOTE", label);
            gfx.DrawRectangle(new XSolidBrush(brand), Margin, y + 10, size.Width + 16, size.Height + 8);
            gfx.DrawString("CHOSEN QUOTE", label, XBrushes.White, new XRect(Margin + 8, y + 14, size.Width, size.Height), XStringFormats.TopLeft);
            y += size.Height + 18;
        }
        y += 20;

        var quote = appendix.Quote;
        var received = quote.StatusHistory.LastOrDefault(x => x.Status is QuoteStatuses.Received or QuoteStatuses.UpdateReceived);
        var totals = QuoteTotals.Of(quote.PaymentLines);
        var facts = new List<(string Label, string Value)>
        {
            ("Supplier's reference", quote.Reference.Length > 0 ? quote.Reference : "None given"),
            ("Received", received is null ? "Not recorded" : $"{Day(received.At.ToLocalTime())}{(received.Status == QuoteStatuses.UpdateReceived ? " (updated quote)" : "")}"),
            ("Valid until", quote.ValidUntil is { } until ? Day(until) + (quote.IsExpired(DateOnly.FromDateTime(input.PreparedAt)) ? " - expired" : "") : "No expiry date given"),
            ("First year", $"{Money(totals.FirstYearExVat)} ex VAT · {Money(totals.FirstYearIncVat)} inc VAT"),
            ("Whole term", $"{Money(totals.TermExVat)} ex VAT · {Money(totals.TermIncVat)} inc VAT"),
            ("Payments", quote.PaymentLines.Count == 0 ? "No prices entered" : string.Join("\n", quote.PaymentLines.Select(HelpdeskStore.DescribeLine)))
        };
        foreach (var (label, value) in facts)
        {
            gfx.DrawString(label, Font(9.5, true), grey, new XRect(Margin, y, 130, 14), XStringFormats.TopLeft);
            y = Write(gfx, value, Font(10), ink, y, width - 140, Margin + 140) + 6;
        }

        y = Write(gfx, "In this appendix", Font(12, true), ink, y + 18, width) + 4;
        var next = appendix.StartPage + 1;
        if (appendix.Parts.Count == 0) y = Write(gfx, "Nothing - none of this quote's files could be placed in a PDF.", Font(10), grey, y, width);
        foreach (var part in appendix.Parts)
        {
            var pages = part.Pages == 1 ? $"page {next}" : $"pages {next}-{next + part.Pages - 1}";
            y = Bullet(gfx, $"{part.Document.FileName} - {pages}", ink, y, width) + 2;
            next += part.Pages;
        }
        if (appendix.Left.Count > 0)
        {
            y = Write(gfx, "Not included", Font(12, true), ink, y + 16, width) + 4;
            foreach (var (document, reason) in appendix.Left)
                y = Bullet(gfx, $"{document.FileName}: {reason}", ink, y, width) + 2;
        }
        DrawFooter(gfx, page, input, appendix.StartPage, total);
    }

    private static void DrawPicture(PdfDocument pdf, ProposalInput input, Appendix appendix, Part part, XImage image, int total)
    {
        var page = pdf.AddPage();
        page.Size = PageSize.A4;
        var number = pdf.PageCount;
        using var gfx = XGraphics.FromPdfPage(page);
        var width = page.Width.Point - 2 * Margin;
        var top = Write(gfx, $"Appendix {appendix.Letter} · {appendix.Supplier} · {part.Document.FileName}", Font(9, true), new XSolidBrush(ToX(Grey)), Margin, width) + 10;
        var box = new XRect(Margin, top, width, page.Height.Point - Margin - top - 10);
        var ratio = image.PixelHeight == 0 ? 1 : (double)image.PixelWidth / image.PixelHeight;
        var drawWidth = Math.Min(box.Width, box.Height * ratio);
        var drawHeight = drawWidth / ratio;
        gfx.DrawImage(image, box.X + (box.Width - drawWidth) / 2, box.Y, drawWidth, drawHeight);
        DrawFooter(gfx, page, input, number, total);
    }

    // Lined up with the footer MigraDoc puts on the written pages.
    private static void DrawFooter(XGraphics gfx, PdfPage page, ProposalInput input, int number, int total) =>
        gfx.DrawString($"{FooterText(input)} · Page {number} of {total}", Font(8), new XSolidBrush(ToX(Grey)),
            new XRect(Margin, page.Height.Point - Unit.FromCentimeter(1).Point - 14.5, page.Width.Point - 2 * Margin, 10), XStringFormats.TopLeft);

    // A list line whose wrapped lines hang under the text, not the bullet.
    private static double Bullet(XGraphics gfx, string text, XBrush brush, double y, double width)
    {
        gfx.DrawString("•", Font(10), brush, new XRect(Margin, y, 10, 12), XStringFormats.TopLeft);
        return Write(gfx, text, Font(10), brush, y, width - 12, Margin + 12);
    }

    private static XColor ToX(Color color) => XColor.FromArgb((int)color.R, (int)color.G, (int)color.B);

    private static XFont Font(double size, bool bold = false) => new(FontName, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    // Word-wrapped text from y down; returns the y below it. Lines break at spaces, and at "\n".
    private static double Write(XGraphics gfx, string text, XFont font, XBrush brush, double y, double width, double x = Margin)
    {
        var height = font.GetHeight();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var trial = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length > 0 && gfx.MeasureString(trial, font).Width > width)
                {
                    gfx.DrawString(line, font, brush, new XRect(x, y, width, height), XStringFormats.TopLeft);
                    y += height;
                    line = word;
                }
                else line = trial;
            }
            gfx.DrawString(line, font, brush, new XRect(x, y, width, height), XStringFormats.TopLeft);
            y += height;
        }
        return y;
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------

    private static bool IsDraft(ProjectRecord project) => project.Status is ProjectStatuses.New or ProjectStatuses.GatheringQuotes;

    private static string FooterText(ProposalInput input)
    {
        var title = input.Project.Title.Length > 60 ? input.Project.Title[..57].TrimEnd() + "…" : input.Project.Title;
        return $"{input.Project.Reference} · {title} · Proposal" + (IsDraft(input.Project) ? " (draft)" : "");
    }

    private static QuoteTotals ChosenTotals(ProjectRecord project) =>
        project.Items.Select(x => x.Chosen).OfType<ItemSupplier>().Aggregate(new QuoteTotals(), (sum, x) => sum + QuoteTotals.Of(x.PaymentLines));

    private static string SupplierName(ProposalInput input, Guid id) => input.SupplierNames.TryGetValue(id, out var name) ? name : "Unknown supplier";
    private static string Describe(ProjectItem item) => $"{item.Quantity} × {item.Name}";
    private static string Money(decimal value) => HelpdeskStore.FormatMoney(value);
    private static string Day(DateOnly value) => value.ToString("d MMM yyyy", Uk);
    private static string Day(DateTime value) => value.ToString("d MMM yyyy", Uk);
    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    private static string Sentence(string text) => text.Trim() is var trimmed && trimmed.Length > 0 && !".!?".Contains(trimmed[^1]) ? trimmed + "." : text.Trim();

    private static bool CanLoadPicture(string path)
    {
        try { using var image = XImage.FromFile(path); return image.PixelWidth > 0; }
        catch (Exception) { return false; }
    }

    // The school's colour from Settings → Branding, falling back to the default teal if it isn't a #RRGGBB value.
    private static Color ParseColor(string? value)
    {
        var hex = (value ?? "").Trim().TrimStart('#');
        return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new Color((byte)(rgb >> 16), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF))
            : new Color(0x06, 0x7A, 0x78);
    }

    // The colour mixed 88% with white, for shading the chosen quote and the project's band.
    private static Color Tint(Color color) =>
        new((byte)(color.R + (255 - color.R) * 0.88), (byte)(color.G + (255 - color.G) * 0.88), (byte)(color.B + (255 - color.B) * 0.88));

    private static Table Table(Section section, params double[] widthsCm)
    {
        var table = section.AddTable();
        table.Format.Font.Size = 8.5;
        table.Format.SpaceAfter = 0;
        table.Borders.Bottom.Width = 0.5;
        table.Borders.Bottom.Color = Rule;
        table.TopPadding = Unit.FromPoint(3);
        table.BottomPadding = Unit.FromPoint(3);
        foreach (var width in widthsCm) table.AddColumn(Unit.FromCentimeter(width));
        return table;
    }

    private static void Header(Table table, params string[] labels)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;
        row.Format.Font.Bold = true;
        row.Format.Font.Color = Grey;
        row.Borders.Bottom.Width = 1;
        row.Borders.Bottom.Color = Ink;
        for (var i = 0; i < labels.Length; i++)
        {
            var label = row.Cells[i].AddParagraph();
            var lines = labels[i].Split('\n');
            for (var line = 0; line < lines.Length; line++)
            {
                if (line > 0) label.AddLineBreak();
                label.AddText(lines[line]);
            }
            if (i >= labels.Length - 4 && labels.Length == 6) row.Cells[i].Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static void Cells(Row row, params string[] values)
    {
        for (var i = 0; i < values.Length; i++) row.Cells[i].AddParagraph(values[i]);
    }

    private static void MoneyCells(Row row, int first, QuoteTotals totals)
    {
        decimal[] values = [totals.FirstYearExVat, totals.FirstYearIncVat, totals.TermExVat, totals.TermIncVat];
        for (var i = 0; i < values.Length; i++)
        {
            row.Cells[first + i].AddParagraph(Money(values[i]));
            row.Cells[first + i].Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static void Fact(Table table, string label, string value)
    {
        var row = table.AddRow();
        row.Format.Font.Size = 10;
        row.TopPadding = Unit.FromPoint(4);
        row.BottomPadding = Unit.FromPoint(4);
        row.Cells[0].AddParagraph(label).Format.Font.Color = Grey;
        row.Cells[1].AddParagraph(value);
    }

    private static void Spacer(Section section, double points) => section.AddParagraph().Format.SpaceBefore = Unit.FromPoint(points);

    // Keeps the requester's own line breaks.
    private static void MultiLine(Section section, string text)
    {
        var paragraph = section.AddParagraph();
        var first = true;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (!first) paragraph.AddLineBreak();
            paragraph.AddText(line);
            first = false;
        }
    }
}

// A reason the proposal can't be made that the person asking can act on, as opposed to a fault.
public sealed class ProposalException(string message) : Exception(message);
