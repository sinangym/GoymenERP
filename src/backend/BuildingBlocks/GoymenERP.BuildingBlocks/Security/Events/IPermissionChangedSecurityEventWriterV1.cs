using System.Data;

namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>
/// ADR-SEC-001 §4 (No 134): <see cref="SecurityEventTypesV1.PermissionChanged"/> is fail-closed and is written in the SAME SQL
/// transaction as the owner's grant (same database, the <c>security</c> schema). SecurityEvents owns the append and borrows the
/// caller's open transaction without committing it: the grant and its security event commit together or neither stays. Only a
/// valid company-scoped <see cref="SecurityEventTypesV1.PermissionChanged"/> envelope is accepted; any refusal or store failure
/// throws, so the caller's transaction is rolled back (never a grant without its event, never an event without its grant).
/// </summary>
public interface IPermissionChangedSecurityEventWriterV1
{
    Task AppendAsync(IDbTransaction transaction, SecurityEventV1 securityEvent, CancellationToken cancellationToken = default);
}
