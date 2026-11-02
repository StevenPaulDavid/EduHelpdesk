using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// What EduNotify calls when a teacher uses an on-call template that raises a ticket ("Call technician"): Settings → EduNotify
// → Technician calls (HelpdeskStore.EduNotifyLink), specced in the EduNotify project's docs/TECH-CALL.md. Every call needs the
// incoming key as "Authorization: Bearer ehk_…"; anything else gets 401 and no hint of what the helpdesk holds. Answers are
// JSON, with a plain-English "error" when refused, as EduNotify's own API does.
//
//   GET  /api/v1/status                    { acceptingCalls }
//   POST /api/v1/tickets                   201 { number: "IT-123", id } (200 for a call already raised; 503 when not accepting)
//   POST /api/v1/tickets/{ref}/assign      200 { assignedTo } or { ignored: true }; 404 not a call ticket; 409 no such technician
public static class EduNotifyCallEndpoints
{
    public static IEndpointRouteBuilder MapEduNotifyCallEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AllowAnonymous().AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var header = http.Request.Headers.Authorization.ToString();
            var key = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : "";
            if (!http.RequestServices.GetRequiredService<HelpdeskStore>().CheckEduNotifyIncomingKey(key))
                return Results.Json(new { error = "That key isn't EduHelpdesk's incoming key from Settings → EduNotify." }, statusCode: StatusCodes.Status401Unauthorized);
            // Whatever the call changes is put down to EduNotify in the ticket's history.
            http.Items[HelpdeskStore.ActorItem] = new Actor(null, "EduNotify");
            return await next(context);
        });

        api.MapGet("/status", (HelpdeskStore store) => Results.Json(new { acceptingCalls = store.EduNotifyAcceptsCalls }));

        api.MapPost("/tickets", (HelpdeskStore.CallTicketRequest? request, HelpdeskStore store) =>
        {
            if (request is null) return Results.Json(new { error = "Send the call as JSON." }, statusCode: StatusCodes.Status400BadRequest);
            var result = store.RaiseCallTicket(request);
            return result.Number is { } id
                ? Results.Json(new { number = result.Ref, id }, statusCode: result.Status)
                : Results.Json(new { error = result.Error }, statusCode: result.Status);
        });

        api.MapPost("/tickets/{reference}/assign", (string reference, AssignRequest? request, HelpdeskStore store) =>
        {
            var result = store.AssignCallTicket(reference, request?.Email);
            return result.Status != StatusCodes.Status200OK
                ? Results.Json(new { error = result.Error }, statusCode: result.Status)
                : result.Ignored ? Results.Json(new { ignored = true }) : Results.Json(new { assignedTo = result.AssignedTo });
        });

        return app;
    }

    public sealed record AssignRequest(string? Email);
}
