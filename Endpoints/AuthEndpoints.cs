namespace AuthService.Endpoints;

using AuthService.Models;
using AuthService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth")
            .WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .AllowAnonymous();

        group.MapPost("/revoke", RevokeAsync)
            .WithName("Revoke")
            .RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest req,
        IAuthService authService,
        IValidator<RegisterRequest> validator,
        HttpContext ctx,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var ip = GetIpAddress(ctx);
        var (response, error) = await authService.RegisterAsync(req, ip, ct);

        return error is null
            ? Results.Ok(response)
            : Results.Conflict(new ProblemDetails
            {
                Title = "Kayıt başarısız",
                Detail = error,
                Status = StatusCodes.Status409Conflict
            });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest req,
        IAuthService authService,
        IValidator<LoginRequest> validator,
        HttpContext ctx,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var ip = GetIpAddress(ctx);
        var (response, error) = await authService.LoginAsync(req, ip, ct);

        return error is null
            ? Results.Ok(response)
            : Results.Problem(
                title: "Giriş başarısız",
                detail: error,
                statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest req,
        IAuthService authService,
        HttpContext ctx,
        CancellationToken ct)
    {
        var ip = GetIpAddress(ctx);
        var (response, error) = await authService.RefreshAsync(req.RefreshToken, ip, ct);

        return error is null
            ? Results.Ok(response)
            : Results.Problem(
                title: "Token yenileme başarısız",
                detail: error,
                statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RevokeAsync(
        RevokeRequest req,
        IAuthService authService,
        HttpContext ctx,
        CancellationToken ct)
    {
        var ip = GetIpAddress(ctx);
        var revoked = await authService.RevokeAsync(req.RefreshToken, ip, ct);

        return revoked
            ? Results.NoContent()
            : Results.Problem(
                title: "Token iptal edilemedi",
                detail: "Token bulunamadı veya zaten iptal edilmiş.",
                statusCode: StatusCodes.Status400BadRequest);
    }

    private static string GetIpAddress(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd)
            ? fwd.ToString().Split(',')[0].Trim()
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
