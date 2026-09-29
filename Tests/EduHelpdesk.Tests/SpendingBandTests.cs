namespace EduHelpdesk.Tests;

// Projects are banded on the highest quote for each item, before and regardless of which quote is chosen
// (HelpdeskStore.BandBasis).
public class SpendingBandTests
{
    private static ItemSupplier Quote(decimal amount, string status = QuoteStatuses.Received, string frequency = PaymentFrequencies.OneOff, int years = 1) =>
        new(Guid.NewGuid())
        {
            StatusHistory = [new QuoteStatusChange(status, DateTime.UtcNow)],
            PaymentLines = [new PaymentLine(Guid.NewGuid(), "Price", amount, frequency, years, VatTreatments.Standard)],
        };

    private static ProjectRecord Project(params ProjectItem[] items) =>
        new(1, "Test", Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), "", DateTime.UtcNow) { Items = [.. items] };

    [Fact]
    public void The_highest_quote_for_each_item_is_added_up_whichever_is_chosen()
    {
        var cheapIpads = Quote(8_000m);
        var dearIpads = Quote(9_500m);
        var trolley = Quote(1_200m);
        var project = Project(
            new ProjectItem(Guid.NewGuid(), "iPads", 30) { Suppliers = [cheapIpads, dearIpads], ChosenSupplierId = cheapIpads.SupplierId },
            new ProjectItem(Guid.NewGuid(), "Trolley", 1) { Suppliers = [trolley] });

        var basis = HelpdeskStore.BandBasisFor(project, includeVat: false);
        Assert.Equal(10_700m, basis.Total);
        Assert.Equal(dearIpads.SupplierId, basis.Items[0].Highest!.SupplierId);
        Assert.True(basis.AllPriced);
        // Including VAT uses the same quotes, with their VAT.
        Assert.Equal(12_840m, HelpdeskStore.BandBasisFor(project, includeVat: true).Total);
    }

    [Fact]
    public void Declined_and_unpriced_quotes_are_left_out_and_recurring_payments_count_over_the_term()
    {
        var project = Project(
            new ProjectItem(Guid.NewGuid(), "Licences", 1) { Suppliers = [Quote(400m, frequency: PaymentFrequencies.Annually, years: 3), Quote(50_000m, QuoteStatuses.Declined)] },
            new ProjectItem(Guid.NewGuid(), "Cables", 10) { Suppliers = [new ItemSupplier(Guid.NewGuid()) { StatusHistory = [new QuoteStatusChange(QuoteStatuses.Requested, DateTime.UtcNow)] }] });

        var basis = HelpdeskStore.BandBasisFor(project, includeVat: false);
        Assert.Equal(1_200m, basis.Total);
        Assert.Equal(1, basis.ItemsPriced);
        Assert.True(basis.AnyPriced);
        Assert.False(basis.AllPriced);
        Assert.Null(basis.Items[1].Highest);
    }

    [Fact]
    public void A_project_with_no_prices_has_no_band_yet()
    {
        using var test = new TestStore();
        var empty = HelpdeskStore.BandBasisFor(Project(new ProjectItem(Guid.NewGuid(), "iPads", 30)), includeVat: false);
        Assert.False(empty.AnyPriced);
        // £0.00 would fall in the bottom example band, but nothing priced means no band at all.
        Assert.NotNull(test.Store.BandFor(0m));
        Assert.Null(test.Store.BandFor(empty));

        var priced = HelpdeskStore.BandBasisFor(Project(new ProjectItem(Guid.NewGuid(), "iPads", 30) { Suppliers = [Quote(6_000m)] }), includeVat: false);
        Assert.Equal(test.Store.BandFor(6_000m), test.Store.BandFor(priced));
    }
}
