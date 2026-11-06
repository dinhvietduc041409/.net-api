using DotnetApi.Contracts;
using DotnetApi.Data;
using DotnetApi.Infrastructure;
using DotnetApi.Interfaces;
using DotnetApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DotnetApi.Services;

public sealed class UserService(
    AppDbContext db,
    IPasswordHasher<AppUser> passwordHasher) : IUserService
{
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .OrderByDescending(user => user.CreatedAt)
            .ToListAsync(ct);

        return users.Select(user => user.ToResponse()).ToList();
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == id, ct);

        return user is null
            ? throw new NotFoundException("User", id)
            : user.ToResponse();
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var email = request.Email!.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(user => user.Email == email, ct))
        {
            throw new ConflictException("A user with this email already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = request.Name!.Trim(),
            Role = request.Role!.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };

        user.PasswordHash = passwordHasher.HashPassword(
            user,
            request.Password ?? AuthDefaults.DemoPassword);

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            throw new ConflictException("A user with this email already exists.");
        }

        return user.ToResponse();
    }

    public async Task<UserResponse> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(user => user.Id == id, ct);

        if (user is null)
        {
            throw new NotFoundException("User", id);
        }

        if (request.Email is not null)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            if (await db.Users.AnyAsync(other => other.Email == email && other.Id != id, ct))
            {
                throw new ConflictException("A user with this email already exists.");
            }

            user.Email = email;
        }

        if (request.Name is not null)
        {
            user.Name = request.Name.Trim();
        }

        if (request.Role is not null)
        {
            user.Role = request.Role.Value;
        }

        if (request.Password is not null)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            throw new ConflictException("A user with this email already exists.");
        }

        return user.ToResponse();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(user => user.Id == id, ct);

        if (user is null)
        {
            throw new NotFoundException("User", id);
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException postgres ||
            postgres.SqlState != PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }

        // The users table only has one unique index, so an empty constraint
        // name still means the email uniqueness check failed.
        return string.IsNullOrEmpty(postgres.ConstraintName) ||
               string.Equals(postgres.ConstraintName, "ux_users_email", StringComparison.Ordinal);
    }
}
