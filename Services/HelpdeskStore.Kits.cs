using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Loan kits and loans: kit contents, issuing and returning, and the repeat-borrowing threshold.
public sealed partial class HelpdeskStore
{
    // The kit currently holding this asset, if a kit containing it is out on loan. An asset that went out inside a kit
    // has to come back the same way: booking it in on its own would leave the kit saying it still has the equipment.
    public (LoanKit Kit, KitLoan Loan)? KitLoanHolding(Guid assetId)
    {
        lock (_sync) return KitLoanHoldingCore(assetId);
    }
    // Any kit this asset belongs to, loaned out or not. Kit equipment is only ever lent as part of its kit, so the
    // per-asset loan feature is off for these entirely - not just while the kit happens to be out.
    public LoanKit? KitContaining(Guid assetId)
    {
        lock (_sync) return KitContainingCore(assetId);
    }
    private LoanKit? KitContainingCore(Guid assetId) => _data.LoanKits.FirstOrDefault(x => x.AssetIds.Contains(assetId));
    private (LoanKit Kit, KitLoan Loan)? KitLoanHoldingCore(Guid assetId)
    {
        foreach (var kit in _data.LoanKits.Where(x => x.AssetIds.Contains(assetId)))
            if (_data.KitLoans.FirstOrDefault(x => x.KitId == kit.Id && x.ReturnedAt is null) is { } loan)
                return (kit, loan);
        return null;
    }
    // ---- Loan kits ---------------------------------------------------------
    // Kit contents are held for reference (so you can see which laptop is in a kit). Issuing a kit deliberately does
    // not touch the assets' own AssignedUserId/LoanDueDate, so kit loans and per-asset loans can't fight each other.
    public (bool Ok, string Message) AddLoanKit(string name, string? notes, IEnumerable<Guid>? assetIds)
    {
        lock (_sync)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0) return (false, "Kit name is required.");
            if (_data.LoanKits.Any(x => string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase))) return (false, "A kit with that name already exists.");
            var contents = ValidAssetIds(assetIds);
            if (KitAdditionBlock(null, contents, []) is { } blocked) return (false, blocked);
            _data.LoanKits.Add(new LoanKit(Guid.NewGuid(), value, (notes ?? string.Empty).Trim(), DateTime.UtcNow)
            {
                AssetIds = contents
            });
            Save();
            return (true, $"{value} added.");
        }
    }

    public (bool Ok, string Message) UpdateLoanKit(Guid id, string name, string? notes, IEnumerable<Guid>? assetIds, bool retired)
    {
        lock (_sync)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0) return (false, "Kit name is required.");
            var index = _data.LoanKits.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "Loan kit was not found.");
            if (OutOnLoanBlock(_data.LoanKits[index]) is { } outBlock) return (false, outBlock);
            if (_data.LoanKits.Any(x => x.Id != id && string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase))) return (false, "A kit with that name already exists.");
            var contents = ValidAssetIds(assetIds);
            // Only what is being added is checked, so an asset that joined before these rules can still be kept - or,
            // more usefully, taken out - without the save being refused over it.
            if (KitAdditionBlock(id, contents, _data.LoanKits[index].AssetIds) is { } blocked) return (false, blocked);
            _data.LoanKits[index] = _data.LoanKits[index] with
            {
                Name = value,
                Notes = (notes ?? string.Empty).Trim(),
                AssetIds = contents,
                IsRetired = retired
            };
            Save();
            return (true, $"{value} updated.");
        }
    }

    // Kits with loan history are kept, so the report doesn't lose the past. Retire them instead.
    public (bool Ok, string Message) DeleteLoanKit(Guid id)
    {
        lock (_sync)
        {
            var kit = _data.LoanKits.FirstOrDefault(x => x.Id == id);
            if (kit is null) return (false, "Loan kit was not found.");
            if (OutOnLoanBlock(kit) is { } outBlock) return (false, outBlock);
            if (_data.KitLoans.Any(x => x.KitId == id)) return (false, "That kit has loan history and cannot be deleted. Retire it instead, and it will stay out of the issue list.");
            _data.LoanKits.Remove(kit);
            Save();
            return (true, $"{kit.Name} deleted.");
        }
    }

    private List<Guid> ValidAssetIds(IEnumerable<Guid>? assetIds) =>
        (assetIds ?? []).Where(id => _data.Assets.Any(a => a.Id == id)).Distinct().ToList();

    // A kit that is out is locked for everyone, Administrator included: its record has to describe what the borrower
    // actually has, so nothing about it changes until it is booked back in.
    private string? OutOnLoanBlock(LoanKit kit) =>
        _data.KitLoans.FirstOrDefault(x => x.KitId == kit.Id && x.ReturnedAt is null) is { } loan
            ? $"{kit.Name} is out on loan with {loan.BorrowerName}. Book it back in before changing it."
            : null;

    // Why an asset cannot join a kit, or null if it can. Held assets are covered by HeldBlock; an asset also belongs to
    // one kit at most, since two kits cannot both be lending the same laptop.
    private string? KitAdditionReason(Guid? kitId, AssetRecord asset)
    {
        if (IsDisposed(asset)) return $"{asset.AssetTag} has been disposed of and cannot be put in a kit.";
        if (HeldBlock(asset, "put in a kit") is { } held) return held;
        if (_data.LoanKits.FirstOrDefault(k => k.Id != kitId && k.AssetIds.Contains(asset.Id)) is { } other)
            return $"{asset.AssetTag} is already in {other.Name}. Take it out of that kit first.";
        return null;
    }

    // The first asset being newly put in a kit that cannot go in, as a message.
    private string? KitAdditionBlock(Guid? kitId, IEnumerable<Guid> contents, IEnumerable<Guid> alreadyIn) =>
        contents.Except(alreadyIn)
            .Select(id => KitAdditionReason(kitId, _data.Assets.First(x => x.Id == id)))
            .FirstOrDefault(x => x is not null);

    // Everything that could be added to this kit (null for a new one): what the kit editor's search offers.
    public IReadOnlyList<AssetRecord> KitCandidates(Guid? kitId)
    {
        lock (_sync) return _data.Assets.Where(x => KitAdditionReason(kitId, x) is null).ToList();
    }

    // Every loan of either kind, for the Loans page and the loan report.
    // Asset assignments only qualify as loans when they have a due-back date: an assignment without one is a permanent
    // allocation (a teacher's own laptop) and has no business in a loan report. Assignments carrying a KitLoanId are
    // left out because the kit loan that created them is already in the list on its own.
    public IReadOnlyList<LoanInsights.LoanEntry> AllLoans()
    {
        lock (_sync)
        {
            var entries = _data.KitLoans
                .Where(x => _data.LoanKits.Any(k => k.Id == x.KitId))
                .Select(x => LoanInsights.From(x, _data.LoanKits.First(k => k.Id == x.KitId).Name))
                .ToList();
            foreach (var asset in _data.Assets)
                entries.AddRange(asset.Assignments
                    .Where(x => x.DueBack is not null && x.KitLoanId is null)
                    .Select(x => LoanInsights.From(x, asset.AssetTag)));
            return entries;
        }
    }

    public (bool Ok, string Message) IssueKit(Guid kitId, Guid? borrowerUserId, string? borrowerName, string? reason, DateOnly dueBack, string? issuedBy, string? notes)
    {
        lock (_sync)
        {
            var kit = _data.LoanKits.FirstOrDefault(x => x.Id == kitId);
            if (kit is null) return (false, "Loan kit was not found.");
            if (kit.IsRetired) return (false, "That kit is retired and cannot be issued.");
            if (_data.KitLoans.Any(x => x.KitId == kitId && x.ReturnedAt is null)) return (false, "That kit is already out on loan.");

            var name = (borrowerName ?? string.Empty).Trim();
            if (borrowerUserId is { } userId)
            {
                var user = _data.Users.FirstOrDefault(x => x.Id == userId);
                if (user is null) return (false, "Select a valid person.");
                name = user.Name;
            }
            else if (name.Length == 0) return (false, "Choose who is borrowing it, or type a name.");

            var chosenReason = _data.LoanReasons.FirstOrDefault(x => string.Equals(x, (reason ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosenReason is null) return (false, "Choose a reason for the loan.");

            // Issuing force-sets every member's status to "On loan", so a disposed asset left in a kit would be quietly
            // brought back from the dead. Refuse, and let someone take it out of the kit first.
            if (kit.AssetIds.Select(id => _data.Assets.FirstOrDefault(x => x.Id == id)).OfType<AssetRecord>().FirstOrDefault(IsDisposed) is { } disposed)
                return (false, $"{kit.Name} contains {disposed.AssetTag}, which has been disposed of. Take it out of the kit before issuing.");
            // The kit is not out, so any holder on its equipment is somebody's own assignment - it joined the kit before
            // that was refused, or was assigned since. Issuing used to move it onto the loan; now it has to be sorted first.
            if (kit.AssetIds.Select(id => _data.Assets.FirstOrDefault(x => x.Id == id)).OfType<AssetRecord>().FirstOrDefault(x => x.AssignedUserId is not null) is { } assigned)
                return (false, $"{kit.Name} contains {assigned.AssetTag}, which is assigned to {UserName(assigned.AssignedUserId!.Value)}. Return it or take it out of the kit before issuing.");

            var loan = new KitLoan(Guid.NewGuid(), kitId, borrowerUserId, name, chosenReason,
                DateTime.UtcNow, dueBack, null, (issuedBy ?? string.Empty).Trim(), (notes ?? string.Empty).Trim());
            _data.KitLoans.Add(loan);

            // The equipment goes out with the kit, so each asset is marked out too - otherwise the asset list still
            // shows a laptop sitting in stock that is actually in somebody's bag.
            var onLoan = ResolveAssetStatus(OnLoanStatus);
            foreach (var assetId in kit.AssetIds)
            {
                var index = _data.Assets.FindIndex(x => x.Id == assetId);
                if (index < 0) continue;
                var asset = _data.Assets[index];
                // A borrower who isn't in the directory can't be recorded as the holder, so for them the status is the
                // only marker - which is exactly why the status is set for every loan and not just named ones.
                // Stamped with the kit loan's id so the unified loan list counts the kit loan once, not once per asset.
                ApplyAssetUpdate(index, asset with
                {
                    AssignedUserId = borrowerUserId,
                    LoanDueDate = borrowerUserId.HasValue ? dueBack : null,
                    Status = onLoan ?? asset.Status
                }, chosenReason, loan.Id);
            }

            Save();
            return (true, $"{kit.Name} issued to {name}, due back {AssetInsights.Format(dueBack)}.");
        }
    }

    public (bool Ok, string Message) ReturnKit(Guid kitId, string? notes)
    {
        lock (_sync)
        {
            var result = ReturnKitCore(kitId, notes);
            if (result.Ok) Save();
            return result;
        }
    }

    // Books a kit back in without saving, so a leaver's kits and assets can all be returned in one save.
    private (bool Ok, string Message) ReturnKitCore(Guid kitId, string? notes)
    {
        var index = _data.KitLoans.FindIndex(x => x.KitId == kitId && x.ReturnedAt is null);
        if (index < 0) return (false, "That kit is not currently out on loan.");
        var loan = _data.KitLoans[index];
        var extra = (notes ?? string.Empty).Trim();
        _data.KitLoans[index] = loan with
        {
            ReturnedAt = DateTime.UtcNow,
            Notes = extra.Length == 0 ? loan.Notes : (loan.Notes.Length == 0 ? extra : $"{loan.Notes} | Returned: {extra}")
        };

        var kit = _data.LoanKits.FirstOrDefault(x => x.Id == kitId);
        var inStock = ResolveAssetStatus(InStockStatus);
        foreach (var assetId in kit?.AssetIds ?? [])
        {
            var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
            if (assetIndex < 0) continue;
            var asset = _data.Assets[assetIndex];
            // Only the status this feature set is reversed. If someone has since marked the laptop as in repair or
            // lost, that is a deliberate decision and booking the kit in should not quietly undo it.
            var status = inStock is not null && string.Equals(asset.Status, OnLoanStatus, StringComparison.OrdinalIgnoreCase) ? inStock : asset.Status;
            ApplyAssetUpdate(assetIndex, asset with { AssignedUserId = null, LoanDueDate = null, Status = status });
        }

        return (true, $"{kit?.Name ?? "Kit"} booked back in from {loan.BorrowerName}.");
    }

    public string SetLoanRepeatThreshold(int count, int days)
    {
        lock (_sync)
        {
            if (count is < 1 or > 100) return "Enter a number of loans between 1 and 100.";
            if (days is < 1 or > 3650) return "Enter a number of days between 1 and 3650.";
            _data.LoanRepeatCount = count;
            _data.LoanRepeatDays = days;
            Save();
            return "Repeat borrower threshold saved.";
        }
    }
}
