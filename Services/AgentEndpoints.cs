using EduHelpdesk.AgentContract;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// What the EduHelpdesk Agent on school PCs calls (agent/EduHelpdesk.Agent; the contract is Services/AgentContract.cs and
// the rules are in HelpdeskStore.Agent). HTTPS only, like the phone app: the enrolment key and each PC's token would
// otherwise cross the school network in the clear. A refusal is { message } in words for the agent's log.
//
// 401 means "enrol again with your key" to the agent; 403 means "not now" (turned off), and it keeps its token.
public static class AgentEndpoints
{
    public const string Prefix = "/api/agent/v1";
    public const int ApiVersion = 1;
    // A report is a few KB; this is far more than any PC sends.
    private const long MaxBody = 256 * 1024;

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix).AllowAnonymous().AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!http.Request.IsHttps)
                return Refuse(StatusCodes.Status403Forbidden, "The device agent only connects over HTTPS. Turn HTTPS on for EduHelpdesk (Enable-Https.cmd in the folder it was installed from).");
            if (http.Request.ContentLength > MaxBody) return Refuse(StatusCodes.Status413PayloadTooLarge, "That report is too large.");
            // Whatever a report changes on an asset is put down to the agent in its history.
            http.Items[HelpdeskStore.ActorItem] = HelpdeskStore.AgentActor;
            return await next(context);
        });

        api.MapGet("/info", (HelpdeskStore store) => Results.Json(new
        {
            serverVersion = PhoneEndpoints.ServerVersion,
            apiVersion = ApiVersion,
            enabled = store.AgentEnabled
        }));

        api.MapPost("/enrol", (EnrolRequest? request, HttpContext http, HelpdeskStore store) =>
            Answer(store.EnrolAgent(request?.Key, request?.Report, http.Connection.RemoteIpAddress)));

        api.MapPost("/report", (AgentReport? report, HttpContext http, HelpdeskStore store) =>
        {
            var header = http.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
            return Answer(store.AgentReportIn(token, report, http.Connection.RemoteIpAddress));
        });

        return app;
    }

    private static IResult Answer(HelpdeskStore.AgentResult result) =>
        result.Answer is { } answer ? Results.Json(answer) : Refuse(result.Status, result.Error ?? "Refused.");

    private static IResult Refuse(int status, string message) => Results.Json(new { message }, statusCode: status);
}
