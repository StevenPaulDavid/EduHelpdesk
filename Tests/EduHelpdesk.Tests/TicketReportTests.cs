using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EduHelpdesk.Tests;

// The ticket report PDF (Services/HelpdeskStore.TicketReport.cs, Services/TicketReportPdf.cs): the staff copy has
// everything, the requester copy leaves out internal notes and files kept for the IT team, and attachments that can go
// into a PDF are merged in after the report.
public class TicketReportTests
{
    // A 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static byte[] Pdf(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++) document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static void Attach(TestStore test, int number, string name, byte[] content, bool shared = false)
    {
        using var stream = new MemoryStream(content);
        Assert.Null(test.Store.AddTicketAttachment(number, name, stream, stream.Length, visibleToRequester: shared));
    }

    private static (TestStore Test, int Number) Ticket()
    {
        var test = new TestStore();
        PdfFonts.Install(test.Root);
        var number = test.AddTicket(test.AddRequester("Priya Shah").Id);
        test.Store.AddTicketComment(number, "The projector bulb needs replacing.");
        test.Store.AddTicketComment(number, "Bulb is £120 - check the budget code.", isInternal: true);
        Attach(test, number, "quote.pdf", Pdf(2));
        Attach(test, number, "photo.png", Png, shared: true);
        Attach(test, number, "log.txt", "Lamp hours exceeded"u8.ToArray(), shared: true);
        Attach(test, number, "notes.docx", "PK not really a document"u8.ToArray());
        return (test, number);
    }

    [Fact]
    public void The_staff_copy_has_everything_with_pdfs_and_pictures_merged_in()
    {
        var (test, number) = Ticket();
        using var _ = test;
        var input = test.Store.TicketReportFor(number, includeInternal: true, "Tess Tech")!;
        Assert.Equal(4, input.Attachments.Count);
        Assert.Equal(0, input.AttachmentsLeftOut);
        Assert.Equal(4, input.FilePaths.Count);

        var bytes = TicketReportPdf.Build(input);
        using var report = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        // The written report (a page or more), the quote's two pages and the picture's one.
        Assert.True(report.PageCount >= 4, $"{report.PageCount} pages");
        var without = TicketReportPdf.Build(test.Store.TicketReportFor(number, includeInternal: true, null)! with { Attachments = [], FilePaths = new Dictionary<Guid, string>() });
        using var bare = PdfReader.Open(new MemoryStream(without), PdfDocumentOpenMode.Import);
        Assert.Equal(bare.PageCount + 3, report.PageCount);
        Assert.Equal($"Ticket {number} report.pdf", TicketReportPdf.FileName(input.Ticket));
    }

    [Fact]
    public void The_requester_copy_leaves_out_internal_notes_and_files_kept_for_the_it_team()
    {
        var (test, number) = Ticket();
        using var _ = test;
        var input = test.Store.TicketReportFor(number, includeInternal: false, null)!;
        Assert.False(input.IncludeInternal);
        Assert.Equal(["photo.png", "log.txt"], input.Attachments.Select(x => x.FileName).Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(2, input.AttachmentsLeftOut);
        // Built without error; the conversation it writes is filtered by IncludeInternal.
        var bytes = TicketReportPdf.Build(input);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void A_ticket_that_does_not_exist_has_no_report()
    {
        using var test = new TestStore();
        Assert.Null(test.Store.TicketReportFor(999_999, true, null));
    }
}
