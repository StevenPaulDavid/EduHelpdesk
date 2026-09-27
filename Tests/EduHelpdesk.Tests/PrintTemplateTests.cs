using Microsoft.Extensions.Logging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace EduHelpdesk.Tests;

// The uploaded Word job-sheet template, rendered for a ticket.
public class PrintTemplateTests
{
    private static TableCell Cell(string text, int span = 1)
    {
        var cell = new TableCell(new Paragraph(new Run(new Text(text))));
        if (span > 1) cell.TableCellProperties = new TableCellProperties(new GridSpan { Val = span });
        return cell;
    }

    [Fact]
    public void Tables_keep_their_rows_and_cells_and_everything_is_encoded()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Ada Quinn");
        var number = test.AddTicket(person.Id, "Screen <flickers>");
        using (var doc = WordprocessingDocument.Create(Path.Combine(test.Root, "App_Data", "print-template.docx"), WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Job {{Job.Number}}"))),
                new Table(
                    new TableRow(Cell("Details", 2)),
                    new TableRow(Cell("Requester"), Cell("{{Requester.Name}}")),
                    new TableRow(Cell("Title"), Cell("{{Job.Title}}")))));
            main.Document.Save();
        }

        var ticket = test.Store.Tickets.Single(x => x.Number == number);
        var html = test.Store.RenderPrintTemplate(ticket, person, null, []);

        Assert.Contains($"<p>Job {number}</p>", html);
        Assert.Contains("<tr><td colspan=\"2\">Details</td></tr>", html);
        Assert.Contains("<tr><td>Requester</td><td>Ada Quinn</td></tr>", html);
        Assert.Contains("<td>Screen &lt;flickers&gt;</td>", html);
    }

    [Fact]
    public void A_file_that_isnt_a_Word_document_is_refused_and_a_damaged_one_falls_back()
    {
        using var test = new TestStore();
        var (ok, message) = test.Store.SavePrintTemplate(new MemoryStream("not a docx"u8.ToArray()));
        Assert.False(ok);
        Assert.Contains("isn't a Word document", message);
        Assert.False(test.Store.HasPrintTemplate);

        // Damaged after it was stored: tickets print with the standard layout instead of failing.
        File.WriteAllText(Path.Combine(test.Root, "App_Data", "print-template.docx"), "damaged");
        var person = test.AddRequester("Ada Quinn");
        var number = test.AddTicket(person.Id);
        var ticket = test.Store.Tickets.Single(x => x.Number == number);
        Assert.Equal("", test.Store.RenderPrintTemplate(ticket, person, null, []));
    }
}

public class FileLogTests
{
    [Fact]
    public void Errors_are_written_with_their_reference_and_read_back_newest_first()
    {
        var folder = Path.Combine(Path.GetTempPath(), "eduhelpdesk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddProvider(new FileLogProvider(folder)));
            var logger = factory.CreateLogger("Test");
            logger.LogInformation("Not recorded: below warning.");
            using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "REF-123" }))
                logger.LogError(new InvalidOperationException("boom"), "First problem");
            logger.LogWarning("Second problem");

            var entries = FileLog.Read(folder);
            Assert.Equal(["Second problem", "First problem"], entries.Select(x => x.Message));
            Assert.Equal("REF-123", entries[1].Reference);
            Assert.Equal("ERROR", entries[1].Level);
            Assert.Contains("InvalidOperationException: boom", entries[1].Details);
            Assert.Equal(1, FileLog.CountSince(folder, DateTime.Now.AddMinutes(-5)));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}