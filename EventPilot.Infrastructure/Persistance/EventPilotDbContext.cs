using EventPilot.Domain.Entities;
using EventPilot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventPilot.Infrastructure.Persistence
{
    public class EventPilotDbContext : IdentityDbContext<ApplicationUser , IdentityRole<int> , int>
    {
        public EventPilotDbContext(DbContextOptions<EventPilotDbContext> options)
            : base(options) { }

        public DbSet<Event> Events => Set<Event>();
        public DbSet<Registration> Registrations => Set<Registration>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // datetime2 does not preserve DateTime.Kind.
            var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(utc);

            modelBuilder.Entity<Event>(e =>
            {
                e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                e.Property(x => x.Title).HasMaxLength(100);
                e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
                e.Property(x => x.Location).HasMaxLength(50);
                e.Property(x => x.Price).HasPrecision(18, 2);
                e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OrganizerUserId).OnDelete(DeleteBehavior.Restrict);
                e.ToTable("Events", table =>
                {
                    table.HasCheckConstraint("CK_Events_Capacity", "[Capacity] > 0");
                    table.HasCheckConstraint("CK_Events_Price", "[Price] >= 0");
                    table.HasCheckConstraint("CK_Events_Dates", "[EndAt] > [StartAt]");
                    table.HasCheckConstraint("CK_Events_Status", "[Status] IN (0, 1, 2)");
                    table.HasCheckConstraint("CK_Events_Category", "[Category] BETWEEN 0 AND 17");
                });
            });
            modelBuilder.Entity<ApplicationUser>(u =>
            {
                u.Property(x => x.FirstName).HasMaxLength(100);
                u.Property(x => x.LastName).HasMaxLength(100);
                u.Property(x => x.PhoneNumber).HasMaxLength(30);
            });

            modelBuilder.Entity<Event>()
                .Property(x => x.RowVersion)
                .IsRowVersion();

            modelBuilder.Entity<Event>()
            .HasQueryFilter(x => !x.IsDeleted);

            modelBuilder.Entity<Registration>(r =>
                {
                    r.ToTable("Registrations");
                    r.HasKey(x => x.Id);

                    r.Property(x => x.RegisteredAt).IsRequired();
                    r.Property(x => x.PaidAmount).HasPrecision(18, 2);

                    r.HasOne(x => x.Event)
                        .WithMany(e => e.Registrations)
                        .HasForeignKey(x => x.EventId)
                        .OnDelete(DeleteBehavior.Cascade);

                    r.HasOne<ApplicationUser>()
                        .WithMany()
                        .HasForeignKey(x => x.UserId)
                        .OnDelete(DeleteBehavior.Restrict);

                    r.HasIndex(x => new { x.EventId, x.UserId }).IsUnique();

                });
        }
    }
}
