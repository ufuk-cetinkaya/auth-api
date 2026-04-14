namespace AuthService.Options;

using System.ComponentModel.DataAnnotations;

public sealed class JwtOptions
{
    [Required] public string Issuer { get; init; } = default!;
    [Required] public string Audience { get; init; } = default!;

    [Required, MinLength(32)]
    public string SecretKey { get; init; } = default!;

    [Range(1, 60)]
    public int AccessTokenExpiryMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenExpiryDays { get; init; } = 7;
}