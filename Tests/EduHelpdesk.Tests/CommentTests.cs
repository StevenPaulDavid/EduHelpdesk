using System.Security.Claims;

namespace EduHelpdesk.Tests;

// Removing comments from tickets, assets and projects: only the one asked for goes, what it said goes with it, and the
// history keeps a line saying one was removed.
public class CommentTests
{
    [Fact]
    public void Removing_a_ticket_comment_takes_only_that_one_and_leaves_a_history_line_without_its_text()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Priya Shah");
        var number = test.AddTicket(person.Id);
        test.Store.AddTicketComment(number, "Keep this one.");
        test.Store.AddTicketComment(number, "The admin password is hunter2.", isInternal: true);
        var unwanted = test.Store.Tickets.Single(x => x.Number == number).Comments[1];

        var (ok, message) = test.Store.RemoveTicketComment(number, HelpdeskStore.CommentKey(unwanted.CreatedAt));
        Assert.True(ok);
        Assert.Equal("Internal note removed.", message);

        var ticket = test.Reopen().Tickets.Single(x => x.Number == number);
        Assert.Equal(["Keep this one."], ticket.Comments.Select(x => x.Text));
        var line = Assert.Single(ticket.History, x => x.Action == "Internal note removed");
        Assert.DoesNotContain("hunter2", line.Details);
        Assert.DoesNotContain(test.Store.GetAuditEntries(), x => x.Details.Contains("hunter2"));

        // Twice is harmless: the second finds nothing.
        Assert.False(test.Store.RemoveTicketComment(number, HelpdeskStore.CommentKey(unwanted.CreatedAt)).Ok);
    }

    [Fact]
    public void Asset_comments_and_project_notes_can_be_removed_too()
    {
        using var test = new TestStore();
        var asset = new AssetRecord(Guid.NewGuid(), "TST-20", "Dell", "Latitude 5440", "Laptop", "SN-20", "Room 1", null);
        test.Store.AddAsset(asset);
        test.Store.AddAssetComment(asset.Id, "Wrong laptop - meant TST-21.");
        var comment = test.Store.Assets.Single(x => x.Id == asset.Id).Comments.Single();
        Assert.True(test.Store.RemoveAssetComment(asset.Id, HelpdeskStore.CommentKey(comment.CreatedAt)).Ok);

        var person = test.AddRequester("Sam Taylor");
        test.Store.UpdateUser(person with { CanRaiseProjects = true });
        var project = test.Store.RaiseProject(person.Id, "Class set of iPads", DateOnly.FromDateTime(DateTime.Today.AddDays(30)), "30 iPads", [], null, 3);
        Assert.True(project.Ok);
        test.Store.AddProjectNote(project.Number, "Quote requested.", isInternal: false);
        var note = test.Store.FindProject(project.Number)!.Notes.Single();
        Assert.Equal("Note removed.", test.Store.RemoveProjectNote(project.Number, HelpdeskStore.CommentKey(note.CreatedAt)).Message);

        var store = test.Reopen();
        Assert.Empty(store.Assets.Single(x => x.Id == asset.Id).Comments);
        Assert.Contains(store.Assets.Single(x => x.Id == asset.Id).History, x => x.Action == "Comment removed");
        Assert.Empty(store.FindProject(project.Number)!.Notes);
        Assert.Contains(store.FindProject(project.Number)!.History, x => x.Action == "Note removed");
    }

    [Fact]
    public void Your_own_comment_needs_Edit_and_anyone_elses_needs_Delete()
    {
        using var test = new TestStore();
        var store = test.Store;
        const string role = "Junior Technician";
        Assert.True(store.RoleAllows(role, Modules.Assets, ModulePermission.Edit));
        Assert.False(store.RoleAllows(role, Modules.Assets, ModulePermission.Delete));

        var me = Guid.NewGuid();
        var junior = Principal(me, role);
        Assert.True(store.CanRemoveComment(junior, Modules.Assets, new Actor(me, "Me")));
        Assert.False(store.CanRemoveComment(junior, Modules.Assets, new Actor(Guid.NewGuid(), "Someone else")));
        // A requester's portal reply and anything from before attribution carry no account id, so they are nobody's own.
        Assert.False(store.CanRemoveComment(junior, Modules.Assets, new Actor(null, "Priya Shah")));
        Assert.False(store.CanRemoveComment(junior, Modules.Assets, null));

        var admin = Principal(Guid.NewGuid(), StaffRoles.Administrator);
        Assert.True(store.CanRemoveComment(admin, Modules.Assets, new Actor(me, "Me")));
        Assert.True(store.CanRemoveComment(admin, Modules.Assets, null));
    }

    private static ClaimsPrincipal Principal(Guid id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "Test"), new Claim(ClaimTypes.Role, role)], "Test"));
}
