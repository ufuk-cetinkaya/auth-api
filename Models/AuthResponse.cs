namespace AuthService.Models;

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiry,
    string TokenType = "Bearer");