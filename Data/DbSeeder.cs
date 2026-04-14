namespace AuthService.Data;

using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public static class DbSeeder
{
    public static readonly string[] Roles = ["Admin", "User", "ReadOnly"];

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            logger.LogInformation("Bekleyen migration'lar uygulanıyor: {Count}", pending.Count());
            await db.Database.MigrateAsync();
        }

        foreach (var role in Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (result.Succeeded)
                    logger.LogInformation("Rol oluşturuldu: {Role}", role);
                else
                    logger.LogError("Rol oluşturulamadı: {Role} — {Errors}",
                        role, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        var adminEmail = config["Seed:AdminEmail"];
        var adminPassword = config["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "Seed:AdminEmail veya Seed:AdminPassword tanımlı değil. Admin kullanıcı oluşturulmadı.");
            return;
        }

        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing is not null)
        {
            if (!await userManager.IsInRoleAsync(existing, "Admin"))
            {
                await userManager.AddToRoleAsync(existing, "Admin");
                logger.LogInformation("Mevcut kullanıcıya Admin rolü eklendi: {Email}", adminEmail);
            }
            return;
        }

        var admin = new AppUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "System",
            LastName = "Admin",
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(admin, adminPassword);
        if (!createResult.Succeeded)
        {
            logger.LogError("Admin kullanıcı oluşturulamadı: {Errors}",
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRolesAsync(admin, ["Admin", "User"]);
        logger.LogInformation("Admin kullanıcı oluşturuldu: {Email}", adminEmail);
    }
}
