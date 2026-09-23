using EventPilot.Application.Users.Dtos;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace EventPilot.Application.Users.Commands.UpdateUser
{
    public sealed record UpdateUserCommand(UpdateUserProfileDto updateUserProfileDto) : IRequest;

}
