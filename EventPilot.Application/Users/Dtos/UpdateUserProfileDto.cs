namespace EventPilot.Application.Users.Dtos
{
    public record UpdateUserProfileDto
    {
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? PhoneNumber { get; init; }
    }
}
