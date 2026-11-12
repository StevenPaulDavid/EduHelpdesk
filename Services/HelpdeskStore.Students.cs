using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Devices given to pupils for the long term - a 1:1 scheme's laptops - without the pupils being in People. The asset
// keeps the status "With Student" and the pupil's name (AssetRecord.StudentName), and the period goes in its ownership
// history like anyone else's. It is an allocation, not a loan: there is no due-back date, so it never shows in Loans,
// the loan report or the repeat-borrower flag (all of which read assignments that have one).
public sealed partial class HelpdeskStore
{
    public const string WithStudentStatus = "With Student";
    public const int MaxStudentNameLength = 120;

    public static bool IsWithStudent(AssetRecord asset) => asset.StudentName.Length > 0;

    // The one rule everything that saves an asset goes through (AddAssetCore, ApplyAssetUpdate): a student name only
    // stands while the status is With Student and nobody in People holds the asset. Change either and it has ended.
    private static AssetRecord WithStudentRule(AssetRecord item)
    {
        var name = (item.StudentName ?? string.Empty).Trim();
        if (item.AssignedUserId is not null || !string.Equals(item.Status, WithStudentStatus, StringComparison.OrdinalIgnoreCase)) name = string.Empty;
        return name == item.StudentName ? item : item with { StudentName = name };
    }

    // No due-back date: what keeps it out of every loan list (they read only periods with one). Reason marks it as a
    // student's, for the ownership table - a period with no UserId can also be someone since deleted from People.
    public const string StudentPeriodReason = "Student";
    private static AssetAssignment StudentPeriod(string name, DateTime at) => new(null, name, at, null, null) { Reason = StudentPeriodReason };

    public (bool Ok, string Message) GiveToStudent(Guid assetId, string? studentName)
    {
        lock (_sync)
        {
            var name = (studentName ?? string.Empty).Trim();
            if (name.Length == 0) return (false, "Enter the student's name.");
            if (name.Length > MaxStudentNameLength) return (false, $"Keep the name under {MaxStudentNameLength} characters.");
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return (false, "Asset was not found.");
            var asset = _data.Assets[index];
            if (IsDisposed(asset)) return (false, $"{asset.AssetTag} has been disposed of.");
            if (AwaitingDisposalBlock(asset, "given to a student") is { } awaiting) return (false, awaiting);
            if (KitContainingCore(assetId) is { } kit) return (false, $"{asset.AssetTag} is part of {kit.Name}. Take it out of the kit first.");
            if (HeldBlock(asset, "given to a student") is { } held) return (false, held);
            if (ResolveAssetStatus(WithStudentStatus) is not { } status)
                return (false, $"There is no \"{WithStudentStatus}\" asset status. Add it back in Settings → Lists → Asset statuses.");
            ApplyAssetUpdate(index, asset with { Status = status, StudentName = name, AssignedUserId = null, LoanDueDate = null });
            Save();
            return (true, $"{asset.AssetTag} is now with {name}.");
        }
    }

    // Back to stock, as the Return button does for someone in People.
    public (bool Ok, string Message) BackFromStudent(Guid assetId)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return (false, "Asset was not found.");
            var asset = _data.Assets[index];
            if (!IsWithStudent(asset)) return (false, $"{asset.AssetTag} isn't with a student.");
            var status = ResolveAssetStatus(InStockStatus) ?? _data.AssetStatuses.FirstOrDefault(x => !string.Equals(x, WithStudentStatus, StringComparison.OrdinalIgnoreCase)) ?? "In use";
            ApplyAssetUpdate(index, asset with { Status = status, StudentName = string.Empty });
            Save();
            return (true, $"{asset.AssetTag} is back from {asset.StudentName}.");
        }
    }
}
