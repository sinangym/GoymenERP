using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed class ApprovedAccountingSourceCatalogStoreV1(
    AccountingDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider)
    : IApprovedAccountingSourceCatalogStoreV1
{
    private const int Prepared = (int)ApprovedAccountingSourceCatalogStatusV1.Prepared;
    private const int Active = (int)ApprovedAccountingSourceCatalogStatusV1.Active;
    private const int Superseded = (int)ApprovedAccountingSourceCatalogStatusV1.Superseded;
    private const string EntityType = "Accounting.ApprovedSourceCatalog";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (packageId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.ApprovedSourceCatalogs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == packageId, cancellationToken);
        return row is null ? null : Restore(row);
    }

    public async Task<(ApprovedAccountingSourceCatalogRecordV1 Record, AccountingWriteOutcome Outcome)> PrepareAsync(
        Guid packageId,
        ApprovedAccountingSourcePackagePreviewV1 preview,
        Guid preparedByUserId,
        DateTimeOffset preparedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        RequireMetadata(metadata);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        if (packageId == Guid.Empty || preparedByUserId == Guid.Empty || preparedAtUtc == default
            || preparedAtUtc.Offset != TimeSpan.Zero || !preview.IsValid || preview.Package is null
            || preview.Package.Mirror.CompanyId != companyContext.CompanyId)
        {
            throw new ArgumentException("A valid parsed package and server preparation provenance are required.",
                nameof(preview));
        }

        if (await dbContext.ApprovedSourceCatalogs
                .SingleOrDefaultAsync(candidate => candidate.Id == packageId, cancellationToken) is { } existing)
        {
            if (existing.PreparedByUserId != preparedByUserId
                || !string.Equals(existing.AccountPlanSourceVersion,
                    preview.Package.Mirror.SourceVersion, StringComparison.Ordinal)
                || !string.Equals(existing.AccountsSourceSha256, preview.AccountsSourceSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existing.PostingMappingsSourceSha256,
                    preview.PostingMappingsSourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Refused(ApprovedAccountingSourceCatalogCodesV1.ContentConflict,
                    "The package identity is already bound to other immutable content.");
            }

            await freshAuthorityFence(cancellationToken);
            var replay = Restore(existing);
            Complete(metadata, replay);
            await SaveAsync(cancellationToken);
            return (replay, AccountingWriteOutcome.Replayed);
        }

        var snapshot = PackageSnapshot.From(preview.Package);
        var row = new ApprovedAccountingSourceCatalogRow
        {
            Id = packageId,
            CompanyId = companyContext.CompanyId,
            Status = Prepared,
            Version = 1,
            CatalogVersion = null,
            TemplateVersion = preview.TemplateVersion,
            AccountPlanSourceVersion = preview.Package.Mirror.SourceVersion,
            AccountsSourceSha256 = preview.AccountsSourceSha256.ToUpperInvariant(),
            PostingMappingsSourceSha256 = preview.PostingMappingsSourceSha256.ToUpperInvariant(),
            PackageJson = JsonSerializer.Serialize(snapshot, Json),
            PreparedByUserId = preparedByUserId,
            PreparedAtUtc = preparedAtUtc,
            CreatedAtUtc = preparedAtUtc,
            UpdatedAtUtc = preparedAtUtc,
        };
        dbContext.ApprovedSourceCatalogs.Add(row);
        await freshAuthorityFence(cancellationToken);
        var record = Restore(row);
        Audit(metadata, row, "accounting.approved-source-catalog.prepared.v1", "Prepare", null, 1,
            "{}", AuditJson(record), "Onayli hesap plani ve esleme paketi degistirilemez surum olarak hazirlandi");
        Complete(metadata, record);
        await SaveAsync(cancellationToken);
        return (record, AccountingWriteOutcome.Recorded);
    }

    public async Task<ApprovedAccountingSourceCatalogRecordV1> ActivateAsync(
        Guid packageId,
        long expectedPackageVersion,
        long expectedActiveCatalogVersion,
        Guid activatedByUserId,
        DateTimeOffset activatedAtUtc,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        RequireMetadata(metadata);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        if (packageId == Guid.Empty || expectedPackageVersion <= 0 || expectedActiveCatalogVersion < 0
            || activatedByUserId == Guid.Empty || activatedAtUtc == default
            || activatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Package, versions and server activation provenance are required.",
                nameof(packageId));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await dbContext.ApprovedSourceCatalogs
                .SingleOrDefaultAsync(candidate => candidate.Id == packageId, cancellationToken)
                ?? throw Refused(ApprovedAccountingSourceCatalogCodesV1.NotFound,
                    "This company has no such prepared Accounting source package.");
            if (row.Version != expectedPackageVersion)
            {
                throw new AccountingVersionConflictException(expectedPackageVersion, row.Version);
            }

            if (row.Status != Prepared)
            {
                throw Refused(ApprovedAccountingSourceCatalogCodesV1.NotPrepared,
                    "Only an immutable prepared package can be activated.");
            }

            var active = await dbContext.ApprovedSourceCatalogs
                .SingleOrDefaultAsync(candidate => candidate.Status == Active, cancellationToken);
            var currentCatalogVersion = active?.CatalogVersion ?? 0;
            if (currentCatalogVersion != expectedActiveCatalogVersion)
            {
                throw new ApprovedAccountingSourceCatalogVersionConflictException(
                    expectedActiveCatalogVersion, currentCatalogVersion);
            }

            await freshAuthorityFence(cancellationToken);
            if (active is not null)
            {
                active.Status = Superseded;
                active.Version = checked(active.Version + 1);
                active.UpdatedAtUtc = activatedAtUtc;
            }

            var before = AuditJson(Restore(row));
            var versionBefore = row.Version;
            row.Status = Active;
            row.CatalogVersion = checked(currentCatalogVersion + 1);
            row.ActivatedByUserId = activatedByUserId;
            row.ActivatedAtUtc = activatedAtUtc;
            row.Version = checked(row.Version + 1);
            row.UpdatedAtUtc = activatedAtUtc;
            var record = Restore(row);
            Audit(metadata, row, "accounting.approved-source-catalog.activated.v1", "Activate",
                versionBefore, row.Version, before, AuditJson(record),
                "Hazirlanan hesap plani ve esleme paketi ayri katalog yetkisiyle kullanima acildi",
                JsonSerializer.Serialize(new
                {
                    row.PreparedByUserId,
                    activatedByUserId,
                    selfActivation = row.PreparedByUserId == activatedByUserId,
                    row.CatalogVersion,
                }, Json));
            Complete(metadata, record);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return record;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<NormalJournalPostingRuleV1?> ResolveAsync(
        Guid companyId,
        Guid sourceId,
        string sourceKind,
        string sourceVersion,
        string ownerProfileId,
        string ownerProfileVersion,
        Guid currencyId,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (companyId != companyContext.CompanyId || sourceId == Guid.Empty || currencyId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.ApprovedSourceCatalogs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Status == Active, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var package = Restore(row).Package;
        return await new ApprovedNormalJournalSourceBindingCatalogV1(package.Bindings).ResolveAsync(
            companyId,
            sourceId,
            sourceKind,
            sourceVersion,
            ownerProfileId,
            ownerProfileVersion,
            currencyId,
            cancellationToken);
    }

    private ApprovedAccountingSourceCatalogRecordV1 Restore(ApprovedAccountingSourceCatalogRow row)
    {
        var snapshot = JsonSerializer.Deserialize<PackageSnapshot>(row.PackageJson, Json)
            ?? throw new InvalidOperationException("Stored Accounting source catalog snapshot is invalid.");
        var approvedBy = row.ActivatedByUserId ?? row.PreparedByUserId;
        var approvedAt = row.ActivatedAtUtc ?? row.PreparedAtUtc;
        return new ApprovedAccountingSourceCatalogRecordV1(
            row.Id,
            row.CompanyId,
            (ApprovedAccountingSourceCatalogStatusV1)row.Status,
            row.Version,
            row.CatalogVersion,
            row.TemplateVersion,
            row.AccountPlanSourceVersion,
            row.AccountsSourceSha256,
            row.PostingMappingsSourceSha256,
            row.PreparedByUserId,
            row.PreparedAtUtc,
            row.ActivatedByUserId,
            row.ActivatedAtUtc,
            snapshot.ToPackage(row.CompanyId, approvedBy, approvedAt));
    }

    private void Complete(AccountingCommandMetadata metadata, ApprovedAccountingSourceCatalogRecordV1 record)
    {
        var completion = metadata.Completion?.Invoke(record)
            ?? throw new InvalidOperationException("Catalog writes require an atomic command completion.");
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
            throw Refused(AccountingStoreCodes.ConcurrentChange,
                "Another catalog write changed the same version first; nothing was stored.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw Refused(AccountingStoreCodes.ConcurrentChange,
                "A durable catalog identity was won by another write; nothing was stored.", exception);
        }
    }

    private void Audit(
        AccountingCommandMetadata metadata,
        ApprovedAccountingSourceCatalogRow row,
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
            row.Id.ToString("D"),
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
            throw Refused(AccountingStoreCodes.CompanyContextMismatch,
                "Accounting source catalogs require the active company context.");
        }
    }

    private static void RequireMetadata(AccountingCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.RoleCodes is null)
        {
            throw new ArgumentException("Catalog writes require actor, permission, correlation and idempotency metadata.",
                nameof(metadata));
        }
    }

    private static AccountingStoreException Refused(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private static string AuditJson(ApprovedAccountingSourceCatalogRecordV1 record) =>
        JsonSerializer.Serialize(new
        {
            record.PackageId,
            record.Status,
            record.Version,
            record.CatalogVersion,
            record.TemplateVersion,
            record.AccountPlanSourceVersion,
            record.AccountsSourceSha256,
            record.PostingMappingsSourceSha256,
            record.PreparedByUserId,
            record.PreparedAtUtc,
            record.ActivatedByUserId,
            record.ActivatedAtUtc,
            accounts = record.Package.Mirror.Rows.Count,
            bindings = record.Package.Bindings.Count,
        }, Json);

    private sealed record AccountSnapshot(Guid AccountId, string Code, string Name);

    private sealed record MappingSnapshot(string MappingKey, string Side, Guid AccountId);

    private sealed record BindingSnapshot(
        Guid SourceId,
        string SourceKind,
        string SourceVersion,
        string OwnerProfileId,
        string OwnerProfileVersion,
        Guid CurrencyId,
        string PostingPurpose,
        string PostingRuleVersion,
        IReadOnlyList<MappingSnapshot> Mappings);

    private sealed record PackageSnapshot(
        string AccountPlanSourceVersion,
        string AccountPlanSourceSha256,
        IReadOnlyList<AccountSnapshot> Accounts,
        IReadOnlyList<BindingSnapshot> Bindings)
    {
        public static PackageSnapshot From(ApprovedAccountingSourcePackageV1 package) =>
            new(
                package.Mirror.SourceVersion,
                package.Mirror.SourceSha256.ToUpperInvariant(),
                [.. package.Mirror.Rows.Select(row => new AccountSnapshot(
                    row.Account.AccountId, row.Code, row.Name))],
                [.. package.Bindings.Select(binding => new BindingSnapshot(
                    binding.SourceId,
                    binding.SourceKind,
                    binding.SourceVersion,
                    binding.OwnerProfileId,
                    binding.OwnerProfileVersion,
                    binding.CurrencyId,
                    binding.Rule.PostingPurpose,
                    binding.Rule.Version,
                    [.. binding.Rule.Mappings.Select(mapping => new MappingSnapshot(
                        mapping.MappingKey,
                        mapping.Side.ToString(),
                        mapping.Account.AccountId))]))]);

        public ApprovedAccountingSourcePackageV1 ToPackage(
            Guid companyId,
            Guid approvedByUserId,
            DateTimeOffset approvedAtUtc)
        {
            var mirror = new AccountPlanMirrorV1(
                companyId,
                AccountPlanSourceVersion,
                AccountPlanSourceSha256,
                approvedByUserId,
                approvedAtUtc,
                Accounts.Select(account => new AccountPlanMirrorRowV1(
                    new AccountRef(companyId, account.AccountId), account.Code, account.Name)));
            var bindings = Bindings.Select(binding =>
            {
                var mappings = binding.Mappings.Select(mapping => new NormalJournalMappingV1(
                    mapping.MappingKey,
                    Enum.Parse<PostingSide>(mapping.Side, ignoreCase: false),
                    new AccountRef(companyId, mapping.AccountId)));
                var rule = new NormalJournalPostingRuleV1(
                    mirror,
                    binding.PostingPurpose,
                    binding.PostingRuleVersion,
                    approvedByUserId,
                    approvedAtUtc,
                    mappings);
                return new ApprovedNormalJournalSourceBindingV1(
                    companyId,
                    binding.SourceId,
                    binding.SourceKind,
                    binding.SourceVersion,
                    binding.OwnerProfileId,
                    binding.OwnerProfileVersion,
                    binding.CurrencyId,
                    rule);
            }).ToArray();
            return new ApprovedAccountingSourcePackageV1(mirror, bindings);
        }
    }
}
