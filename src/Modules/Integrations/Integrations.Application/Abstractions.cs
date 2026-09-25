using Integrations.Application.Connections;
using Integrations.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Integrations.Application;

public interface IIntegrationsDb
{
    DbSet<BrokerConnection> Connections { get; }
    DbSet<SyncJob> SyncJobs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Encrypts broker credentials at rest. Plain credentials exist only in memory during a sync.</summary>
public interface ICredentialProtector
{
    string Protect(IReadOnlyDictionary<string, string> credentials);

    IReadOnlyDictionary<string, string> Unprotect(string protectedCredentials);
}

public interface IInvestmentProviderFactory
{
    IReadOnlySet<BrokerKind> Available { get; }

    IInvestmentProvider Create(BrokerKind kind, IReadOnlyDictionary<string, string> credentials);
}
