using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Interfaces;
using EventPilot.Application.Events.Queries;
using EventPilot.Application.Users.Dtos;
using EventPilot.Domain.Entities;
using EventPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventPilot.Tests;

internal sealed class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; } = true;
    public int? UserId { get; init; }
    public string? Email { get; init; }
}

internal sealed class TestIdentityService(int userId) : IIdentityService
{
    public Task<MeDto> GetMeAsync(int? id, CancellationToken ct) => Task.FromResult(new MeDto
    {
        Id = userId,
        Email = $"user{userId}@example.com",
        FirstName = "User",
        LastName = userId.ToString()
    });

    public Task<int> CreateUserAsync(string email, string password, string firstName, string lastName,
        string? avatarUrl, CancellationToken ct) => throw new NotSupportedException();
    public Task LogoutAsync(int userId, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> BecomeOrganizerAsync(int userId, CancellationToken ct) => throw new NotSupportedException();
    public Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct) => throw new NotSupportedException();
    public Task SendPasswordResetLinkAsync(string email, string resetLinkBase, CancellationToken ct) => throw new NotSupportedException();
    public Task ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct) => throw new NotSupportedException();
    public Task UpdateUserInfo(int? userId, UpdateUserProfileDto dto, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class FailingEventRepository : IEventRepository
{
    public Task<PagedResult<EventDto>> ListPublishedAsync(ListEventQuery query, CancellationToken ct) =>
        throw new InvalidOperationException("private-database-diagnostic");

    public Task<Event?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<EventDto?> GetDetailsAsync(Guid id, int? viewerId, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<Guid> CreateEventAsync(Event ev, int currentUserId, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<EventDto>> GetByOrganizerUserId(ListEventQuery query, int? organizerId, CancellationToken ct) => throw new NotSupportedException();
    public Task DeleteEvent(Guid eventId, int? userId, CancellationToken ct) => throw new NotSupportedException();
    public Task UpdateAsync(Guid eventId, int? userId, UpdateEventDto dto, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> GetOrganizerIdByEventId(Guid eventId, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class FailingDbContextFactory : IDbContextFactory<EventPilotDbContext>
{
    public EventPilotDbContext CreateDbContext() => throw new InvalidOperationException("private-sql-secret");
}
