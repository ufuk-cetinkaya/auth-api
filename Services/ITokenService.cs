using AuthService.Models;

namespace AuthService.Services;

public interface ITokenService
{
    string GenerateAccessToken(AppUser user, IList<string> roles);
    (string plainToken, string hash) GenerateRefreshToken();
    string HashToken(string token);
}