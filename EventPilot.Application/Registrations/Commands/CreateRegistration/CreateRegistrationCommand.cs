using MediatR;
using EventPilot.Application.Registrations.Dtos;

namespace EventPilot.Application.Registrations.Commands.CreateRegistration;

public sealed record CreateRegistrationCommand(Guid EventId) : IRequest<RegistrationResponse>;
