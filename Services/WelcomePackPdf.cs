using System.Globalization;
using MigraDoc.DocumentObjectModel;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EduHelpdesk.Services;

// A new starter's welcome pack as one PDF (HelpdeskStore.WelcomePackFor gathers what goes in it):
//   cover        - welcome, signing in to the staff portal (with the temporary password while it is still to hand),
//                  getting IT help, the equipment issued to them, the school's IT information, and what else is in
//                  the pack;
//   received by  - a signature block for the equipment, for the school's records;
//   then the PDFs chosen for them in Settings, page by page, as they are.
// Laid out with MigraDoc like the proposal (ProposalPdf, whose small helpers it shares). Page numbers run through the
// whole pack, so the cover is laid out twice: once to count its pages, then with the total in the footer.
public static class WelcomePackPdf
{
    private const string FontName = "Body"; // PdfFonts answers every family with the one font it found
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");
    private static readonly Color Ink = new(0x1F, 0x2A, 0x2E);
    private static readonly Color Grey = new(0x5D, 0x6B, 0x70);
    private const double ContentWidthCm = 17;

    public static byte[] Build(WelcomePackInput input)
    {
        if (!PdfFonts.IsAvailable)
            throw new ProposalException("There is no font on this server to write the PDF with. Put a TrueType font family in the app's Fonts folder as regular.ttf, bold.ttf, italic.ttf and bolditalic.ttf.");
        var opened = new List<PdfDocument>();
        try
        {
            // Every document is opened first: they were checked when uploaded, but a file can still go missing.
            var included = new List<(string Name, PdfDocument Pdf)>();
            var left = new List<(string Name, string Reason)>();
            foreach (var (document, path) in input.Documents)
            {
                if (!File.Exists(path)) { left.Add((document.Name, "the file is missing from the server")); continue; }
                try
                {
                    var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                    opened.Add(pdf);
                    if (pdf.PageCount == 0) left.Add((document.Name, "it has no pages"));
                    else included.Add((document.Name, pdf));
                }
                catch (Exception) { left.Add((document.Name, "it couldn't be read")); }
            }

            var appended = included.Sum(x => x.Pdf.PageCount);
            var cover = ProposalPdf.Render(Compose(input, included, left, 0));
            var total = cover.PageCount + appended;
            cover = ProposalPdf.Render(Compose(input, included, left, total));
            if (cover.PageCount + appended != total) cover = ProposalPdf.Render(Compose(input, included, left, cover.PageCount + appended));
            foreach (var (_, pdf) in included)
                foreach (var page in pdf.Pages) cover.AddPage(page);

            cover.Info.Title = $"Welcome pack - {input.Starter.Name}";
            cover.Info.Author = input.OrganisationName;
            cover.Info.Creator = "EduHelpdesk";
            using var output = new MemoryStream();
            cover.Save(output, false);
            return output.ToArray();
        }
        finally
        {
            foreach (var pdf in opened) pdf.Dispose();
        }
    }

    public static string FileName(WelcomePackInput input) =>
        $"Welcome pack - {string.Concat(input.Starter.Name.Split(Path.GetInvalidFileNameChars()))}.pdf";

    private static Document Compose(WelcomePackInput input, List<(string Name, PdfDocument Pdf)> included, List<(string Name, string Reason)> left, int totalPages)
    {
        var brand = ProposalPdf.ParseColor(input.PrimaryColor);
        var starter = input.Starter;
        var record = input.Record;
        var document = new Document();
        document.Info.Title = $"Welcome pack - {starter.Name}";
        document.Info.Author = input.OrganisationName;
        Styles(document, brand);

        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.PageWidth = Unit.FromMillimeter(210);
        setup.PageHeight = Unit.FromMillimeter(297);
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = Unit.FromCentimeter(2);
        setup.FooterDistance = Unit.FromCentimeter(1);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Style = "Footer";
        footer.AddText($"{input.OrganisationName} · Welcome pack for {starter.Name} · Page ");
        footer.AddPageField();
        if (totalPages > 0) footer.AddText($" of {totalPages}");

        // Welcome.
        if (input.LogoPath is { } logo && ProposalPdf.CanLoadPicture(logo))
        {
            var image = section.AddImage(logo);
            image.Height = Unit.FromCentimeter(1.8);
            image.LockAspectRatio = true;
        }
        section.AddParagraph(input.OrganisationName.ToUpperInvariant(), "Eyebrow").Format.SpaceBefore = Unit.FromCentimeter(0.6);
        section.AddParagraph($"Welcome, {FirstName(starter.Name)}", "Cover");
        var about = string.Join(" · ", new[] { record.JobTitle, starter.Department }.Where(x => !string.IsNullOrWhiteSpace(x)));
        section.AddParagraph($"{(about.Length > 0 ? about + " · " : "")}starting {record.StartDate.ToString("dddd d MMMM yyyy", Uk)}", "Muted");
        if (input.LineManagerName is { } manager) section.AddParagraph($"Your line manager is {manager}.", "Muted");

        // Signing in.
        section.AddParagraph("Signing in to the staff portal", StyleNames.Heading2);
        section.AddParagraph("The staff portal is where you report IT problems and follow them up. It works from any computer on the school network.");
        var facts = ProposalPdf.Table(section, 4.2, ContentWidthCm - 4.2);
        ProposalPdf.Fact(facts, "Address", input.SiteAddress + "Portal");
        ProposalPdf.Fact(facts, "Email", string.IsNullOrWhiteSpace(starter.Email) ? "Your school email - IT will confirm it" : starter.Email);
        ProposalPdf.Fact(facts, input.Password is null ? "Password" : "Temporary password",
            input.Password ?? (input.HasPortalAccount ? "Given to you separately" : "Your account will be ready by your first day"));
        var note = section.AddParagraph(input.Password is not null || input.HasPortalAccount
            ? "The first time you sign in you'll be asked to choose your own password. Never share it: IT will never ask you for it."
            : "IT will give you your sign-in details when your account is ready.", "Muted");
        note.Format.SpaceBefore = Unit.FromPoint(6);

        section.AddParagraph("Getting IT help", StyleNames.Heading2);
        section.AddParagraph("Sign in to the staff portal and choose Report a problem: say what's wrong and where, and add a photo of any error message. You'll get a ticket number, and replies from IT appear under My tickets.");

        // Equipment.
        section.AddParagraph("Equipment issued to you", StyleNames.Heading2);
        if (input.Equipment.Count == 0)
            section.AddParagraph("Nothing has been issued to you yet.", "Muted");
        else
        {
            var table = ProposalPdf.Table(section, 3.2, 6.8, 4, 3);
            ProposalPdf.Header(table, "Asset tag", "Item", "Serial number", "Type");
            foreach (var asset in input.Equipment)
                ProposalPdf.Cells(table.AddRow(), asset.AssetTag, $"{asset.Make} {asset.Model}".Trim(), string.IsNullOrWhiteSpace(asset.SerialNumber) ? "-" : asset.SerialNumber, asset.Type);
            section.AddParagraph("It belongs to the school. Look after it, and hand it back to IT when you leave.", "Muted").Format.SpaceBefore = Unit.FromPoint(6);
        }

        // The school's own words.
        if (!string.IsNullOrWhiteSpace(input.ItInformation))
        {
            section.AddParagraph("IT at " + input.OrganisationName, StyleNames.Heading2);
            foreach (var paragraph in input.ItInformation.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
                ProposalPdf.MultiLine(section, paragraph.Trim());
        }

        // What else is in the pack.
        if (included.Count > 0 || left.Count > 0)
        {
            section.AddParagraph("Also in this pack", StyleNames.Heading2);
            foreach (var (name, pdf) in included)
                section.AddParagraph($"•  {name} ({pdf.PageCount} page{(pdf.PageCount == 1 ? "" : "s")})");
            foreach (var (name, reason) in left)
                section.AddParagraph($"{name} couldn't be included: {reason}. Ask IT for a copy.", "Muted");
        }

        // Signed for.
        if (input.Equipment.Count > 0)
        {
            section.AddParagraph("Received by", StyleNames.Heading2).Format.KeepWithNext = true;
            section.AddParagraph("I have received the equipment listed above in working order, and will look after it and return it when asked.").Format.KeepWithNext = true;
            var sign = ProposalPdf.Table(section, 4.2, ContentWidthCm - 4.2);
            sign.Rows.Height = Unit.FromCentimeter(1);
            ProposalPdf.Fact(sign, "Name", starter.Name);
            ProposalPdf.Fact(sign, "Signature", "");
            ProposalPdf.Fact(sign, "Date", "");
            ProposalPdf.Fact(sign, "Issued by (IT)", "");
            sign.KeepTogether = true;
        }

        section.AddParagraph($"Prepared {input.PreparedAt.ToString("d MMMM yyyy", Uk)}.", "Muted").Format.SpaceBefore = Unit.FromPoint(16);
        return document;
    }

    private static void Styles(Document document, Color brand)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontName;
        normal.Font.Size = 10;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var h2 = document.Styles[StyleNames.Heading2]!;
        h2.Font.Size = 12.5;
        h2.Font.Bold = true;
        h2.Font.Color = brand;
        h2.ParagraphFormat.SpaceBefore = Unit.FromPoint(14);
        h2.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        h2.ParagraphFormat.KeepWithNext = true;

        Add(document, "Cover", x => { x.Font.Size = 26; x.Font.Bold = true; x.Font.Color = brand; x.ParagraphFormat.SpaceBefore = Unit.FromPoint(4); });
        Add(document, "Eyebrow", x => { x.Font.Size = 8.5; x.Font.Bold = true; x.Font.Color = brand; });
        Add(document, "Muted", x => { x.Font.Size = 9.5; x.Font.Color = Grey; });
        Add(document, "Footer", x => { x.Font.Size = 8; x.Font.Color = Grey; });
    }

    private static void Add(Document document, string name, Action<Style> setup) => setup(document.Styles.AddStyle(name, StyleNames.Normal));

    private static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
}
