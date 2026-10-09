using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Исключения сервисов ядра → HTTP: 400 / 403 (+violations) / 404 / 409.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlatformErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            AccessDeniedException e => new ObjectResult(new { message = e.Message, violations = e.Violations }) { StatusCode = 403 },
            KeyNotFoundException e => new NotFoundObjectResult(new { message = e.Message }),
            InvalidOperationException e => new ConflictObjectResult(new { message = e.Message }),
            ArgumentException e => new BadRequestObjectResult(new { message = e.Message }),
            _ => null,
        };
        if (context.Result is not null)
            context.ExceptionHandled = true;
    }
}
