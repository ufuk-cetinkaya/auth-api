namespace AuthService.Middleware;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx,
        Exception exception,
        CancellationToken ct)
    {
        logger.LogError(exception,
            "İşlenmeyen hata: {Message} | TraceId: {TraceId}",
            exception.Message,
            ctx.TraceIdentifier);

        var (status, title) = exception switch
        {
            OperationCanceledException => (499, "İstek iptal edildi"),
            _ => (500, "Sunucu hatası")
        };

        ctx.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Title = title,
                Status = status,
                Type = "https://tools.ietf.org/html/rfc7807"
            }
        });
    }
}