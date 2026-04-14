using AuthService.Data;
using AuthService.Models;
using AuthService.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthService.Services;

public sealed class AuthService(
    UserManager<AppUser> userManager,
    ITokenService tokenService,
    AppDbContext db,
    IOptions<JwtOptions> opts,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions _opts = opts.Value;

    public async Task<(AuthResponse response, string? error)> RegisterAsync(
        RegisterRequest req, string ipAddress, CancellationToken ct = default)
    {
        var existing = await userManager.FindByEmailAsync(req.Email);
        if (existing is not null)
            return (null!, "Bu e-posta adresi zaten kayıtlı.");

        var user = new AppUser
        {
            UserName = req.Email,
            Email = req.Email,
            FirstName = req.FirstName,
            LastName = req.LastName,
        };

        var result = await userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (null!, errors);
        }

        await userManager.AddToRoleAsync(user, "User");

        logger.LogInformation("Yeni kullanıcı kaydedildi: {UserId}", user.Id);

        return await IssueTokensAsync(user, ipAddress, ct);
    }

    public async Task<(AuthResponse? response, string? error)> LoginAsync(
        LoginRequest req, string ipAddress, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive)
            return (null, "Geçersiz kimlik bilgileri.");

        if (await userManager.IsLockedOutAsync(user))
            return (null, "Hesap geçici olarak kilitlendi. Lütfen daha sonra tekrar deneyin.");

        var valid = await userManager.CheckPasswordAsync(user, req.Password);
        if (!valid)
        {
            await userManager.AccessFailedAsync(user);
            logger.LogWarning("Başarısız giriş denemesi: {Email} / IP: {Ip}", req.Email, ipAddress);
            return (null, "Geçersiz kimlik bilgileri.");
        }

        await userManager.ResetAccessFailedCountAsync(user);

        logger.LogInformation("Kullanıcı giriş yaptı: {UserId}", user.Id);

        return await IssueTokensAsync(user, ipAddress, ct);
    }

    public async Task<(AuthResponse? response, string? error)> RefreshAsync(
        string refreshToken, string ipAddress, CancellationToken ct = default)
    {
        var hash = tokenService.HashToken(refreshToken);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive)
        {
            if (stored?.User is not null)
            {
                logger.LogWarning(
                    "Geçersiz/süresi dolmuş refresh token kullanım denemesi. UserId: {UserId}",
                    stored.User.Id);
                await RevokeAllUserTokensAsync(stored.User.Id, "Şüpheli aktivite", ct);
            }
            return (null, "Geçersiz ya da süresi dolmuş refresh token.");
        }

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedByIp = ipAddress;

        var (newPlain, newHash) = tokenService.GenerateRefreshToken();
        var newToken = new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = newHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_opts.RefreshTokenExpiryDays),
            CreatedByIp = ipAddress,
        };
        stored.ReplacedByTokenId = newToken.Id;

        db.RefreshTokens.Add(newToken);
        await db.SaveChangesAsync(ct);

        var roles = await userManager.GetRolesAsync(stored.User);
        var access = tokenService.GenerateAccessToken(stored.User, roles);
        var expiry = DateTime.UtcNow.AddMinutes(_opts.AccessTokenExpiryMinutes);

        return (new AuthResponse(access, newPlain, expiry), null);
    }

    public async Task<bool> RevokeAsync(
        string refreshToken, string ipAddress, CancellationToken ct = default)
    {
        var hash = tokenService.HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive) return false;

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedByIp = ipAddress;

        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<(AuthResponse, string? error)> IssueTokensAsync(
        AppUser user, string ipAddress, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var access = tokenService.GenerateAccessToken(user, roles);
        var expiry = DateTime.UtcNow.AddMinutes(_opts.AccessTokenExpiryMinutes);

        var (plain, hash) = tokenService.GenerateRefreshToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(_opts.RefreshTokenExpiryDays),
            CreatedByIp = ipAddress,
        };

        db.RefreshTokens.Add(refresh);

        var expired = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && (t.IsRevoked || t.ExpiresAt < DateTime.UtcNow))
            .ToListAsync(ct);
        db.RefreshTokens.RemoveRange(expired);

        await db.SaveChangesAsync(ct);

        return (new AuthResponse(access, plain, expiry), null);
    }

    private async Task RevokeAllUserTokensAsync(
        string userId, string reason, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(ct);

        foreach (var t in tokens)
        {
            t.IsRevoked = true;
            t.RevokedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        logger.LogWarning("Tüm tokenlar iptal edildi. UserId: {UserId}, Sebep: {Reason}",
            userId, reason);
    }
}