using Ai.Application.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ai.Application;

public interface IAiDb
{
    DbSet<AiClient> Clients { get; }
    DbSet<AiAuditEvent> AuditEvents { get; }
    DbSet<AiRecycledItem> RecycleBin { get; }
    DbSet<AiWriteReceipt> WriteReceipts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
