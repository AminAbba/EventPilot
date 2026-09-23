using MediatR;

namespace EventPilot.Application.Auth.Commands.ResetPassword;
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : IRequest;

