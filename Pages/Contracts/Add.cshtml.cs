using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Contracts;

// Adds a contract to the register. With ?project=&item= it starts filled in from the quote chosen for that item of an
// approved project (ContractRules.FromQuote), for checking and finishing before it is saved.
public class AddModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public ContractInput Input { get; set; } = new() { Status = ContractStatusDefaults.Active, CostPeriod = ContractCostPeriods.PerYear };
    public ContractForm Form => ContractForm.For(store, Input);
    public ProjectRecord? FromProject { get; private set; }
    public string? Message { get; private set; }

    public void OnGet(int? project, Guid? item)
    {
        Input.Status = store.ContractStatuses.FirstOrDefault() ?? ContractStatusDefaults.Active;
        if (project is not { } number || item is not { } itemId) return;
        var (contract, error) = store.ContractFromProject(number, itemId);
        if (contract is null) { Message = error; return; }
        Input = ContractInput.From(contract);
        FromProject = store.FindProject(number);
    }

    public IActionResult OnPost()
    {
        if (Input.ProjectNumber is { } number) FromProject = store.FindProject(number);
        var record = Input.ToRecord(Guid.NewGuid(), out var error);
        if (record is null) { Message = error; return Page(); }
        var (ok, message, id) = store.AddContract(record);
        if (!ok) { Message = message; return Page(); }
        TempData["Message"] = message;
        return RedirectToPage("/Contract", new { id });
    }
}
