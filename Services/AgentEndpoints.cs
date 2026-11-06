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

        api.MapGet("/info", (HelpdeskStore store, IWebHostEnvironment environment) => Results.Json(new
        {
            serverVersion = PhoneEndpoints.ServerVersion,
            apiVersion = ApiVersion,
            enabled = store.AgentEnabled,
            agentVersion = AgentPackage.Latest(environment)?.VersionText
        }));

        api.MapPost("/enrol", (EnrolRequest? request, HttpContext http, HelpdeskStore store, IWebHostEnvironment environment) =>
            Answer(store.EnrolAgent(request?.Key, request?.Report, http.Connection.RemoteIpAddress), environment));

        api.MapPost("/report", (AgentReport? report, HttpContext http, HelpdeskStore store, IWebHostEnvironment environment) =>
            Answer(store.AgentReportIn(Token(http), report, http.Connection.RemoteIpAddress), environment));

        // The agent itself, for an enrolled PC updating itself. Only to a PC with a token: the exe is no secret, but
        // there's no reason to hand it to anyone on the network who asks. Staff download it from the Device agent page.
        api.MapGet("/agent", (HttpContext http, HelpdeskStore store, IWebHostEnvironment environment) =>
        {
            if (store.AgentDownloadRefusal(Token(http)) is { } refusal) return Refuse(refusal.Status, refusal.Message);
            return AgentPackage.Latest(environment) is { } package
                ? Results.File(package.Path, "application/octet-stream", AgentPackage.FileName)
                : Refuse(StatusCodes.Status404NotFound, "This helpdesk has no agent to hand out.");
        });

        // Remote support (HelpdeskStore.RemoteSupport): the PC waits here for a Quick Assist request, then says how it went.
        api.MapGet("/remote", async (HttpContext http, HelpdeskStore store, CancellationToken cancel) =>
        {
            var wait = await store.WaitForRemoteRequestAsync(Token(http), LongestRemoteWait, cancel);
            return wait.Offer is { } offer ? Results.Json(offer)
                : wait.Status == StatusCodes.Status204NoContent ? Results.NoContent()
                : Refuse(wait.Status, wait.Error ?? "Refused.");
        });
        api.MapPost("/remote/{id:guid}", (Guid id, RemoteStateReport? report, HttpContext http, HelpdeskStore store) =>
        {
            var (status, error) = store.ReportRemoteState(Token(http), id, report?.State);
            return error is null ? Results.NoContent() : Refuse(status, error);
        });

        return app;
    }

    // Under the minute a proxy or the agent's own client might give up at.
    private static readonly TimeSpan LongestRemoteWait = TimeSpan.FromSeconds(50);

    private static string? Token(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }

    // Every answer says which agent this helpdesk hands out, so a PC running an older one knows to update.
    private static IResult Answer(HelpdeskStore.AgentResult result, IWebHostEnvironment environment) =>
        result.Answer is { } answer
            ? Results.Json(AgentPackage.Latest(environment) is { } package ? answer with { AgentVersion = package.VersionText, AgentSha256 = package.Sha256 } : answer)
            : Refuse(result.Status, result.Error ?? "Refused.");

    private static IResult Refuse(int status, string message) => Results.Json(new { message }, statusCode: status);
}
