using System.Security.Cryptography;

namespace ApiGateway.Auth;

public record UserRecord(Guid Id, string Email, string PasswordHash, string Role, string FullName);

public class InMemoryIdentityStore
{
    private readonly Dictionary<string, UserRecord> _users;
    private readonly Dictionary<string, (Guid UserId, DateTimeOffset ExpiresAt)> _refreshTokens = new();
    private readonly object _lock = new();

    public InMemoryIdentityStore(IConfiguration configuration)
    {
        var demoPassword = configuration["DemoIdentity:Password"];
        if (string.IsNullOrWhiteSpace(demoPassword))
            throw new InvalidOperationException("Supply DemoIdentity:Password externally for the demo identity store.");
        var users = new UserRecord[]
        {
            new(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "rider@swiftride.vn",
                HashPassword(demoPassword),
                "Rider",
                "Nguyễn Văn Khách Hàng"),
            new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "driver1@swiftride.vn",
                HashPassword(demoPassword),
                "Driver",
                "Nguyễn Văn Tài Xế 1"),
            new(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "driver2@swiftride.vn",
                HashPassword(demoPassword),
                "Driver",
                "Trần Văn Tài Xế 2"),
            new(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "driver3@swiftride.vn",
                HashPassword(demoPassword),
                "Driver",
                "Lê Văn Tài Xế 3"),
            new(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                "admin@swiftride.vn",
                HashPassword(demoPassword),
                "Admin",
                "Admin SwiftRide")
        };

        _users = users.ToDictionary(u => u.Email, StringComparer.OrdinalIgnoreCase);
    }

    public UserRecord? ValidateCredentials(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        if (_users.TryGetValue(email.Trim(), out var user))
        {
            if (BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                return user;
            }
        }

        return null;
    }

    public string StoreRefreshToken(Guid userId, int expiryDays = 7)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(expiryDays);

        lock (_lock)
        {
            // Prune expired tokens during store to prevent unbounded memory growth
            var now = DateTimeOffset.UtcNow;
            var expiredKeys = _refreshTokens
                .Where(kvp => kvp.Value.ExpiresAt <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _refreshTokens.Remove(key);
            }

            _refreshTokens[token] = (userId, expiresAt);
        }

        return token;
    }

    public (Guid UserId, bool IsValid) ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return (Guid.Empty, false);
        }

        lock (_lock)
        {
            if (_refreshTokens.TryGetValue(refreshToken.Trim(), out var entry))
            {
                if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    return (entry.UserId, true);
                }

                _refreshTokens.Remove(refreshToken.Trim());
            }
        }

        return (Guid.Empty, false);
    }

    public void RevokeRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        lock (_lock)
        {
            _refreshTokens.Remove(refreshToken.Trim());
        }
    }

    public UserRecord? GetById(Guid id)
    {
        return _users.Values.FirstOrDefault(u => u.Id == id);
    }

    private static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);
}
