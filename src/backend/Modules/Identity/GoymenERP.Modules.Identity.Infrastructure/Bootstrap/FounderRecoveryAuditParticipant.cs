using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Identity.Infrastructure.Bootstrap;

internal interface IFounderRecoveryAuditParticipant
{
    Task AppendAsync(IReadOnlyList<FounderRecoveryCompanyAuditV1> records, CancellationToken cancellationToken);
}
/// <summary>The Identity owner supplies only its current transaction; Audit owns every audit table write.</summary>
internal sealed class FounderRecoveryAuditParticipant(IdentityDbContext database, IFounderRecoverySystemAuditV1 owner)
    : IFounderRecoveryAuditParticipant
{
    public Task AppendAsync(IReadOnlyList<FounderRecoveryCompanyAuditV1> records, CancellationToken cancellationToken)
    {
        if (!database.Database.IsRelational() || database.Database.CurrentTransaction is not { } transaction)
            throw new InvalidOperationException("Founder recovery audit requires Identity's active SQL transaction.");
        return owner.AppendAsync(transaction.GetDbTransaction(), records, cancellationToken);
    }
}

internal interface IFounderBootstrapAuditParticipant
{
    Task AppendAsync(IReadOnlyList<FounderBootstrapCompanyAuditV1> records, CancellationToken cancellationToken);
}

/// <summary>The bootstrap twin of <see cref="FounderRecoveryAuditParticipant"/>: Identity lends its transaction, Audit writes.</summary>
internal sealed class FounderBootstrapAuditParticipant(IdentityDbContext database, IFounderBootstrapSystemAuditV1 owner)
    : IFounderBootstrapAuditParticipant
{
    public Task AppendAsync(IReadOnlyList<FounderBootstrapCompanyAuditV1> records, CancellationToken cancellationToken)
    {
        if (!database.Database.IsRelational() || database.Database.CurrentTransaction is not { } transaction)
            throw new InvalidOperationException("Founder bootstrap audit requires Identity's active SQL transaction.");
        return owner.AppendAsync(transaction.GetDbTransaction(), records, cancellationToken);
    }
}
