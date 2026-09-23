using EventPilot.Application.Users.Dtos;
using MediatR;

namespace EventPilot.Application.Users.Queries.GetMe;

public sealed record GetMeQuery() : IRequest<MeDto>;
