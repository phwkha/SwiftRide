using System.Text;
namespace ApiGateway.Auth;
public static class JwtSecretConfiguration
{
    public static string GetRequiredSecret(IConfigurationSection section)
    {
        var secret = section["Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Supply Jwt:Secret externally with at least 32 UTF-8 bytes.");
        return secret;
    }
}
