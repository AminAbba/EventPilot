using EventPilot.Application.Registrations.Dtos;
using MediatR;

namespace EventPilot.Application.Registrations.Commands.CancelRegistration;

public sealed record CancelRegistrationCommand(Guid EventId) : IRequest<RegistrationResponse>;
