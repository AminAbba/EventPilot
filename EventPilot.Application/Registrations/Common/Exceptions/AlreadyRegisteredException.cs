namespace EventPilot.Application.Registrations.Common.Exceptions;

public sealed class AlreadyRegisteredException : Exception
{
    public AlreadyRegisteredException(string message) : base(message) { }
}
