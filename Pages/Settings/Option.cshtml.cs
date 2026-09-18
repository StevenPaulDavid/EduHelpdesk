using EduHelpdesk.Services; using Microsoft.AspNetCore.Mvc; using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class OptionModel(HelpdeskStore store):PageModel
{
 public string Kind {get;set;}=""; public IReadOnlyList<string> Values=>Kind switch{"Teams" or "Team"=>store.TechnicianTeams,"Departments" or "Department"=>store.Departments,"Locations" or "Location"=>store.Locations,"AssetTypes" or "Asset type"=>store.AssetTypes,"AssetMakes" or "Asset make"=>store.AssetMakes,"AssetModels" or "Asset model"=>store.AssetModels,"Categories" or "Category"=>store.Categories,"Statuses" or "Status"=>store.Statuses,"Priorities" or "Priority"=>store.Priorities,_=>[]};
 public void OnGet(string kind){Kind=kind;}
 public IActionResult OnPost(string kind,string value,string? currentValue){Kind=kind; if(string.IsNullOrWhiteSpace(value)){ModelState.AddModelError("","A value is required.");return Page();} var ticketKind = NormalizeTicketKind(kind); TempData["Message"] = ticketKind is null ? (currentValue is null ? store.AddManagedOption(kind,value) : store.UpdateManagedOption(kind,currentValue,value)) : (currentValue is null ? store.AddTicketOption(ticketKind,value) : store.UpdateTicketOption(ticketKind,currentValue,value)); return RedirectToPage(new{kind});}
 public IActionResult OnPostDelete(string kind,string value){var ticketKind = NormalizeTicketKind(kind); TempData["Message"] = ticketKind is null ? store.DeleteManagedOption(kind,value) : store.DeleteTicketOption(ticketKind,value); return RedirectToPage(new{kind});}
 private static string? NormalizeTicketKind(string kind) => kind switch { "Categories" => "Category", "Statuses" => "Status", "Priorities" => "Priority", _ => null };
}
