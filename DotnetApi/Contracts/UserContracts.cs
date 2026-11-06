using DotnetApi.Models;

namespace DotnetApi.Contracts;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string Name,
    UserRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateUserRequest(
    string? Email,
    string? Name,
    UserRole? Role,
    string? Password = null);

public sealed class UpdateUserRequest
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public UserRole? Role { get; set; }
    public string? Password { get; set; }
}

public static class UserMappings
{
    public static UserResponse ToResponse(this AppUser user) => new(
        user.Id,
        user.Email,
        user.Name,
        user.Role,
        user.CreatedAt,
        user.UpdatedAt);
}
