using EventPilot.Application.Users.Dtos;
using MediatR;

namespace EventPilot.Application.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;
