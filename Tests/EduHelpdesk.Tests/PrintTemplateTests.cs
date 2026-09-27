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
}
