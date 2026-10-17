using System.Security.Claims;

namespace EduHelpdesk.Services;

// The technician record of whoever is signed in: whose tickets "My tickets" means, on the ticket list and the overview,
// and who takes a job or logs one already done. Null for an account with no technician record.
public static class SignedInTechnician
{
    public static Guid? Id(HelpdeskStore store, ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && store.Technicians.Any(x => x.Id == id) ? id : null;
}
