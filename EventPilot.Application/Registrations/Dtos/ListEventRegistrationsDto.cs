namespace EventPilot.Application.Registrations.Dtos
{
    public sealed class ListEventRegistrationsDto
    {
        public int RegistrationId { get; init; }
        public EventPilot.Domain.Enums.RegistrationStatus Status { get; init; }
        public string FullName { get; init; } = null!;
        public string Email { get; init; } = null!;
        public DateTime RegisteredAt { get; init; }
        public string? PhoneNumber { get; init; }
    }
}
