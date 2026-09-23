namespace EventPilot.Domain.Entities
{
    public class Event
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Title { get; set; } = default!;
        public string? Description { get; set; }
        public string Location { get; set; } = default!;
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public int Capacity { get; set; }
        public string? ImageUrl { get; set; }
        public decimal Price { get; set; } = 0;
        public EventStatus Status { get; set; }
        public EventCategory Category { get; set; }
        public byte[] RowVersion { get; set; } = default!;
        public int OrganizerUserId { get; set; }
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public bool IsFree => Price == 0;
        public string? MeetingUrl { get; set; }


        #region Relations
        public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
        #endregion

    }

    public enum EventStatus
    {
        Draft,
        Published,
        Cancelled
    }

    public enum EventCategory
    {
        Technology,
        Entrepreneurship,
        ArtsAndCulture,
        TourismAndTravel,
        Management,
        Business,
        TechnicalAndEngineeringAndIndustry,
        Charity,
        ReligiousAndCeremonial,
        Medical,
        Finance,
        PersonalDevelopmentAndFamily,
        EducationAndAcademic,
        Sports,
        Entertainment,
        Humanities,
        BasicSciences,
        Other,
    }
}
