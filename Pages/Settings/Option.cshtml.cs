using EduHelpdesk.Services; using Microsoft.AspNetCore.Mvc; using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class OptionModel(HelpdeskStore store):PageModel
{
 public string Kind {get;set;}=""; public IReadOnlyList<string> Values=>Kind switch{"Teams"=>store.TechnicianTeams,"Departments"=>store.Departments,"Locations"=>store.Locations,"AssetTypes"=>store.AssetTypes,"AssetMakes"=>store.AssetMakes,"AssetModels"=>store.AssetModels,"Categories"=>store.Categories,"Statuses"=>store.Statuses,"Priorities"=>store.Priorities,_=>[]};
 public void OnGet(string kind){Kind=kind;}
 public IActionResult OnPost(string kind,string value,string? currentValue){Kind=kind; if(string.IsNullOrWhiteSpace(value)){ModelState.AddModelError("","A value is required.");return Page();} TempData["Message"]=currentValue is null?store.AddManagedOption(kind,value):store.UpdateManagedOption(kind,currentValue,value); return RedirectToPage(new{kind});}
 public IActionResult OnPostDelete(string kind,string value){TempData["Message"]=store.DeleteManagedOption(kind,value);return RedirectToPage(new{kind});}
}
