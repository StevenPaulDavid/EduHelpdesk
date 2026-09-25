using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The school's spending bands (Settings → Spending bands): reference information about how many quotes a purchase of a
// given size needs. Projects are measured against them by the whole-contract total of their chosen quotes.
public sealed partial class HelpdeskStore
{
    public const int MaxSpendingBands = 20;
    public const int MaxBandQuotes = 20;
    public const int MaxBandRequirementsLength = 1000;

    public IReadOnlyList<SpendingBand> SpendingBands { get { lock (_sync) return _data.SpendingBands.OrderBy(x => x.From).ToList(); } }
    public bool SpendingBandsIncludeVat { get { lock (_sync) return _data.SpendingBandsIncludeVat; } }

    // The amount a project is banded on: every payment over the full term, across all items, from each item's chosen
    // quote - excluding or including VAT as Settings says. Items with no chosen quote add nothing yet.
    public decimal BandTotal(ProjectRecord project)
    {
        var totals = project.Items.Select(x => x.Chosen).OfType<ItemSupplier>().Aggregate(new QuoteTotals(), (sum, x) => sum + QuoteTotals.Of(x.PaymentLines));
        return SpendingBandsIncludeVat ? totals.TermIncVat : totals.TermExVat;
    }

    public SpendingBand? BandFor(decimal amount) => SpendingBands.FirstOrDefault(x => x.Contains(amount));

    public (bool Ok, string Message) AddSpendingBand(string? name, string? from, string? upTo, int quotesNeeded, string? requirements)
    {
        lock (_sync)
        {
            if (_data.SpendingBands.Count >= MaxSpendingBands) return (false, $"There can be up to {MaxSpendingBands} bands.");
            var (band, error) = CheckBand(Guid.NewGuid(), name, from, upTo, quotesNeeded, requirements);
            if (band is null) return (false, error!);
            _data.SpendingBands.Add(band);
            Save();
            return (true, $"{band.Name} added.");
        }
    }

    public (bool Ok, string Message) UpdateSpendingBand(Guid id, string? name, string? from, string? upTo, int quotesNeeded, string? requirements)
    {
        lock (_sync)
        {
            var index = _data.SpendingBands.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "That band couldn't be found.");
            var (band, error) = CheckBand(id, name, from, upTo, quotesNeeded, requirements);
            if (band is null) return (false, error!);
            if (band == _data.SpendingBands[index]) return (true, "Nothing had changed.");
            _data.SpendingBands[index] = band;
            Save();
            return (true, $"{band.Name} saved.");
        }
    }

    public (bool Ok, string Message) DeleteSpendingBand(Guid id)
    {
        lock (_sync)
        {
            var band = _data.SpendingBands.FirstOrDefault(x => x.Id == id);
            if (band is null) return (false, "That band couldn't be found.");
            _data.SpendingBands.Remove(band);
            Save();
            return (true, $"{band.Name} deleted.");
        }
    }

    public (bool Ok, string Message) SetSpendingBandsIncludeVat(bool includeVat)
    {
        lock (_sync)
        {
            if (_data.SpendingBandsIncludeVat == includeVat) return (true, "Nothing had changed.");
            _data.SpendingBandsIncludeVat = includeVat;
            Save();
            return (true, includeVat ? "Projects are now banded on their total including VAT." : "Projects are now banded on their total excluding VAT.");
        }
    }

    // A band has to make sense on its own and must not overlap another, or one project total could fall in two bands.
    // Gaps are allowed - a school may only care about some ranges - and a project in a gap simply shows no band.
    private (SpendingBand? Band, string? Error) CheckBand(Guid id, string? name, string? from, string? upTo, int quotesNeeded, string? requirements)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0) return (null, "Give the band a name.");
        if (text.Length > 100) return (null, "Keep the band's name under 100 characters.");
        if (ParseAmount(from) is not { } low) return (null, "Enter the amount the band starts from, e.g. 1000 or 1,000.00.");
        decimal? high = null;
        if (!string.IsNullOrWhiteSpace(upTo))
        {
            if (ParseAmount(upTo) is not { } value) return (null, "Enter the amount the band goes up to, or leave it blank for no upper limit.");
            if (value <= low) return (null, "The band has to go up to more than it starts from.");
            high = value;
        }
        if (quotesNeeded is < 0 or > MaxBandQuotes) return (null, $"Enter the number of quotes needed, from 0 to {MaxBandQuotes}.");
        var notes = (requirements ?? string.Empty).Trim();
        if (notes.Length > MaxBandRequirementsLength) return (null, $"Keep the requirements under {MaxBandRequirementsLength} characters.");
        var band = new SpendingBand(id, text, low, high, quotesNeeded, notes);
        if (_data.SpendingBands.FirstOrDefault(x => x.Id != id && Overlaps(x, band)) is { } clash)
            return (null, $"That range overlaps {clash.Name} ({DescribeRange(clash)}). Adjust one so they don't share any amount.");
        return (band, null);
    }

    private static bool Overlaps(SpendingBand a, SpendingBand b) =>
        a.From <= (b.UpTo ?? decimal.MaxValue) && b.From <= (a.UpTo ?? decimal.MaxValue);

    public static string DescribeRange(SpendingBand band) =>
        band.UpTo is { } top ? $"{FormatMoney(band.From)} to {FormatMoney(top)}" : $"{FormatMoney(band.From)} and over";
}
