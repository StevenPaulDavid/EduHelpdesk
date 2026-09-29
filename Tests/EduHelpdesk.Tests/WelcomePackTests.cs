using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EduHelpdesk.Tests;

// The welcome pack: its documents in Settings, which templates and onboardings include them, and the PDF itself
// (Services/HelpdeskStore.WelcomePack.cs, Services/WelcomePackPdf.cs).
public class WelcomePackTests
{
    private static MemoryStream Pdf(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++) document.AddPage();
        var stream = new MemoryStream();
        document.Save(stream, false);
        stream.Position = 0;
        return stream;
    }

    private static OnboardingDocument Upload(TestStore test, string name, int pages)
    {
        using var pdf = Pdf(pages);
        var (ok, message) = test.Store.AddOnboardingDocument(name, name + ".pdf", pdf, pdf.Length);
        Assert.True(ok, message);
        return test.Store.OnboardingDocuments.Single(x => x.Name == name);
    }

    [Fact]
    public void Only_readable_pdfs_are_accepted()
    {
        using var test = new TestStore();
        var document = Upload(test, "Acceptable use policy", 2);
        Assert.Equal(2, document.Pages);

        using var notPdf = new MemoryStream("just some text"u8.ToArray());
        Assert.False(test.Store.AddOnboardingDocument(null, "notes.txt", notPdf, notPdf.Length).Ok);
        notPdf.Position = 0;
        // Named .pdf, but it isn't one.
        Assert.False(test.Store.AddOnboardingDocument(null, "fake.pdf", notPdf, notPdf.Length).Ok);
        Assert.Single(test.Store.OnboardingDocuments);
        Assert.Single(test.Reopen().OnboardingDocuments);
    }

    [Fact]
    public void Templates_choose_documents_and_a_new_onboarding_copies_the_choice()
    {
        using var test = new TestStore();
        var policy = Upload(test, "Acceptable use policy", 2);
        var handbook = Upload(test, "Staff IT handbook", 3);
        var teacher = test.Store.OnboardingTemplates.Single(x => x.Name == "Teacher");
        Assert.True(test.Store.SetOnboardingTemplateDocuments(teacher.Id, [handbook.Id, policy.Id]).Ok);
        // Kept in the order the documents are listed in Settings, not the order ticked.
        Assert.Equal([policy.Id, handbook.Id], test.Reopen().OnboardingTemplates.Single(x => x.Id == teacher.Id).DocumentIds);

        var (ok, _, number) = test.Store.StartOnboarding(new HelpdeskStore.OnboardingDetails("Sam Price", null, "Teacher", null, null, DateOnly.FromDateTime(DateTime.Today).AddDays(7), null), teacher.Id, null);
        Assert.True(ok);
        Assert.Equal([policy.Id, handbook.Id], test.Store.FindOnboarding(number)!.PackDocumentIds);
        Assert.True(test.Store.SetOnboardingPackDocuments(number, [handbook.Id]).Ok);
        Assert.Equal([handbook.Id], test.Reopen().FindOnboarding(number)!.PackDocumentIds);

        // Deleting a document takes it out of every template and pack.
        Assert.True(test.Store.DeleteOnboardingDocument(handbook.Id).Ok);
        Assert.Empty(test.Store.FindOnboarding(number)!.PackDocumentIds);
        Assert.Equal([policy.Id], test.Store.OnboardingTemplates.Single(x => x.Id == teacher.Id).DocumentIds);
        Assert.Null(test.Store.FindOnboardingDocument(handbook.Id));
    }

    [Fact]
    public void The_pack_is_a_cover_then_the_chosen_documents()
    {
        using var test = new TestStore();
        PdfFonts.Install(test.Root);
        var policy = Upload(test, "Acceptable use policy", 2);
        var handbook = Upload(test, "Staff IT handbook", 3);
        test.Store.SetOnboardingItInfo("Wi-Fi: Staff network.\n\nPrinting: tap your badge at any printer.");
        var (_, _, number) = test.Store.StartOnboarding(new HelpdeskStore.OnboardingDetails("Sam Price", "sam.price@school.example", "Teacher", null, null, DateOnly.FromDateTime(DateTime.Today).AddDays(7), null), null, null);
        test.Store.SetOnboardingPackDocuments(number, [policy.Id, handbook.Id]);
        var record = test.Store.FindOnboarding(number)!;
        test.Store.AddAsset(new AssetRecord(Guid.NewGuid(), "LT-PACK1", "Dell", "Latitude 5440", "Laptop", "SN-PACK1", "", record.StarterId));

        var input = test.Store.WelcomePackFor(number, "Otter-Lantern-Maple-38", "http://helpdesk/")!;
        Assert.Single(input.Equipment);
        Assert.Equal(2, input.Documents.Count);

        var bytes = WelcomePackPdf.Build(input);
        using var pack = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        // The cover (one page or more) and then exactly the five pages of the two documents.
        Assert.True(pack.PageCount >= 6, $"{pack.PageCount} pages");
        Assert.Equal("Welcome pack - Sam Price.pdf", WelcomePackPdf.FileName(input));
    }
}
