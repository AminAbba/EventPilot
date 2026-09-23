using EventPilot.Application.Abstractions;
using EventPilot.Infrastructure.Persistence;

namespace EventPilot.Infrastructure.Persistance;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly EventPilotDbContext _db;
    public EfUnitOfWork(EventPilotDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

