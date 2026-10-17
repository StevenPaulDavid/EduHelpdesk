using System.Security.Claims;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The desk switcher at the left of the top bar: the desk, or desks, someone is working on. The ticket list, the asset
// list and report, the daily sheet and the overview open on it, and Log a ticket logs to it. It is kept against the
// account (HelpdeskStore.Preferences), so it follows them to any PC. A page asked for one desk in its address (?desk=)
// shows that desk without changing the switcher.
// "My desks" - every desk the account is on - is the default. "All desks" is offered to whoever can see more desks than
// their own, and is the default for an account on no desk. Someone who can see only one desk has nothing to switch,
// and gets no switcher.
public partial class HelpdeskStore
{
    public const string WorkingDeskPreference = "desk.working";
    public const string AllDesksChoice = "all";

    // Choices: the desks to offer, in desk order, after My desks (OffersMine) and before All desks (OffersAll). ChosenKey:
    // null for My desks, AllDesksChoice, or the chosen desk's id. Working: the desks the pages open on, empty for every
    // desk (as the ticket list's own desk filter means it).
    public sealed record DeskSwitch(IReadOnlyList<Desk> Choices, bool OffersMine, bool OffersAll, string? ChosenKey, Desk? Chosen, IReadOnlyList<Desk> Working)
    {
        public bool IsMine => ChosenKey is null;
        public bool IsAll => ChosenKey == AllDesksChoice;
        public string Label => Chosen?.Name ?? (IsAll ? "All desks" : "My desks");
    }

    // Null when there is nothing to switch between: one desk installed, or one desk this person can see.
    public DeskSwitch? SwitcherFor(ClaimsPrincipal user)
    {
        var state = DeskSwitchFor(user);
        return state.Choices.Count > 1 ? state : null;
    }

    // What the pages open on, switcher or not: someone on only the Site desk works on Site. Empty for every desk.
    public IReadOnlyList<Desk> WorkingDesks(ClaimsPrincipal user) => DeskSwitchFor(user).Working;

    // Whether the top bar shows this area (DeskAreas): when any desk being worked on uses it - all of them on All desks.
    // The role's own permission is asked separately. With one desk, always.
    public bool AreaOn(ClaimsPrincipal user, string area)
    {
        if (!HasSeveralDesks) return true;
        var working = WorkingDesks(user);
        return (working.Count == 0 ? Desks : working).Any(x => DeskUses(x, area));
    }

    // The desks Log a ticket offers: those being worked on (every desk they can see on All desks) that have a category to
    // log in. One is a plain "Log a Site ticket"; several, a menu. Empty with one desk installed.
    public IReadOnlyList<Desk> LoggingDesks(ClaimsPrincipal user)
    {
        if (!HasSeveralDesks) return [];
        var working = WorkingDesks(user);
        return (working.Count == 0 ? DesksVisibleTo(user) : working).Where(x => CategoriesFor(x.Id).Count > 0).ToList();
    }

    // Where a ticket this person logs starts: the desk asked for (Log a ticket's ?desk=), the one desk being worked on,
    // or their own first desk - whichever comes first with a category in it. Null to start on the first category.
    public Desk? StartingDeskFor(ClaimsPrincipal user, string? asked = null)
    {
        bool HasCategories(Desk desk) => CategoriesFor(desk.Id).Count > 0;
        if (FindDesk(asked) is { } desk && DesksVisibleTo(user).Any(x => x.Id == desk.Id) && HasCategories(desk)) return desk;
        if (WorkingDesks(user) is [var one] && HasCategories(one)) return one;
        return DesksOf(user).FirstOrDefault(HasCategories);
    }

    // The blank choice in a page's own desk filter (the asset list and report), which follows the switcher.
    public string WorkingDeskOption(ClaimsPrincipal user) =>
        SwitcherFor(user) is { } switcher ? $"As the top bar: {switcher.Label}"
        : WorkingDesks(user) is { Count: > 0 } working ? $"My desks ({string.Join(", ", working.Select(x => x.Name))})"
        : "My desks";

    private DeskSwitch DeskSwitchFor(ClaimsPrincipal user)
    {
        if (!HasSeveralDesks) return new DeskSwitch([], false, false, null, null, []);
        var visible = DesksVisibleTo(user);
        var mine = DesksOf(user).Where(d => visible.Any(v => v.Id == d.Id)).ToList();
        // On every desk they can see, My desks and All desks are the same thing, so only My desks is offered.
        var offersMine = mine.Count > 0;
        var offersAll = mine.Count < visible.Count;
        IReadOnlyList<Desk> all = visible.Count == Desks.Count ? [] : visible;
        var saved = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var account) ? Preference(account, WorkingDeskPreference) : null;
        // A desk that has been deleted, or that they have been taken off, falls back to the default.
        if (Guid.TryParse(saved, out var deskId) && visible.FirstOrDefault(x => x.Id == deskId) is { } desk)
            return new DeskSwitch(visible, offersMine, offersAll, desk.Id.ToString(), desk, [desk]);
        if (offersAll && (saved == AllDesksChoice || !offersMine))
            return new DeskSwitch(visible, offersMine, true, AllDesksChoice, null, all);
        return new DeskSwitch(visible, true, offersAll, null, null, mine.Count == Desks.Count ? [] : mine);
    }

    // choice: a desk's id or name, AllDesksChoice, or blank for My desks. False for a desk this person can't see.
    public bool ChooseWorkingDesk(ClaimsPrincipal user, string? choice)
    {
        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var account)) return false;
        if (string.IsNullOrWhiteSpace(choice))
        {
            if (!DeskSwitchFor(user).OffersMine) return false;
            SetPreference(account, WorkingDeskPreference, null);
            return true;
        }
        if (string.Equals(choice, AllDesksChoice, StringComparison.OrdinalIgnoreCase))
        {
            if (!DeskSwitchFor(user).OffersAll) return false;
            SetPreference(account, WorkingDeskPreference, AllDesksChoice);
            return true;
        }
        if (FindDesk(choice) is not { } desk || !DesksVisibleTo(user).Any(x => x.Id == desk.Id)) return false;
        SetPreference(account, WorkingDeskPreference, desk.Id.ToString());
        return true;
    }
}
