using System.Data;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Domain.Entities;
using EventPilot.Domain.Enums;
using EventPilot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventPilot.Infrastructure.Persistence;

public static class DemoDataSeeder
{
    public const string Password = "EventPilot2026!";

    public static async Task SeedAsync(EventPilotDbContext db, UserManager<ApplicationUser> userManager)
    {
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        if (await db.Users.AnyAsync() || await db.Events.IgnoreQueryFilters().AnyAsync()
            || await db.Registrations.AnyAsync())
        {
            Console.WriteLine("Seed skipped: database already contains users, events, or registrations.");
            return;
        }

        string[] firstNames = ["Alex", "Camille", "Jordan", "Sam", "Morgan", "Taylor", "Robin", "Jamie", "Casey", "Avery"];
        string[] lastNames = ["Martin", "Bernard", "Dubois", "Thomas", "Robert", "Richard", "Petit", "Durand", "Leroy", "Moreau"];
        var users = new List<ApplicationUser>();
        for (var i = 0; i < 100; i++)
        {
            var email = i < 10 ? $"organizer{i + 1:00}@example.com" : $"attendee{i - 9:00}@example.com";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstNames[i % firstNames.Length],
                LastName = lastNames[i / firstNames.Length]
            };
            var result = await userManager.CreateAsync(user, Password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
            var roles = await userManager.AddToRolesAsync(user, i < 10
                ? [AppRoles.User, AppRoles.Organizer] : [AppRoles.User]);
            if (!roles.Succeeded)
                throw new InvalidOperationException("Could not assign demo account roles.");
            users.Add(user);
        }

        string[] topics = ["Web Development", "Startup Ideas", "Local Art", "Travel Planning", "Team Leadership", "Small Business", "Engineering", "Community Giving", "Cultural Traditions", "Health Education", "Personal Finance", "Personal Growth", "Learning Together", "Running Club", "Game Night", "World History", "Science Discovery", "Community Meetup"];
        string[] formats = ["Workshop", "Meetup", "Introduction", "Discussion", "Masterclass"];
        string[] cities = ["Paris", "Lyon", "Bordeaux", "Lille", "Nantes", "Toulouse", "Nice", "Strasbourg", "Rennes", "Montpellier"];
        var now = DateTime.UtcNow;
        var registrationCount = 0;
        for (var i = 0; i < 250; i++)
        {
            var category = i % topics.Length;
            var start = now.Date.AddDays(1 + i / 2).AddHours(9 + i % 9);
            var ev = new Event
            {
                Id = Guid.NewGuid(),
                Title = $"{topics[category]} {formats[(i / topics.Length) % formats.Length]} #{i + 1:000}",
                Description = $"Join our {topics[category].ToLowerInvariant()} session for practical activities, friendly discussion, and a chance to meet people with shared interests. All experience levels are welcome.",
                Location = $"{cities[i % cities.Length]} Community Centre, Room {1 + i % 6}",
                StartAt = start,
                EndAt = start.AddHours(1 + i % 4),
                Capacity = 20 + i % 9 * 10,
                Price = i % 3 == 0 ? 0 : 10 + i % 10 * 5,
                Status = EventStatus.Published,
                Category = (EventCategory)category,
                OrganizerUserId = users[i % 10].Id
            };

            // Wrap around without repeating attendees in an event.
            for (var j = 0; j < 5 + i % 11; j++)
            {
                var attendee = users[10 + (i * 7 + j) % 90];
                ev.Registrations.Add(new Registration
                {
                    UserId = attendee.Id,
                    RegisteredAt = now.AddMinutes(-1 - i - j),
                    Status = RegistrationStatus.Confirmed,
                    EmailSnapshot = attendee.Email!,
                    FullNameSnapshot = $"{attendee.FirstName} {attendee.LastName}",
                    IsPaid = ev.Price > 0,
                    PaidAmount = ev.Price
                });
                registrationCount++;
            }
            db.Events.Add(ev);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine($"Seeded 100 users, 250 events by 10 organizers, and {registrationCount} registrations across 90 attendees.");
    }
}
