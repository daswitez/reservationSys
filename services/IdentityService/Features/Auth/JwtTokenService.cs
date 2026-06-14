using System.IdentityModel.Tokens.Jwt;
using System.Text;
using IdentityService.Domain;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Features.Auth;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public TokenResult Create(User user, IReadOnlyList<string> roles)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is required.");
        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is required.");
        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is required.");
        var expiresInMinutes = configuration.GetValue<int>("Jwt:ExpiresInMinutes");

        if (expiresInMinutes <= 0)
        {
            throw new InvalidOperationException("Jwt:ExpiresInMinutes must be greater than zero.");
        }

        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(expiresInMinutes);
        var payload = new JwtPayload
        {
            [JwtRegisteredClaimNames.Iss] = issuer,
            [JwtRegisteredClaimNames.Aud] = audience,
            [JwtRegisteredClaimNames.Sub] = user.UserId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iat] = EpochTime.GetIntDate(issuedAt),
            [JwtRegisteredClaimNames.Nbf] = EpochTime.GetIntDate(issuedAt),
            [JwtRegisteredClaimNames.Exp] = EpochTime.GetIntDate(expiresAt),
            [JwtRegisteredClaimNames.Email] = user.Email,
            ["user_id"] = user.UserId.ToString(),
            ["auth_version"] = user.AuthVersion,
            ["roles"] = roles
        };

        if (user.TenantId.HasValue)
        {
            payload["tenant_id"] = user.TenantId.Value.ToString();
        }

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            new JwtHeader(signingCredentials),
            payload);

        return new TokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            checked(expiresInMinutes * 60));
    }
}

public sealed record TokenResult(string AccessToken, int ExpiresIn);
