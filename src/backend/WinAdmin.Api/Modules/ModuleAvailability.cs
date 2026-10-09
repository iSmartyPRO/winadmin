using WinAdmin.Core.Abstractions;

namespace WinAdmin.Api.Modules;

/// <summary>Контроллер принадлежит модулю: выключенный или недоступный модуль — 404.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class WinAdminModuleAttribute(string moduleId) : Attribute
{
    public string ModuleId { get; } = moduleId;
}

/// <summary>Стоит после UseRouting и до UseAuthentication: 404 не раскрывает наличие функции.</summary>
public sealed class ModuleAvailabilityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IModuleRegistry modules)
    {
        var module = context.GetEndpoint()?.Metadata.GetMetadata<WinAdminModuleAttribute>();
        if (module is not null && !modules.GetState(module.ModuleId).Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    }
}
