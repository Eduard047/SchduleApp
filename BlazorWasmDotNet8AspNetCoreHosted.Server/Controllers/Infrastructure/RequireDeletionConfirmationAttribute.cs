using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Controllers.Infrastructure;

/// <summary>
/// Вимагає явного підтвердження перед виконанням руйнівної операції.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireDeletionConfirmationAttribute : Attribute, IAsyncActionFilter
{
    public RequireDeletionConfirmationAttribute(string? subject = null)
    {
        Subject = subject;
    }

    public string? Subject { get; }

    /// <summary>
    /// Дозволяє явно вказати назву аргументу екшену, значення якого потрібно повернути клієнту як targetId.
    /// За замовчуванням використовується аргумент з назвою "id".
    /// </summary>
    public string? TargetArgumentName { get; init; }

    /// <summary>
    /// Можна передати власний текст попередження (українською).
    /// </summary>
    public string? Message { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IsConfirmed(context.HttpContext.Request.Query))
        {
            await next();
            return;
        }

        object? target = null;
        if (!string.IsNullOrWhiteSpace(TargetArgumentName) &&
            context.ActionArguments.TryGetValue(TargetArgumentName, out var named))
        {
            target = named;
        }
        else if (context.ActionArguments.TryGetValue("id", out var idValue))
        {
            target = idValue;
        }

        var text = Message ?? (string.IsNullOrWhiteSpace(Subject)
            ? "Підтвердіть намір видалити запис, щоб уникнути випадкових втрат."
            : $"Підтвердіть, що хочете видалити {Subject}, щоб уникнути випадкових втрат.");

        context.Result = new BadRequestObjectResult(new
        {
            message = text,
            requiresConfirmation = true,
            targetId = target
        });
    }

    private static bool IsConfirmed(IQueryCollection query)
    {
        if (!query.TryGetValue("confirm", out var values)) return false;
        var raw = values.ToString();
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("1")
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
