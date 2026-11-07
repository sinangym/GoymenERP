using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// B-04a: the posting account mapping catalog of the active company. Every write is one transaction and one
/// <c>SaveChanges</c>: the version row(s), the immutable rows, the ADR-011 command attempt and the audit
/// event(s). The active approved account plan is read through the reviewed catalog store's own
/// <see cref="ApprovedAccountingSourceCatalogStoreV1.ReadAsync"/> (no bypass, the store is not changed). It also
/// answers the B-04b read port <see cref="IAccountingPostingAccountMappingResolverV1"/>.
/// </summary>
public sealed class AccountingPostingAccountMappingStoreV1(
    AccountingDbContext dbContext,
    ApprovedAccountingSourceCatalogStoreV1 catalog,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider)
    : IAccountingPostingAccountMappingStoreV1, IAccountingPostingAccountMappingResolverV1
{
    private const int Prepared = (int)AccountingPostingAccountMappingStatusV1.Prepared;
    private const int Active = (int)AccountingPostingAccountMappingStatusV1.Active;
    private const int Superseded = (int)AccountingPostingAccountMappingStatusV1.Superseded;
    private const int CatalogActive = (int)ApprovedAccountingSourceCatalogStatusV1.Active;
    private const string EntityType = "Accounting.PostingAccountMapping";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(
        long mappingVersion,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (mappingVersion <= 0)
        {
            return null;
        }

        var row = await dbContext.PostingAccountMappingVersions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.MappingVersion == mappingVersion, cancellationToken);
        return row is null ? null : await RestoreAsync(row, cancellationToken);
    }

    public async Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        var row = await dbContext.PostingAccountMappingVersions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Status == Active, cancellationToken);
        return row is null ? null : await RestoreAsync(row, cancellationToken);
    }

    public async Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(
        long expectedLatestMappingVersion,
        DateOnly effectiveFrom,
        IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows,
        Guid preparedByUserId,
        DateTimeOffset preparedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        RequireMetadata(metadata);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        if (expectedLatestMappingVersion < 0 || effectiveFrom == default || rows.Count == 0
            || preparedByUserId == Guid.Empty || preparedAtUtc == default || preparedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A validated payload and server preparation provenance are required.",
                nameof(rows));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var latest = await dbContext.PostingAccountMappingVersions
                .Select(candidate => (long?)candidate.MappingVersion)
                .MaxAsync(cancellationToken) ?? 0;
            if (latest != expectedLatestMappingVersion)
            {
                throw new AccountingPostingAccountMappingSequenceConflictException(
                    "expectedLatestMappingVersion", expectedLatestMappingVersion, latest);
            }

            var plan = await ActivePlanAsync(cancellationToken)
                ?? throw new AccountingPostingAccountMappingRefusalException(
                    AccountingPostingAccountMappingCodesV1.AccountPlanUnavailable);
            var companyId = companyContext.CompanyId;
            var mappingVersion = checked(latest + 1);
            var entries = new List<AccountingPostingAccountMappingEntryRow>(rows.Count);
            for (var index = 0; index < rows.Count; index++)
            {
                var draft = rows[index];
                var account = plan.Lookup(new AccountRef(companyId, draft.AccountId))
                    ?? throw new AccountingPostingAccountMappingRefusalException(
                        AccountingPostingAccountMappingCodesV1.AccountUnknown, $"rows[{index}].accountId");
                entries.Add(new AccountingPostingAccountMappingEntryRow
                {
                    CompanyId = companyId,
                    MappingVersion = mappingVersion,
                    LineNumber = index + 1,
                    Purpose = draft.Purpose,
                    MappingKey = draft.MappingKey,
                    ItemType = draft.ItemType,
                    LinePurpose = draft.LinePurpose,
                    IsExport = draft.IsExport,
                    AccountId = draft.AccountId,
                    AccountCode = account.Code,
                    VatRateCode = draft.VatRateCode,
                });
            }

            var row = new AccountingPostingAccountMappingVersionRow
            {
                CompanyId = companyId,
                MappingVersion = mappingVersion,
                EffectiveFrom = effectiveFrom,
                Status = Prepared,
                Version = 1,
                AccountPlanSourceVersion = plan.SourceVersion,
                AccountPlanSourceSha256 = plan.SourceSha256.ToUpperInvariant(),
                PreparedByUserId = preparedByUserId,
                PreparedAtUtc = preparedAtUtc,
                CreatedAtUtc = preparedAtUtc,
                UpdatedAtUtc = preparedAtUtc,
            };
            dbContext.PostingAccountMappingVersions.Add(row);
            dbContext.PostingAccountMappingRows.AddRange(entries);
            await freshAuthorityFence(cancellationToken);
            var record = Restore(row, entries);
            Audit(metadata, row, "accounting.posting-account-mapping.prepared.v1", "Prepare", null, row.Version,
                "{}", AuditJson(record), "Muhasebe hesap eslemesi degistirilemez surum olarak hazirlandi");
            Complete(metadata, record);
            await SaveAsync(cancellationToken);
            await freshAuthorityFence(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return record;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(
        long mappingVersion,
        long expectedVersion,
        long expectedActiveMappingVersion,
        Guid activatedByUserId,
        DateTimeOffset activatedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        RequireMetadata(metadata);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        if (mappingVersion <= 0 || expectedVersion <= 0 || expectedActiveMappingVersion < 0
            || activatedByUserId == Guid.Empty || activatedAtUtc == default || activatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Version, expectations and server activation provenance are required.",
                nameof(mappingVersion));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await dbContext.PostingAccountMappingVersions
                .SingleOrDefaultAsync(candidate => candidate.MappingVersion == mappingVersion, cancellationToken)
                ?? throw new AccountingPostingAccountMappingRefusalException(
                    AccountingPostingAccountMappingCodesV1.NotFound);
            if (row.Version != expectedVersion)
            {
                throw new AccountingVersionConflictException(expectedVersion, row.Version);
            }

            if (row.Status != Prepared)
            {
                throw new AccountingPostingAccountMappingRefusalException(
                    AccountingPostingAccountMappingCodesV1.NotPrepared);
            }

            if (row.PreparedByUserId == activatedByUserId)
            {
                throw new AccountingPostingAccountMappingRefusalException(
                    AccountingPostingAccountMappingCodesV1.SelfActivation, "activation");
            }

            var active = await dbContext.PostingAccountMappingVersions
                .SingleOrDefaultAsync(candidate => candidate.Status == Active, cancellationToken);
            var currentActive = active?.MappingVersion ?? 0;
            if (currentActive != expectedActiveMappingVersion)
            {
                throw new AccountingPostingAccountMappingSequenceConflictException(
                    "expectedActiveMappingVersion", expectedActiveMappingVersion, currentActive);
            }

            var entries = await EntriesAsync(row.MappingVersion, cancellationToken);
            var plan = await ActivePlanAsync(cancellationToken)
                ?? throw new AccountingPostingAccountMappingRefusalException(
                    AccountingPostingAccountMappingCodesV1.AccountPlanUnavailable);
            for (var index = 0; index < entries.Count; index++)
            {
                if (plan.Lookup(new AccountRef(row.CompanyId, entries[index].AccountId)) is null)
                {
                    throw new AccountingPostingAccountMappingRefusalException(
                        AccountingPostingAccountMappingCodesV1.AccountNotInActivePlan, $"rows[{index}].accountId");
                }
            }

            await freshAuthorityFence(cancellationToken);
            if (active is not null)
            {
                var activeEntries = await EntriesAsync(active.MappingVersion, cancellationToken);
                var activeBefore = AuditJson(Restore(active, activeEntries));
                var activeVersionBefore = active.Version;
                active.Status = Superseded;
                active.Version = checked(active.Version + 1);
                active.UpdatedAtUtc = activatedAtUtc;
                Audit(metadata, active, "accounting.posting-account-mapping.superseded.v1", "Supersede",
                    activeVersionBefore, active.Version, activeBefore, AuditJson(Restore(active, activeEntries)),
                    "Muhasebe hesap eslemesi yeni etkin surumle yururlukten kalkti");
            }

            var before = AuditJson(Restore(row, entries));
            var versionBefore = row.Version;
            row.Status = Active;
            row.ActivatedByUserId = activatedByUserId;
            row.ActivatedAtUtc = activatedAtUtc;
            row.Version = checked(row.Version + 1);
            row.UpdatedAtUtc = activatedAtUtc;
            var record = Restore(row, entries);
            Audit(metadata, row, "accounting.posting-account-mapping.activated.v1", "Activate",
                versionBefore, row.Version, before, AuditJson(record),
                "Hazirlanan muhasebe hesap eslemesi baska bir yetkili tarafindan etkinlestirildi",
                JsonSerializer.Serialize(new
                {
                    row.PreparedByUserId,
                    activatedByUserId,
                    supersededMappingVersion = active?.MappingVersion,
                }, Json));
            Complete(metadata, record);
            await SaveAsync(cancellationToken);
            await freshAuthorityFence(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return record;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AccountingPostingAccountMappingResolutionV1> ResolveAsync(
        Guid companyId,
        string purpose,
        DateOnly businessDate,
        string mappingKey,
        string? itemType = null,
        string? linePurpose = null,
        bool? isExport = null,
        string? vatRateCode = null,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || companyId != companyContext.CompanyId)
        {
            return AccountingPostingAccountMappingResolutionV1.Unavailable(
                AccountingPostingAccountMappingUnavailableReasonsV1.CompanyContextInvalid);
        }

        if (businessDate == default
            || !AccountingPostingAccountMappingKeysV1.IsAllowed(purpose, mappingKey)
            || !AccountingPostingAccountMappingPayloadV1.IsSelector(itemType)
            || !AccountingPostingAccountMappingPayloadV1.IsSelector(linePurpose)
            || !AccountingPostingAccountMappingPayloadV1.IsSelector(vatRateCode))
        {
            return AccountingPostingAccountMappingResolutionV1.Unavailable(
                AccountingPostingAccountMappingUnavailableReasonsV1.QueryInvalid);
        }

        var active = await dbContext.PostingAccountMappingVersions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Status == Active, cancellationToken);
        if (active is null)
        {
            return AccountingPostingAccountMappingResolutionV1.Unavailable(
                AccountingPostingAccountMappingUnavailableReasonsV1.NoActiveMapping);
        }

        if (businessDate < active.EffectiveFrom)
        {
            return AccountingPostingAccountMappingResolutionV1.Unavailable(
                AccountingPostingAccountMappingUnavailableReasonsV1.NotEffectiveForDate, active.MappingVersion);
        }

        var candidates = await dbContext.PostingAccountMappingRows.AsNoTracking()
            .Where(candidate => candidate.MappingVersion == active.MappingVersion
                && candidate.Purpose == purpose && candidate.MappingKey == mappingKey)
            .ToListAsync(cancellationToken);
        var (kind, entry) = AccountingPostingAccountMappingMatcherV1.Match(
            candidates.Select(Entry), purpose, mappingKey, itemType, linePurpose, isExport, vatRateCode);
        switch (kind)
        {
            case AccountingPostingAccountMappingMatchKindV1.Unmapped:
                return AccountingPostingAccountMappingResolutionV1.Unmapped(active.MappingVersion);
            case AccountingPostingAccountMappingMatchKindV1.Ambiguous:
                return AccountingPostingAccountMappingResolutionV1.Ambiguous(active.MappingVersion);
        }

        var plan = await ActivePlanAsync(cancellationToken);
        if (plan is null)
        {
            return AccountingPostingAccountMappingResolutionV1.Unavailable(
                AccountingPostingAccountMappingUnavailableReasonsV1.AccountPlanUnavailable, active.MappingVersion);
        }

        return plan.Lookup(new AccountRef(companyId, entry!.AccountId)) is null
            ? AccountingPostingAccountMappingResolutionV1.Unavailable(
                AccountingPostingAccountMappingUnavailableReasonsV1.AccountNotInActivePlan, active.MappingVersion)
            : AccountingPostingAccountMappingResolutionV1.Mapped(entry.AccountId, active.MappingVersion);
    }

    private async Task<AccountPlanMirrorV1?> ActivePlanAsync(CancellationToken cancellationToken)
    {
        var activeCatalogId = await dbContext.ApprovedSourceCatalogs.AsNoTracking()
            .Where(candidate => candidate.Status == CatalogActive)
            .Select(candidate => (Guid?)candidate.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (activeCatalogId is not { } id)
        {
            return null;
        }

        var mirror = (await catalog.ReadAsync(id, cancellationToken))?.Package.Mirror;
        return mirror is not null && mirror.CompanyId == companyContext.CompanyId ? mirror : null;
    }

    private async Task<List<AccountingPostingAccountMappingEntryRow>> EntriesAsync(
        long mappingVersion,
        CancellationToken cancellationToken) =>
        await dbContext.PostingAccountMappingRows.AsNoTracking()
            .Where(candidate => candidate.MappingVersion == mappingVersion)
            .OrderBy(candidate => candidate.LineNumber)
            .ToListAsync(cancellationToken);

    private async Task<AccountingPostingAccountMappingRecordV1> RestoreAsync(
        AccountingPostingAccountMappingVersionRow row,
        CancellationToken cancellationToken) =>
        Restore(row, await EntriesAsync(row.MappingVersion, cancellationToken));

    private static AccountingPostingAccountMappingRecordV1 Restore(
        AccountingPostingAccountMappingVersionRow row,
        IReadOnlyList<AccountingPostingAccountMappingEntryRow> entries) =>
        new AccountingPostingAccountMappingRecordV1(
            row.CompanyId,
            row.MappingVersion,
            row.EffectiveFrom,
            (AccountingPostingAccountMappingStatusV1)row.Status,
            row.Version,
            row.AccountPlanSourceVersion,
            row.AccountPlanSourceSha256,
            row.PreparedByUserId,
            row.PreparedAtUtc,
            row.ActivatedByUserId,
            row.ActivatedAtUtc,
            [.. entries.OrderBy(entry => entry.LineNumber).Select(Entry)]);

    private static AccountingPostingAccountMappingEntryV1 Entry(AccountingPostingAccountMappingEntryRow row) =>
        new AccountingPostingAccountMappingEntryV1(row.LineNumber, row.Purpose, row.MappingKey, row.ItemType, row.LinePurpose, row.IsExport, row.AccountId,
            row.AccountCode, row.VatRateCode);

    private void Complete(AccountingCommandMetadata metadata, AccountingPostingAccountMappingRecordV1 record)
    {
        var completion = metadata.Completion?.Invoke(record)
            ?? throw new InvalidOperationException("Mapping writes require an atomic command completion.");
        dbContext.Commands.Add(new AccountingCommandRow
        {
            CompanyId = companyContext.CompanyId,
            CommandName = completion.CommandName,
            IdempotencyKeyHash = completion.IdempotencyKeyHash,
            ActorId = completion.ActorId,
            PayloadHash = completion.RequestHash,
            ResultJson = completion.ResultJson,
            CorrelationId = completion.OriginalCorrelationId,
            RecordedAtUtc = timeProvider.GetUtcNow(),
        });
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange,
                "Another mapping write changed the same version first; nothing was stored.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange,
                "A mapping version or the single active version was won by another write; nothing was stored.",
                exception);
        }
    }

    private void Audit(
        AccountingCommandMetadata metadata,
        AccountingPostingAccountMappingVersionRow row,
        string eventType,
        string operation,
        long? versionBefore,
        long versionAfter,
        string before,
        string after,
        string reason,
        string? approvalContext = null) =>
        auditEvents.Enqueue(new AuditEventDraft(
            row.CompanyId,
            eventType,
            EntityType,
            row.MappingVersion.ToString(CultureInfo.InvariantCulture),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            before,
            after,
            reason,
            approvalContext,
            AuditAiInvolvement.None,
            null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

    private void RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch,
                "Posting account mappings require the active company context.");
        }
    }

    private static void RequireMetadata(AccountingCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.RoleCodes is null)
        {
            throw new ArgumentException("Mapping writes require actor, permission, correlation and idempotency metadata.",
                nameof(metadata));
        }
    }

    private static string AuditJson(AccountingPostingAccountMappingRecordV1 record) =>
        JsonSerializer.Serialize(new
        {
            record.MappingVersion,
            effectiveFrom = record.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            status = record.Status.ToString(),
            record.Version,
            record.AccountPlanSourceVersion,
            record.AccountPlanSourceSha256,
            record.PreparedByUserId,
            record.PreparedAtUtc,
            record.ActivatedByUserId,
            record.ActivatedAtUtc,
            rows = record.Rows.Select(entry => new
            {
                entry.LineNumber,
                entry.Purpose,
                entry.MappingKey,
                entry.ItemType,
                entry.LinePurpose,
                entry.IsExport,
                entry.AccountId,
                entry.AccountCode,
                entry.VatRateCode,
            }),
        }, Json);
}
