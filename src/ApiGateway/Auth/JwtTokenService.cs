using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Auth;

public class JwtTokenService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenMinutes;

    public JwtTokenService(IConfigurationSection jwtSection)
    {
        _secret = jwtSection["Secret"] ?? "SwiftRide-JWT-Secret-Key-2026-SuperSecure-256Bits!";
        _issuer = jwtSection["Issuer"] ?? "SwiftRide.Gateway";
        _audience = jwtSection["Audience"] ?? "SwiftRide.Client";
        _accessTokenMinutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var mins) ? mins : 15;
    }

    public int AccessTokenExpiresInSeconds => _accessTokenMinutes * 60;

    public string GenerateAccessToken(UserRecord user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.OutboundClaimTypeMap.Clear();

        var key = Encoding.UTF8.GetBytes(_secret);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("role", user.Role),
            new("name", user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
