using DotnetApi.Contracts;

namespace DotnetApi.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct);

    Task<UserResponse> GetAsync(Guid id, CancellationToken ct);

    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct);

    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
