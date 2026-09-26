using Ai.Application.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ai.Application;

public interface IAiDb
{
    DbSet<AiClient> Clients { get; }
    DbSet<AiAuditEvent> AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
