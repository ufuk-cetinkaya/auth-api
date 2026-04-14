using AuthService.Models;

namespace AuthService.Services;

public interface IAuthService
{
    Task<(AuthResponse response, string? error)> RegisterAsync(
        RegisterRequest req, string ipAddress, CancellationToken ct = default);

    Task<(AuthResponse? response, string? error)> LoginAsync(
        LoginRequest req, string ipAddress, CancellationToken ct = default);

    Task<(AuthResponse? response, string? error)> RefreshAsync(
        string refreshToken, string ipAddress, CancellationToken ct = default);

    Task<bool> RevokeAsync(
        string refreshToken, string ipAddress, CancellationToken ct = default);
}