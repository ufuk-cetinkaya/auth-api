namespace AuthService.Endpoints;

using System.Security.Claims;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/me", GetMeAsync)
            .WithName("GetMe");
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        if (userId is null) return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Results.NotFound();

        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.CreatedAt
        });
    }
}