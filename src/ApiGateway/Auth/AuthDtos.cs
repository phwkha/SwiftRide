namespace ApiGateway.Auth;

public record LoginRequest(string Email, string Password, string? Role = null);

public record RefreshRequest(string RefreshToken);

public record UserDto(Guid Id, string Email, string Role, string FullName)
{
    public string Name => FullName;
}

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    UserDto User);
