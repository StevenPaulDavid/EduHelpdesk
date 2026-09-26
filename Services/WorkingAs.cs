using System.Security.Claims;

namespace EduHelpdesk.Services;

// Whose tickets "mine" means - on the ticket list's My tickets queue and on the overview. Everyone is themselves,
// except that a role with the Working as flag can pick another technician on the ticket list, remembered in this
// browser's cookie (JobsModel.OnPostWhoAmI).
public static class WorkingAs
{
    public const string Cookie = "helpdesk_me";

    public static Guid? SignedInTechnicianId(HelpdeskStore store, ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && store.Technicians.Any(x => x.Id == id) ? id : null;

    public static Guid? Resolve(HelpdeskStore store, ClaimsPrincipal user, HttpRequest request) =>
        store.UserHasFlag(user, Modules.Flags.WorkingAs) && Guid.TryParse(request.Cookies[Cookie], out var me) && store.Technicians.Any(x => x.Id == me)
            ? me
            : SignedInTechnicianId(store, user);
}
