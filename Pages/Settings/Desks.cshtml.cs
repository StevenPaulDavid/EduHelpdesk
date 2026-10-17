using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Desks: the parts of the school the helpdesk serves (HelpdeskStore.Desks) - IT and Facilities built in
// (HelpdeskStore.BuiltInDesks), and any the school adds. Categories, teams and statuses are placed on a desk on their
// own Settings pages; people on their technician account.
public class DesksModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<Desk> Desks => store.Desks;
    // The built-in desks that are switched off.
    public IReadOnlyList<Desk> OffDesks => store.AllDesks.Where(x => !store.IsDeskOn(x.Id)).ToList();
    public bool FacilitiesChoicePending => store.FacilitiesChoicePending;
    // The school's own desks, any of which could be the Facilities desk.
    public IReadOnlyList<Desk> OwnDesks => store.Desks.Where(x => !x.IsBuiltIn).ToList();
    public static string KindLabel(Desk desk) => desk.Kind == DeskKinds.It ? "Built in: IT" : desk.Kind == DeskKinds.Facilities ? "Built in: Facilities" : "";
    public CategoryStyle StyleFor(Desk desk) => store.StyleForDesk(desk);
    public bool FeatureOn(string feature) => store.IsFeatureOn(feature);
    public IReadOnlyList<string> CategoriesOn(Desk desk) => store.CategoriesFor(desk.Id);
    public IReadOnlyList<string> TeamsOn(Desk desk) => store.TeamsFor(desk.Id);
    public int PeopleOn(Desk desk) => store.TechniciansOnDesk(desk.Id).Count(x => x.IsActive);
    public int OpenTicketsOn(Desk desk)
    {
        var lookup = store.DeskLookup();
        return store.Tickets.Count(x => !TicketInsights.IsClosed(x) && lookup(x.Category).Id == desk.Id);
    }
    [TempData] public string? Message { get; set; }

    public IActionResult OnPostAdd(string? name, string? prefix, string? helperLine, int? firstNumber)
    {
        Message = store.AddDesk(name, prefix, helperLine, firstNumber ?? 1).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostHours(Guid id, bool useOwn, DayOfWeek[]? days, string? windows)
    {
        Message = store.SetDeskHours(id, useOwn, days, windows).Message;
        return RedirectToPage();
    }

    // Monday first, as the school week reads.
    public static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public IActionResult OnPostNextNumber(Guid id, int? next)
    {
        Message = next is { } value ? store.SetDeskNextNumber(id, value).Message : "Enter the number the next ticket should get.";
        return RedirectToPage();
    }

    public IActionResult OnPostSave(Guid id, string? name, string? prefix, string? helperLine, bool showInPortal)
    {
        Message = store.UpdateDesk(id, name, prefix, helperLine, showInPortal).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostAreas(Guid id, string[]? areas)
    {
        Message = store.SetDeskAreas(id, areas).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostStyle(Guid id, string? icon, string? color)
    {
        Message = store.SetDeskLook(id, icon, color);
        return RedirectToPage();
    }

    public IActionResult OnPostMove(Guid id, int direction)
    {
        Message = store.MoveDesk(id, direction);
        return RedirectToPage();
    }

    public IActionResult OnPostSwitchOff(Guid id)
    {
        Message = store.SwitchDeskOff(id).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostSwitchOn(Guid id)
    {
        Message = store.SwitchDeskOn(id).Message;
        return RedirectToPage();
    }

    // Asked once on an install from before Facilities was built in: make one of its desks Facilities, add one, or not now.
    public IActionResult OnPostFacilities(string? choice, Guid? id)
    {
        Message = choice switch
        {
            "make" when id is { } deskId => store.MakeFacilitiesDesk(deskId).Message,
            "add" => store.AddFacilitiesDesk().Message,
            "later" => store.DeclineFacilitiesDesk().Message,
            _ => "Choose what to do."
        };
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        Message = store.DeleteDesk(id).Message;
        return RedirectToPage();
    }
}
