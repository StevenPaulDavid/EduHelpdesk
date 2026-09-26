using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;

namespace EduHelpdesk.Services;

// A change the database wouldn't take - locked, full, unwritable. Normally the store has already put its memory back to
// what was last saved (HelpdeskStore.Persist), so "nothing was changed" is true. If it couldn't even re-read the
// database, the message says so rather than promising something it can't.
public sealed class SaveFailedException(Exception inner, bool restored)
    : Exception(restored
        ? "Your change couldn't be saved, so nothing was changed. The database may be busy or unavailable - try again in a moment, and if it keeps happening let whoever looks after the helpdesk know (the details are in its log)."
        : "Your change couldn't be saved, and the helpdesk couldn't re-read its database either. Restart the app before making more changes, and let whoever looks after the helpdesk know (the details are in its log).", inner)
{
    public bool Restored { get; } = restored;
}

// Turns a failed save into that message on the page it came from, instead of an error page. Registered for every
// Razor Page in Program.cs. Pages show TempData["Message"] as their notice.
public sealed class SaveFailureFilter(ITempDataDictionaryFactory tempData, ILogger<SaveFailureFilter> logger) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception is not SaveFailedException failure || executed.ExceptionHandled) return;
        logger.LogError(failure.InnerException, "A change could not be saved; the store was reloaded from the database");
        tempData.GetTempData(context.HttpContext)["Message"] = failure.Message;
        executed.ExceptionHandled = true;
        // Back to the page the change was made on, as a plain GET: the same address without the handler.
        var request = context.HttpContext.Request;
        var query = QueryHelpers.ParseQuery(request.QueryString.Value)
            .Where(x => !string.Equals(x.Key, "handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Value.Select(v => new KeyValuePair<string, string?>(x.Key, v)));
        executed.Result = new RedirectResult(request.PathBase + request.Path + QueryString.Create(query));
    }
}
