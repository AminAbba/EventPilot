using MediatR;

namespace EventPilot.Application.Users.Commands.CreateUser;

public sealed record CreateUserCommand(string Email, string Password, string FirstName , string LastName , string? AvatarUrl) : IRequest<int>;
