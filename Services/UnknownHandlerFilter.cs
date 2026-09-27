using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EduHelpdesk.Services;

// A request naming a handler the page doesn't have - a form from an old tab after an update, or someone trying
// addresses - used to fall through to the page with nothing loaded, and the detail pages then crashed with a server
// error. It is "not found" instead. A plain GET of a page that has no OnGet is normal and left alone.
public sealed class UnknownHandlerFilter : IPageFilter
{
    public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (context.HandlerMethod is not null) return;
        var request = context.HttpContext.Request;
        var namedHandler = context.RouteData.Values.ContainsKey("handler") || request.Query.ContainsKey("handler");
        if (namedHandler || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
            context.Result = new NotFoundResult();
    }

    public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
}
