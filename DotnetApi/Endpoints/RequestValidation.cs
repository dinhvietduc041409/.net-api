using System.Net.Mail;
using DotnetApi.Contracts;
using DotnetApi.Models;

namespace DotnetApi.Endpoints;

internal static class RequestValidation
{
    public static Dictionary<string, string[]> Validate(CreateUserRequest request) =>
        ValidateUser(
            request.Email,
            request.Name,
            request.Role,
            request.Password,
            requireAll: true);

    public static Dictionary<string, string[]> Validate(UpdateUserRequest request) =>
        ValidateUser(
            request.Email,
            request.Name,
            request.Role,
            request.Password,
            requireAll: false);

    public static Dictionary<string, string[]> Validate(CreateProductRequest request) =>
        ValidateProduct(request.Name, request.Description, request.Price, requireAll: true);

    public static Dictionary<string, string[]> Validate(UpdateProductRequest request) =>
        ValidateProduct(request.Name, request.Description, request.Price, requireAll: false);

    public static Dictionary<string, string[]> Validate(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        ValidateEmail(request.Email, required: true, errors);
        ValidateRequiredString(request.Password, "password", "Password", 128, required: true, errors);

        return errors;
    }

    public static Dictionary<string, string[]> Validate(RefreshTokenRequest request) =>
        ValidateRefreshToken(request.RefreshToken);

    public static Dictionary<string, string[]> Validate(LogoutRequest request) =>
        ValidateRefreshToken(request.RefreshToken);

    private static Dictionary<string, string[]> ValidateRefreshToken(string? refreshToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            errors["refreshToken"] = ["Refresh token is required."];
        }
        else if (refreshToken.Length > 512)
        {
            errors["refreshToken"] = ["Refresh token is invalid."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUser(
        string? email,
        string? name,
        UserRole? role,
        string? password,
        bool requireAll)
    {
        var errors = new Dictionary<string, string[]>();

        ValidateEmail(email, requireAll, errors);
        ValidateRequiredString(name, "name", "Name", 200, requireAll, errors);

        if (requireAll && role is null)
        {
            errors["role"] = ["Role is required."];
        }

        if (password is not null)
        {
            if (password.Length < 8)
            {
                errors["password"] = ["Password must be at least 8 characters."];
            }
            else if (password.Length > 128)
            {
                errors["password"] = ["Password must be 128 characters or fewer."];
            }
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateProduct(
        string? name,
        string? description,
        decimal? price,
        bool requireAll)
    {
        var errors = new Dictionary<string, string[]>();

        ValidateRequiredString(name, "name", "Name", 200, requireAll, errors);

        if (description is null)
        {
            if (requireAll)
            {
                errors["description"] = ["Description is required."];
            }
        }
        else if (description.Trim().Length == 0)
        {
            errors["description"] = ["Description must not be empty."];
        }

        if (requireAll && price is null)
        {
            errors["price"] = ["Price is required."];
        }
        else if (price is < 0 or > 9_999_999_999.99m)
        {
            errors["price"] = ["Price must be between 0 and 9,999,999,999.99."];
        }

        return errors;
    }

    private static void ValidateEmail(
        string? email,
        bool required,
        Dictionary<string, string[]> errors)
    {
        if (email is null)
        {
            if (required)
            {
                errors["email"] = ["Email is required."];
            }

            return;
        }

        var trimmed = email.Trim();

        if (trimmed.Length == 0)
        {
            errors["email"] = ["Email is required."];
        }
        else if (trimmed.Length > 320)
        {
            errors["email"] = ["Email must be 320 characters or fewer."];
        }
        else if (!MailAddress.TryCreate(trimmed, out var address) ||
                 !string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            errors["email"] = ["Email must be a valid email address."];
        }
    }

    private static void ValidateRequiredString(
        string? value,
        string key,
        string displayName,
        int maxLength,
        bool required,
        Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            if (required)
            {
                errors[key] = [$"{displayName} is required."];
            }

            return;
        }

        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            errors[key] = [$"{displayName} is required."];
        }
        else if (trimmed.Length > maxLength)
        {
            errors[key] = [$"{displayName} must be {maxLength} characters or fewer."];
        }
    }
}
