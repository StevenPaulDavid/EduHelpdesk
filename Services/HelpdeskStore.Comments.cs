using System.Security.Claims;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Removing a comment from a ticket, an asset or a project (onboarding notes are ticket comments). A comment has no id of
// its own, so the pages name it by the moment it was written, which is stored to the tick.
//
// What was said goes with it - the usual reason for removing one is that it shouldn't have been written down (a
// password, something about a pupil, the wrong ticket), so copying it into the history would defeat the point. The
// history records that one was removed, whose it was and when it had been written, and who removed it.
public sealed partial class HelpdeskStore
{
    // Your own comment goes with the right to add one; anyone else's needs Delete on the module.
    public bool CanRemoveComment(ClaimsPrincipal user, string module, Actor? by) =>
        UserCan(user, module, ModulePermission.Delete)
        || (UserCan(user, module, ModulePermission.Edit) && IsAuthor(user, by));

    public static bool IsAuthor(ClaimsPrincipal user, Actor? by) =>
        by?.Id is { } author && Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id == author;

    public static long CommentKey(DateTime createdAt) => createdAt.Ticks;

    public TicketComment? FindTicketComment(int number, long key)
    {
        lock (_sync) return _data.Tickets.FirstOrDefault(x => x.Number == number)?.Comments.FirstOrDefault(x => x.CreatedAt.Ticks == key);
    }

    public AssetComment? FindAssetComment(Guid assetId, long key)
    {
        lock (_sync) return _data.Assets.FirstOrDefault(x => x.Id == assetId)?.Comments.FirstOrDefault(x => x.CreatedAt.Ticks == key);
    }

    public ProjectNote? FindProjectNote(int number, long key)
    {
        lock (_sync) return _data.Projects.FirstOrDefault(x => x.Number == number)?.Notes.FirstOrDefault(x => x.CreatedAt.Ticks == key);
    }

    public (bool Ok, string Message) RemoveTicketComment(int number, long key)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Ticket was not found.");
            var ticket = _data.Tickets[index];
            var comment = ticket.Comments.FirstOrDefault(x => x.CreatedAt.Ticks == key);
            if (comment is null) return (false, "That comment has already been removed.");
            var kind = comment.IsInternal ? "Internal note" : "Comment";
            var history = ticket.History.ToList();
            history.Add(new TicketActivity($"{kind} removed", RemovedDetails(kind, comment.By, comment.CreatedAt), DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { Comments = ticket.Comments.Where(x => !ReferenceEquals(x, comment)).ToList(), History = history };
            Save();
            return (true, $"{kind} removed.");
        }
    }

    public (bool Ok, string Message) RemoveAssetComment(Guid assetId, long key)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return (false, "Asset was not found.");
            var asset = _data.Assets[index];
            var comment = asset.Comments.FirstOrDefault(x => x.CreatedAt.Ticks == key);
            if (comment is null) return (false, "That comment has already been removed.");
            var history = asset.History.ToList();
            history.Add(new AssetActivity("Comment removed", RemovedDetails("Comment", comment.By, comment.CreatedAt), DateTime.UtcNow) { By = CurrentActor() });
            _data.Assets[index] = asset with { Comments = asset.Comments.Where(x => !ReferenceEquals(x, comment)).ToList(), History = history };
            Save();
            return (true, "Comment removed.");
        }
    }

    public (bool Ok, string Message) RemoveProjectNote(int number, long key)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var project = _data.Projects[index];
            var note = project.Notes.FirstOrDefault(x => x.CreatedAt.Ticks == key);
            if (note is null) return (false, "That note has already been removed.");
            var kind = note.IsInternal ? "Internal note" : "Note";
            _data.Projects[index] = WithHistory(project with { Notes = project.Notes.Where(x => !ReferenceEquals(x, note)).ToList() },
                $"{kind} removed", RemovedDetails(kind, note.By, note.CreatedAt));
            Save();
            return (true, $"{kind} removed.");
        }
    }

    // "A comment by Jo Bloggs from 12 Sep 2026, 14:03 was removed."
    private static string RemovedDetails(string kind, Actor? by, DateTime createdAt) =>
        $"{(kind == "Internal note" ? "An internal note" : $"A {kind.ToLowerInvariant()}")}{(by is { } actor ? $" by {actor.Name}" : "")} from {createdAt.ToLocalTime():d MMM yyyy, HH:mm} was removed.";
}
