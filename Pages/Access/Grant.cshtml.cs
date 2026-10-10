using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Access;

// One line of the access control register: one person's access to one system or area. Without an id it records new
// access (?person= and ?system= start it filled in); with one it shows the access, its problems and its history, and
// changes, removes or deletes it. Opens at Access control: View; each action checks its own permission.
public class GrantModel(HelpdeskStore store) : PageModel
{
    public sealed class Input
    {
        public Guid? PersonId { get; set; }
        public Guid? ResourceId { get; set; }
        public string? AccessLevel { get; set; }
        public string? Identifier { get; set; }
        public bool Privileged { get; set; }
        public string? Mfa { get; set; }
        public DateOnly? GrantedOn { get; set; }
        public string? GrantedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public DateOnly? ApprovedOn { get; set; }
        public string? Notes { get; set; }
    }

    [BindProperty] public Input Form { get; set; } = new();
    public AccessGrant? Grant { get; private set; }
    public UserRecord? Person { get; private set; }
    public AccessResource? Resource { get; private set; }
    public IReadOnlyList<AccessRules.Issue> Issues { get; private set; } = [];
    public IReadOnlyList<AuditEntry> History { get; private set; } = [];
    public IReadOnlyList<UserRecord> People { get; private set; } = [];
    public IReadOnlyList<AccessResource> Resources { get; private set; } = [];
    public IReadOnlyList<string> RevokeReasons => store.RevokeReasons;
    public DateOnly Today { get; } = AssetInsights.Today;
    [TempData] public string? Message { get; set; }
    public string? Error { get; private set; }

    public bool IsNew => Grant is null;
    public bool CanAdd => store.UserCan(User, Modules.Access, ModulePermission.New);
    public bool CanEdit => store.UserCan(User, Modules.Access, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Access, ModulePermission.Delete);
    public bool CanOpenPeople => store.UserCan(User, Modules.Requesters, ModulePermission.View);
    public bool IsLive => Grant?.IsActive(Today) ?? true;

    public IActionResult OnGet(Guid? id, Guid? person, Guid? system)
    {
        if (id is { } grantId)
        {
            if (!Load(grantId)) return NotFound();
            Form = new Input
            {
                AccessLevel = Grant!.AccessLevel, Identifier = Grant.Identifier, Privileged = Grant.Privileged, Mfa = Grant.Mfa, GrantedOn = Grant.GrantedOn,
                GrantedBy = Grant.GrantedBy, ApprovedBy = Grant.ApprovedBy, ApprovedOn = Grant.ApprovedOn, Notes = Grant.Notes
            };
            return Page();
        }
        if (!CanAdd) return Forbid();
        LoadLists();
        Form = new Input { PersonId = person, ResourceId = system, GrantedOn = Today, GrantedBy = store.CurrentActor().Name, Mfa = MfaStates.NotEnabled };
        return Page();
    }

    public IActionResult OnPostAdd()
    {
        if (!CanAdd) return Forbid();
        var (ok, message, id) = store.AddAccessGrant(Record(Guid.Empty, Form.PersonId ?? Guid.Empty, Form.ResourceId ?? Guid.Empty));
        if (!ok) { Error = message; LoadLists(); return Page(); }
        Message = message;
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostSave(Guid id)
    {
        if (!CanEdit) return Forbid();
        if (!Load(id)) return NotFound();
        var (ok, message) = store.UpdateAccessGrant(Record(id, Grant!.PersonId, Grant.ResourceId) with
        {
            LastReviewedOn = Grant.LastReviewedOn, LastReviewedBy = Grant.LastReviewedBy,
            RevokedOn = Grant.RevokedOn, RevokedBy = Grant.RevokedBy, RevokeReason = Grant.RevokeReason
        });
        if (!ok) { Error = message; return Page(); }
        Message = message;
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostRevoke(Guid id, DateOnly? revokedOn, string? reason)
    {
        if (!CanEdit) return Forbid();
        Message = store.RevokeAccessGrant(id, revokedOn, reason).Message;
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostDelete(Guid id)
    {
        if (!CanDelete) return Forbid();
        var error = store.DeleteAccessGrant(id);
        if (error is not null) { Message = error; return RedirectToPage(new { id }); }
        Message = "Access record deleted.";
        return RedirectToPage("/Access/Index");
    }

    private AccessGrant Record(Guid id, Guid person, Guid resource) => new(id, person, resource, DateTime.UtcNow)
    {
        AccessLevel = Form.AccessLevel ?? "", Identifier = Form.Identifier ?? "", Privileged = Form.Privileged, Mfa = Form.Mfa ?? "",
        GrantedOn = Form.GrantedOn, GrantedBy = Form.GrantedBy ?? "", ApprovedBy = Form.ApprovedBy ?? "", ApprovedOn = Form.ApprovedOn, Notes = Form.Notes ?? ""
    };

    private void LoadLists()
    {
        People = store.Users.Where(x => x.IsActive && x.AnonymisedAt is null).OrderBy(x => x.Name, NaturalComparer.Instance).ToList();
        Resources = store.AccessResources.Where(x => !x.IsRetired).ToList();
    }

    private bool Load(Guid id)
    {
        Grant = store.FindAccessGrant(id);
        if (Grant is null) return false;
        Person = store.Users.FirstOrDefault(x => x.Id == Grant.PersonId);
        Resource = store.FindAccessResource(Grant.ResourceId);
        Issues = AccessRules.Issues(Grant, Person, Resource, Today, store.AccessReviewDays);
        var key = id.ToString();
        History = store.GetAuditEntries().Where(x => x.EntityType == "AccessGrant" && x.EntityKey == key).OrderByDescending(x => x.At).ToList();
        return true;
    }
}
