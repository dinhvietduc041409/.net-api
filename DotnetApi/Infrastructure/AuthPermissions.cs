using DotnetApi.Models;

namespace DotnetApi.Infrastructure;

public static class AuthPermissions
{
    public static IReadOnlyList<string> For(UserRole role) => role switch
    {
        UserRole.Admin =>
        [
            "admin:access",
            "user:read",
            "user:write",
            "product:read",
            "product:write",
        ],
        UserRole.Manager =>
        [
            "admin:access",
            "user:read",
            "product:read",
            "product:write",
        ],
        _ =>
        [
            "user:read",
            "product:read",
        ],
    };
}
