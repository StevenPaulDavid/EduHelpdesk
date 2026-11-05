using System.Reflection;
using System.Security.Claims;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// What the EduHelpdesk phone app calls (docs/Phone-API.md has the whole contract). HTTPS only. Everything but /info and
// the three ways of getting a token needs a paired phone's token (PhoneAuthenticationHandler), and then asks the same
// questions the web pages do - the role's ticks and the desks the person sees - so the phone can do nothing the browser
// wouldn't let that person do. Answers are JSON; a refusal is { message } in words to show the technician.
public static class PhoneEndpoints
{
    public const string Prefix = "/api/mobile/v1";
    public const int ApiVersion = 1;
    public const string MinimumAppVersion = "1.0.0";
    // The most one list carries; a phone searches rather than scrolls through thousands.
    private const int MaxTickets = 200;
    private const int MaxPeople = 30;
    private const int InboxSize = 50;
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(25);

    public sealed record PairRequest(string? Code, string? DeviceName);
    public sealed record SignInRequest(string? Email, string? Password, string? DeviceName);
    public sealed record CodeRequest(string? PendingId, string? Code, string? DeviceName);
    public sealed record ReadRequest(string? Id, bool All);
    public sealed record StatusRequest(string? Status);
    public sealed record PriorityRequest(string? Priority);
    public sealed record CloseRequest(string? Message);

    // What this server's API can do beyond version 1's first release, so a newer app can hide what an older server lacks.
    // work-tickets: reply, set status and priority, and close (3.2).
    public static readonly string[] ApiFeatures = ["work-tickets"];

    public static IEndpointRouteBuilder MapPhoneEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix).AddEndpointFilter(async (context, next) =>
            context.HttpContext.Request.IsHttps
                ? await next(context)
                : Refuse(StatusCodes.Status403Forbidden, "The phone app only connects over HTTPS. An administrator can turn HTTPS on by running Enable-Https.cmd from the folder EduHelpdesk was installed from."));

        // ---- Before a phone has a token ----
        var open = api.MapGroup("").AllowAnonymous();
        open.MapGet("/info", (HelpdeskStore store, IWebHostEnvironment environment) => Results.Json(new
        {
            schoolName = store.Branding.BrandName,
            serverVersion = ServerVersion,
            apiVersion = ApiVersion,
            minAppVersion = MinimumAppVersion,
            // The app this server hands out (GET /app/android), for the phone's update check. Null when it has none.
            latestAppVersion = LatestApp(environment)?.Version.ToString(3),
            phoneAppEnabled = store.AllowPhoneApp,
            features = ApiFeatures
        }));
        // The APK the release zip brought (deploy/New-Release.ps1), downloaded by the app itself through its pinned
        // connection, or by a phone that doesn't have the app yet. Anonymous, as the app's own download page would be.
        open.MapGet("/app/android", (IWebHostEnvironment environment) => LatestApp(environment) is { } apk
            ? Results.File(apk.Path, "application/vnd.android.package-archive", Path.GetFileName(apk.Path))
            : Refuse(StatusCodes.Status404NotFound, "This helpdesk doesn't have the phone app to hand out. Ask whoever looks after EduHelpdesk."));
        open.MapPost("/pair", (PairRequest? request, HttpContext http, HelpdeskStore store) =>
        {
            var (token, error) = store.PairPhone(request?.Code, request?.DeviceName, http.Connection.RemoteIpAddress);
            return token is null ? Refuse(StatusCodes.Status400BadRequest, error!) : Results.Json(new { token });
        });
        open.MapPost("/sign-in", SignIn);
        open.MapPost("/sign-in/code", SignInCode);

        // ---- A paired phone ----
        var phone = api.MapGroup("").RequireAuthorization(policy => policy.AddAuthenticationSchemes(PhoneAuthenticationHandler.Scheme).RequireAuthenticatedUser());
        phone.MapPost("/sign-out", (ClaimsPrincipal user, HelpdeskStore store) =>
        {
            if (Guid.TryParse(user.FindFirst(PhoneAuthenticationHandler.PhoneClaim)?.Value, out var id)) store.RevokePhone(id, "Phone signed out");
            return Results.NoContent();
        });
        phone.MapGet("/me", WhoAmI);
        phone.MapGet("/options", Options);
        phone.MapGet("/people", People);
        phone.MapGet("/tickets", Tickets);
        phone.MapGet("/tickets/{number:int}", Ticket);
        phone.MapGet("/tickets/{number:int}/attachments/{id:guid}", Attachment);
        phone.MapPost("/tickets/{number:int}/assign-to-me", (int number, ClaimsPrincipal user, HelpdeskStore store) =>
            Take(number, user, store, (me) => store.AssignToMe(number, me)));
        phone.MapPost("/tickets/{number:int}/on-my-way", (int number, ClaimsPrincipal user, HelpdeskStore store) =>
            Take(number, user, store, (me) => store.SayOnMyWay(number, me)));
        phone.MapPost("/tickets/{number:int}/comments", Reply);
        phone.MapPost("/tickets/{number:int}/status", (int number, StatusRequest? request, ClaimsPrincipal user, HelpdeskStore store) =>
            Work(number, user, store, () => store.SetTicketStatus(number, request?.Status)));
        phone.MapPost("/tickets/{number:int}/priority", (int number, PriorityRequest? request, ClaimsPrincipal user, HelpdeskStore store) =>
            Work(number, user, store, () => store.SetTicketPriority(number, request?.Priority)));
        phone.MapPost("/tickets/{number:int}/close", (int number, CloseRequest? request, ClaimsPrincipal user, HelpdeskStore store) =>
            Work(number, user, store, () => store.CloseTicket(number, request?.Message)));
        phone.MapPost("/tickets", LogTicket);
        phone.MapGet("/alerts", Alerts);
        phone.MapGet("/alerts/poll", PollAlerts);
        phone.MapPost("/alerts/read", (ReadRequest? request, ClaimsPrincipal user, HelpdeskStore store) =>
        {
            var me = Me(user);
            if (request?.All == true) store.MarkAllNotificationsRead(me);
            else if (long.TryParse(request?.Id, out var id)) store.MarkNotificationRead(me, id);
            return Results.NoContent();
        });
        return app;
    }

    // ---- Signing in with a password, for a phone with no pairing code to hand ----

    // The web sign-in's rules (Pages/Login, Pages/LoginCode): the same lockout counts, the same vague "incorrect", and two-step
    // sign-in where the account has it. What the web would make someone do first - change a temporary password, set up
    // two-step sign-in the school requires - they do on the web, then come back.
    private static IResult SignIn(SignInRequest? request, HttpContext http, HelpdeskStore store, SignInThrottle throttle, TwoFactorPending pending)
    {
        var email = (request?.Email ?? "").Trim();
        var address = http.Connection.RemoteIpAddress;
        if (throttle.Refusal("helpdesk", email, address) is { } refusal) return Refuse(StatusCodes.Status429TooManyRequests, refusal);
        var technician = store.Technicians.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));
        var passwordOk = PasswordHasher.Verify(technician?.PasswordHash, request?.Password ?? "");
        if (technician is null || !technician.IsActive || !passwordOk)
        {
            throttle.Failed("helpdesk", email, address);
            return Refuse(StatusCodes.Status400BadRequest, "Incorrect email or password.");
        }
        throttle.Succeeded("helpdesk", email);
        if (store.PhoneRefusal(technician) is { } off) return Refuse(StatusCodes.Status403Forbidden, off);
        if (technician.RequirePasswordChange) return Refuse(StatusCodes.Status403Forbidden, "Your password is a temporary one. Sign in on the web and choose your own first, then sign in here.");
        if (technician.TwoFactor is not null) return Results.Json(new { pendingId = pending.StartForPhone(technician) });
        if (store.RequireTwoFactor) return Refuse(StatusCodes.Status403Forbidden, "This helpdesk requires two-step sign-in. Set it up on the web first (your name → Two-step sign-in), then sign in here.");
        return Results.Json(new { token = store.IssuePhoneToken(technician, request?.DeviceName, HelpdeskStore.PhonePairedBy.Password, address) });
    }

    private static IResult SignInCode(CodeRequest? request, HttpContext http, HelpdeskStore store, SignInThrottle throttle, TwoFactorPending pending)
    {
        if (pending.ReadForPhone(request?.PendingId, store) is not { } technician) return Refuse(StatusCodes.Status400BadRequest, "That took too long. Sign in again.");
        var address = http.Connection.RemoteIpAddress;
        if (throttle.Refusal(Pages.LoginCodeModel.ThrottleForm, technician.Email, address) is { } refusal) return Refuse(StatusCodes.Status429TooManyRequests, refusal);
        var check = store.CheckTwoFactor(technician.Id, request?.Code);
        if (!check.Ok)
        {
            throttle.Failed(Pages.LoginCodeModel.ThrottleForm, technician.Email, address);
            return Refuse(StatusCodes.Status400BadRequest, check.UsedRecoveryCode ? "That recovery code isn't one of yours, or it has been used." : "That code didn't match. Type the code the app shows now.");
        }
        throttle.Succeeded(Pages.LoginCodeModel.ThrottleForm, technician.Email);
        if (store.PhoneRefusal(technician) is { } off) return Refuse(StatusCodes.Status403Forbidden, off);
        return Results.Json(new { token = store.IssuePhoneToken(technician, request?.DeviceName, HelpdeskStore.PhonePairedBy.Password, address) });
    }

    // ---- Who, and which desks ----

    private static IResult WhoAmI(ClaimsPrincipal user, HttpContext http, HelpdeskStore store)
    {
        var technician = store.Technicians.First(x => x.Id == Me(user));
        var desks = VisibleDesks(user, store);
        return Results.Json(new
        {
            id = technician.Id,
            name = technician.Name,
            role = technician.Role,
            team = technician.Team,
            schoolName = store.Branding.BrandName,
            serverAddress = store.SiteAddress.Length > 0 ? store.SiteAddress : $"{http.Request.Scheme}://{http.Request.Host}",
            serverVersion = ServerVersion,
            defaultDeskId = (store.StartingDeskFor(user) ?? desks.FirstOrDefault())?.Id,
            priorities = store.Priorities,
            // Settings → Sign-in security: the app must lock itself after time away.
            appLockRequired = store.RequirePhoneAppLock,
            desks = desks.Select(d => DeskJson(d, store))
        });
    }

    private static IResult Options(Guid? desk, ClaimsPrincipal user, HelpdeskStore store)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.New)) return Refuse(StatusCodes.Status403Forbidden, "Your role can't log tickets.");
        if (VisibleDesks(user, store).FirstOrDefault(x => x.Id == desk) is not { } chosen) return Refuse(StatusCodes.Status404NotFound, "That desk isn't one you work on.");
        return Results.Json(new
        {
            categories = store.CategoriesFor(chosen.Id).Select(name => new { name, deskId = chosen.Id, icon = store.StyleFor(name).Icon }),
            priorities = store.Priorities,
            locations = store.Locations,
            technicians = store.Technicians.Where(x => x.IsActive && HelpdeskStore.TechnicianOnDesk(x, chosen.Id)).OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name, email = x.Email, department = x.Team, location = "" })
        });
    }

    // Requesters, for Log a ticket: name, email or department, as the web New ticket form searches them.
    private static IResult People(string? q, ClaimsPrincipal user, HelpdeskStore store)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.New)) return Refuse(StatusCodes.Status403Forbidden, "Your role can't log tickets.");
        var words = (q ?? "").Trim();
        var found = store.Users.Where(x => x.IsActive && (words.Length == 0
                || x.Name.Contains(words, StringComparison.OrdinalIgnoreCase)
                || x.Email.Contains(words, StringComparison.OrdinalIgnoreCase)
                || x.Department.Contains(words, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name).Take(MaxPeople);
        return Results.Json(found.Select(PersonJson));
    }

    // ---- Tickets ----

    // The web Tickets page's views, counts and search (TicketListQuery), for one desk or every desk the person sees.
    private static IResult Tickets(string? view, Guid? desk, string? q, ClaimsPrincipal user, HelpdeskStore store)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.Access)) return Refuse(StatusCodes.Status403Forbidden, "Your role can't see tickets.");
        var chosen = TicketListQuery.Views.Contains(view) ? view! : "open";
        var deskOf = store.DeskLookup();
        var all = store.TicketsFor(user);
        var scoped = desk is { } only ? all.Where(t => deskOf(t.Category).Id == only).ToList() : all;
        var context = Context(user, store, deskOf, search: !string.IsNullOrWhiteSpace(q));
        var (sort, descending) = TicketListQuery.DefaultSort(chosen);
        var query = new TicketListQuery { View = chosen, Search = q, Sort = sort, Descending = descending };
        if (desk is { } d) query.Desks.Add(d);
        var counts = TicketListQuery.ViewCounts(string.IsNullOrWhiteSpace(q) ? scoped : new TicketListQuery { View = "all", Search = q }.Run(scoped, context), context);
        var me = Me(user);
        return Results.Json(new
        {
            tickets = query.Run(scoped, context).Take(MaxTickets).Select(t => TicketJson(t, user, me, store, full: false)),
            counts
        });
    }

    private static IResult Ticket(int number, ClaimsPrincipal user, HelpdeskStore store)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.View)) return Refuse(StatusCodes.Status403Forbidden, "Your role can't open tickets.");
        if (Visible(number, user, store) is not { } ticket) return Refuse(StatusCodes.Status404NotFound, "That ticket isn't one you can see.");
        var me = Me(user);
        // As opening it in the browser does: whatever the bell was holding about it has been dealt with.
        store.MarkTicketNotificationsRead(me, number);
        return Results.Json(TicketJson(ticket, user, me, store, full: true));
    }

    private static IResult Attachment(int number, Guid id, ClaimsPrincipal user, HelpdeskStore store)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.View) || Visible(number, user, store) is null) return Refuse(StatusCodes.Status404NotFound, "That ticket isn't one you can see.");
        if (store.FindAttachment(number, id) is not { } found) return Refuse(StatusCodes.Status404NotFound, "That file is no longer on the ticket.");
        return Results.File(found.Path, found.Attachment.ContentType, found.Attachment.FileName);
    }

    private static IResult Take(int number, ClaimsPrincipal user, HelpdeskStore store, Func<Guid, HelpdeskStore.TakeResult> take)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.Edit)) return Refuse(StatusCodes.Status403Forbidden, "Your role can't change tickets.");
        if (Visible(number, user, store) is null) return Refuse(StatusCodes.Status404NotFound, "That ticket isn't one you can see.");
        var me = Me(user);
        var result = take(me);
        if (!result.Ok) return Refuse(StatusCodes.Status409Conflict, result.Message);
        return Results.Json(new { ticket = TicketJson(store.Tickets.First(x => x.Number == number), user, me, store, full: true), message = result.Message });
    }

    // Status, priority and close (HelpdeskStore.WorkTicket): the ticket page's rules and words.
    private static IResult Work(int number, ClaimsPrincipal user, HelpdeskStore store, Func<HelpdeskStore.WorkResult> work)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.Edit)) return Refuse(StatusCodes.Status403Forbidden, "Your role can't change tickets.");
        if (Visible(number, user, store) is null) return Refuse(StatusCodes.Status404NotFound, "That ticket isn't one you can see.");
        var result = work();
        if (!result.Ok) return Refuse(StatusCodes.Status409Conflict, result.Message);
        return Results.Json(new { ticket = TicketJson(store.Tickets.First(x => x.Number == number), user, Me(user), store, full: true), message = result.Message });
    }

    // A reply or internal note (HelpdeskStore.AddTicketReply), optionally moving the status, with up to the usual number of
    // photos. Photos follow the comment: shared with the requester on a reply, internal on a note. Sent as a form, because
    // of the photos; the photos are only kept once the reply is.
    private static async Task<IResult> Reply(int number, HttpRequest request, ClaimsPrincipal user, HelpdeskStore store)
    {
        if (!request.HasFormContentType) return Refuse(StatusCodes.Status400BadRequest, "Send the reply as a form.");
        var form = await request.ReadFormAsync();
        string? Field(string name) => form[name].FirstOrDefault() is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        var internalNote = string.Equals(Field("internal"), "true", StringComparison.OrdinalIgnoreCase);
        var notify = string.Equals(Field("notifyRequester"), "true", StringComparison.OrdinalIgnoreCase);
        return Work(number, user, store, () =>
        {
            var result = store.AddTicketReply(number, Field("text"), internalNote, notify, Field("status"));
            if (!result.Ok || form.Files.Count == 0) return result;
            var added = 0;
            var problems = new List<string>();
            foreach (var photo in form.Files.Take(HelpdeskStore.MaxAttachmentsPerUpload))
            {
                using var stream = photo.OpenReadStream();
                if (store.AddTicketAttachment(number, photo.FileName, stream, photo.Length, visibleToRequester: !internalNote) is { } error) problems.Add(error);
                else added++;
            }
            var photos = added > 0 ? $" {added} photo{(added == 1 ? "" : "s")} attached." : "";
            return result with { Message = string.Join(" ", new[] { result.Message + photos }.Concat(problems)) };
        });
    }

    // Log a ticket (Pages/NewTicket's rules) or a job already done (Pages/QuickLog's, HelpdeskStore.LogDoneJob), with up to
    // the usual number of photos. Sent as a form, because of the photos.
    private static async Task<IResult> LogTicket(HttpRequest request, ClaimsPrincipal user, HelpdeskStore store)
    {
        if (!store.UserCan(user, Modules.Tickets, ModulePermission.New)) return Refuse(StatusCodes.Status403Forbidden, "Your role can't log tickets.");
        if (!request.HasFormContentType) return Refuse(StatusCodes.Status400BadRequest, "Send the ticket as a form.");
        var form = await request.ReadFormAsync();
        string? Field(string name) => form[name].FirstOrDefault() is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        var me = Me(user);
        var category = Field("category");
        var title = Field("title");
        var location = Field("location");
        var visibleDesks = VisibleDesks(user, store).Select(x => x.Id).ToHashSet();
        if (category is null || !store.Categories.Contains(category, StringComparer.OrdinalIgnoreCase) || !visibleDesks.Contains(store.DeskForCategory(category).Id))
            return Refuse(StatusCodes.Status400BadRequest, "Choose what it's about.");
        if (title is null) return Refuse(StatusCodes.Status400BadRequest, "Give it a short title.");
        if (location is not null && !store.Locations.Contains(location, StringComparer.OrdinalIgnoreCase)) return Refuse(StatusCodes.Status400BadRequest, "Choose a location from the list.");

        int number;
        if (string.Equals(Field("alreadyDone"), "true", StringComparison.OrdinalIgnoreCase))
        {
            var doneBy = Guid.TryParse(Field("doneById"), out var who) ? who : me;
            DateTime? at = DateTime.TryParse(Field("doneAt"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var when) ? when.ToUniversalTime() : null;
            var (logged, error) = store.LogDoneJob(me, new HelpdeskStore.DoneJob(category, title, Field("description"), location, doneBy, at));
            if (logged is null) return Refuse(StatusCodes.Status400BadRequest, error ?? "The job couldn't be logged.");
            number = logged.Value;
        }
        else
        {
            var priority = Field("priority") ?? store.Priorities.FirstOrDefault() ?? "Normal";
            if (!store.Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase)) return Refuse(StatusCodes.Status400BadRequest, "Choose a priority from the list.");
            if (!Guid.TryParse(Field("requesterId"), out var requester) || !store.Users.Any(x => x.Id == requester && x.IsActive))
                return Refuse(StatusCodes.Status400BadRequest, "Choose who it is for.");
            var now = DateTime.UtcNow;
            var sla = store.SlaFor(priority, category);
            number = store.AddTicket(new TicketRecord(0, title, Field("description") ?? "", requester, [], null, priority, store.StartingStatusFor(category), category,
                now, null, sla, store.CalculateDueDate(sla, now, category), false, false, null, location) { Type = TicketTypes.Incident });
        }
        foreach (var photo in form.Files.Take(HelpdeskStore.MaxAttachmentsPerUpload))
        {
            using var stream = photo.OpenReadStream();
            store.AddTicketAttachment(number, photo.FileName, stream, photo.Length);
        }
        return Results.Json(TicketJson(store.Tickets.First(x => x.Number == number), user, me, store, full: true), statusCode: StatusCodes.Status201Created);
    }

    // ---- Alerts: the web bell's notification centre ----

    private static IResult Alerts(ClaimsPrincipal user, HttpContext http, HelpdeskStore store, LinkGenerator links)
    {
        if (!store.NotificationsOpenFor(HelpdeskStore.StaffAudience)) return Results.Json(new { items = Array.Empty<object>(), disabled = true });
        var inbox = store.InboxFor(Me(user), NotificationEndpoints.Broadcast(store, user), InboxSize, store.VisibleDeskIds(user));
        return Results.Json(new { unread = inbox.Unread, items = inbox.Entries.Select(x => AlertJson(x.Notification, x.Unread, http, store, links)) });
    }

    // While the app is open: waits up to 25 seconds for something new, as the browser's bell does (NotificationEndpoints).
    private static async Task<IResult> PollAlerts(long? after, int? wait, ClaimsPrincipal user, HttpContext http, HelpdeskStore store, LinkGenerator links, IHostApplicationLifetime lifetime)
    {
        if (!store.NotificationsOpenFor(HelpdeskStore.StaffAudience)) return Results.Json(new { disabled = true });
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, lifetime.ApplicationStopping);
        var seconds = Math.Clamp(wait ?? 0, 0, (int)LongestWait.TotalSeconds);
        var batch = await store.WaitForNotificationsAsync(HelpdeskStore.StaffAudience, Me(user), after ?? -1, TimeSpan.FromSeconds(seconds), ending.Token,
            NotificationEndpoints.Broadcast(store, user), knownUnread: null, store.VisibleDeskIds(user));
        return Results.Json(new { cursor = batch.Cursor, items = batch.Items.Where(x => x.Kind != HelpdeskStore.NotificationKinds.Test).Select(x => AlertJson(x, true, http, store, links)) });
    }

    // ---- Shapes ----

    private static object TicketJson(TicketRecord t, ClaimsPrincipal user, Guid me, HelpdeskStore store, bool full)
    {
        var desk = store.DeskOf(t);
        var requester = store.Users.FirstOrDefault(x => x.Id == t.RequesterId);
        var technician = t.TechnicianId is { } id ? store.Technicians.FirstOrDefault(x => x.Id == id) : null;
        var self = store.Technicians.FirstOrDefault(x => x.Id == me);
        var canEdit = store.UserCan(user, Modules.Tickets, ModulePermission.Edit) && self is not null;
        var asset = store.IsFeatureOn(Features.Assets) ? t.AssetIds.Select(a => store.Assets.FirstOrDefault(x => x.Id == a)).FirstOrDefault(x => x is not null) : null;
        var attachments = store.GetTicketAttachments(t.Number);
        return new
        {
            number = t.Number,
            @ref = store.Ref(t),
            deskId = desk.Id,
            title = t.Title,
            description = full ? t.Description : "",
            type = t.Type,
            category = t.Category,
            priority = t.Priority,
            status = t.Status,
            statusTone = store.StatusTone(t.Status),
            requester = requester is null ? new { id = t.RequesterId, name = "Unknown requester", email = "", department = "", location = "" } : PersonJson(requester),
            createdAt = Utc(t.CreatedAt),
            updatedAt = Utc(t.LastModifiedAt),
            technicianId = t.TechnicianId,
            technicianName = technician?.Name,
            team = t.TeamName ?? "",
            location = t.Location ?? "",
            assetTag = asset?.AssetTag,
            dueAt = t.IsSlaPaused ? null : Utc(t.DueDate),
            sla = t.SlaId is { } sla ? store.Slas.FirstOrDefault(x => x.Id == sla)?.Name : null,
            closedAt = Utc(t.ClosedAt),
            commentCount = t.Comments.Count,
            attachmentCount = attachments.Count,
            comments = full ? t.Comments.Select(c => new
            {
                author = c.By?.Name ?? (c.FromRequester ? requester?.Name ?? "Requester" : "Helpdesk"),
                text = c.Text,
                at = Utc(c.CreatedAt),
                @internal = c.IsInternal,
                byRequester = c.FromRequester
            }) : null,
            history = full ? t.History.Select(h => new { action = h.Action, details = h.By is { } by ? $"{h.Details} ({by.Name})" : h.Details, at = Utc(h.CreatedAt) }) : null,
            attachments = full ? attachments.Select(a => new { id = a.Id, fileName = a.FileName, contentType = a.ContentType }) : null,
            canAssignToMe = canEdit && !TicketInsights.IsClosed(t) && t.TechnicianId != me && store.TakeRefusal(t, self!, onMyWay: false) is null,
            canSayOnMyWay = canEdit && store.TakeRefusal(t, self!, onMyWay: true) is null,
            // Reply, status, priority and close: the role's Tickets: Edit, as on the ticket page.
            canEdit,
            // Whether a reply can tell the requester (a portal pop-up), and whether Close needs a closing message.
            requesterSeesIt = full && store.PortalTicket(t.RequesterId, t.Number) is not null,
            closeNeedsMessage = full && store.RequiresCloseMessage(t),
            // The ticket's desk's statuses, for the status picker, with their tones.
            statuses = full ? store.StatusesFor(desk.Id).Select(s => new { name = s, tone = store.StatusTone(s) }) : null
        };
    }

    private static object PersonJson(UserRecord x) => new { id = x.Id, name = x.Name, email = x.Email, department = x.Department, location = x.Location };

    // The desk's colour is its staff-portal tile colour (PortalLook), the same one the web app shows.
    private static object DeskJson(Desk d, HelpdeskStore store) => new
    {
        id = d.Id,
        name = d.Name,
        prefix = d.Prefix,
        color = store.StyleForDesk(d).Color,
        hours = d.HasOwnHours ? string.Join(", ", d.WorkWindows.Select(w => w.Start.ToString("HH:mm") + "–" + w.End.ToString("HH:mm"))) : "",
        // When the phone's "Only during my desk's hours" lets alerts through: the desk's own days and windows, as its SLA
        // clock uses (HelpdeskStore.ClockFor), or the school days from the first period's start to the last one's end -
        // breaks included, when technicians still pick up jobs. Days are ISO, 1 Monday to 7 Sunday. No windows: any time.
        alertDays = (d.HasOwnHours && d.WorkDays.Count > 0 ? d.WorkDays : store.SchoolDays).Select(x => x == DayOfWeek.Sunday ? 7 : (int)x).Order(),
        alertWindows = d.HasOwnHours
            ? d.WorkWindows.Select(w => new { start = w.Start.ToString("HH:mm"), end = w.End.ToString("HH:mm") }).ToList()
            : store.SchoolPeriods.Count == 0 ? []
            : [new { start = store.SchoolPeriods.Min(x => x.Start).ToString("HH:mm"), end = store.SchoolPeriods.Max(x => x.End).ToString("HH:mm") }]
    };

    // Kinds in the app's words; titles in the bell's (generic on purpose: the number, never the ticket's words).
    private static object AlertJson(HelpdeskStore.Notification n, bool unread, HttpContext http, HelpdeskStore store, LinkGenerator links) => new
    {
        id = n.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        kind = n.Kind switch
        {
            HelpdeskStore.NotificationKinds.NewTicket => "new-ticket",
            HelpdeskStore.NotificationKinds.Reply => "reply",
            HelpdeskStore.NotificationKinds.Assigned => "assigned",
            _ => n.Kind
        },
        title = NotificationEndpoints.Describe(n, links, http, forCentre: true).Title,
        at = Utc(n.CreatedAt),
        unread,
        ticketNumber = n.TicketNumber,
        deskId = n.DeskId ?? (n.TicketNumber is { } number && store.Tickets.FirstOrDefault(x => x.Number == number) is { } t ? store.DeskOf(t).Id : null)
    };

    // ---- Helpers ----

    public const string AppFolder = "phone-app";

    // The newest EduHelpdesk-Android-<version>.apk in the program's phone-app folder, where the release zip puts it.
    public static (string Path, Version Version)? LatestApp(IWebHostEnvironment environment) => LatestApp(Path.Combine(environment.ContentRootPath, AppFolder));

    public static (string Path, Version Version)? LatestApp(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        (string Path, Version Version)? best = null;
        foreach (var file in Directory.EnumerateFiles(folder, "EduHelpdesk-Android-*.apk"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(file), @"^EduHelpdesk-Android-(\d+\.\d+\.\d+)\.apk$");
            if (match.Success && Version.Parse(match.Groups[1].Value) is var version && (best is null || version > best.Value.Version)) best = (file, version);
        }
        return best;
    }

    private static IResult Refuse(int status, string message) => Results.Json(new { message }, statusCode: status);

    private static Guid Me(ClaimsPrincipal user) => Guid.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private static IReadOnlyList<Desk> VisibleDesks(ClaimsPrincipal user, HelpdeskStore store) =>
        store.VisibleDeskIds(user) is { } ids ? store.Desks.Where(x => ids.Contains(x.Id)).ToList() : store.Desks;

    private static TicketRecord? Visible(int number, ClaimsPrincipal user, HelpdeskStore store) => store.TicketsFor(user).FirstOrDefault(x => x.Number == number);

    private static TicketContext Context(ClaimsPrincipal user, HelpdeskStore store, Func<string?, Desk> deskOf, bool search) => new(
        store.Users, store.Technicians, store.Assets, store.Priorities, store.Statuses, DateTime.UtcNow, store.TicketDueSoonHours, Me(user),
        store.IsFeatureOn(Features.Onboarding) ? store.Onboardings.ToDictionary(x => x.TicketNumber) : null,
        search ? store.TicketAttributeAnswers() : null, deskOf);

    // Stored times are UTC; this makes sure they say so, so the phone shows them in its own time.
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    public static string ServerVersion { get; } = typeof(PhoneEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(PhoneEndpoints).Assembly.GetName().Version?.ToString(3) ?? "";
}
