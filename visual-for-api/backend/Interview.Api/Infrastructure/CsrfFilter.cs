using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Interview.Api.Infrastructure;

//JSON API использует antiforgery напрямую, без сервисов MVC-представлений.
//Реализует фильтрацию по токену, если у нас запрос любой, кроме GET, то проверяет
public sealed class CsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method)
            || HttpMethods.IsOptions(context.HttpContext.Request.Method)) 
            return;
        try 
        { 
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new { detail = "Защитный токен недействителен. Обновите страницу и повторите действие." }) { StatusCode = 400 };
        }
    }
}
