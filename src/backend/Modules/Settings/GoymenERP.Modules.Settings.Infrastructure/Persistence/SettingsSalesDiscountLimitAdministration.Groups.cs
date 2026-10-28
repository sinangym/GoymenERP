using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The group list and the limit records of the server-resolved company (SAT-D36, SAT-D62, SAT-D63). Another company's group
/// id is the same code-less 404 as an unknown one (the company filter never shows it), and as a linked group it is 422
/// <c>settings_discount_limit_linked_group_not_found</c>.
/// </summary>
internal sealed partial class SettingsSalesDiscountLimitAdministration
{
    private const string GroupCreateCommandName = "settings.sales-discount-limit-group.create.v1";
    private const string GroupRenameCommandName = "settings.sales-discount-limit-group.rename.v1";
    private const string GroupDeactivateCommandName = "settings.sales-discount-limit-group.deactivate.v1";

    public async Task<SettingsCommandResultV1> CreateGroupAsync(CreateSalesDiscountLimitGroupRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TryNormalizeCode(request.Code, out var code))
        {
            return Unstored(Answers.Invalid(
                "Kod 1-32 karakter olmalı; yalnız büyük harf (A-Z), rakam, '_' ve '-' içerebilir."), metadata);
        }

        if (!TryNormalizeName(request.Name, out var name))
        {
            return Unstored(Answers.Invalid("Ad 1-100 karakter olmalı ve kontrol karakteri içermemelidir."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { Code = code, Name = name, Reason = reason });
        if (await FindExecutionAsync(GroupCreateCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("create", metadata, code, Replay(existing, requestHash, metadata));
        }

        if (await TakenRefusalAsync(code, name, exceptId: null, cancellationToken) is { } taken)
        {
            return Done("create", metadata, code,
                await StoreRefusalAsync(GroupCreateCommandName, taken, keyHash, requestHash, metadata, cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        var row = new SalesDiscountLimitGroupRow
        {
            CompanyId = companyId,
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            IsActive = true,
            Version = 1,
            LimitVersion = 0,
            CreatedAtUtc = now,
            CreatedBy = metadata.ActorId,
            UpdatedAtUtc = now,
            UpdatedBy = metadata.ActorId,
        };
        database.SalesDiscountLimitGroups.Add(row);
        Audit(auditEvents, companyId, "settings.sales-discount-limit-group.created.v1", GroupEntityType, row.Id.ToString("D"),
            "Create", null, row.Version, scope, null, GroupSnapshot(row), reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("create", metadata, code, await SaveChangeAsync(GroupCreateCommandName, Answers.Created(row.Id), keyHash,
            requestHash, metadata,
            async token => await TakenRefusalAsync(code, name, exceptId: null, token) ?? Answers.VersionConflict(),
            cancellationToken));
    }

    public async Task<SettingsCommandResultV1> RenameGroupAsync(Guid groupId, RenameSalesDiscountLimitGroupRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (groupId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Grup ve beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!TryNormalizeName(request.Name, out var name))
        {
            return Unstored(Answers.Invalid("Ad 1-100 karakter olmalı ve kontrol karakteri içermemelidir."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            GroupId = groupId, ExpectedVersion = expectedVersion, Name = name, Reason = reason,
        });
        if (await FindExecutionAsync(GroupRenameCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rename", metadata, groupId.ToString("D"), Replay(existing, requestHash, metadata));
        }

        var row = await database.SalesDiscountLimitGroups.SingleOrDefaultAsync(group => group.Id == groupId, cancellationToken);
        var refusal = row is null ? Answers.NotFound()
            : row.Version != expectedVersion ? Answers.VersionConflict()
            : !row.IsActive ? Answers.GroupInactive()
            : string.Equals(row.Name, name, StringComparison.Ordinal) ? null
            : await TakenRefusalAsync(null, name, groupId, cancellationToken);
        if (refusal is not null)
        {
            return Done("rename", metadata, groupId.ToString("D"),
                await StoreRefusalAsync(GroupRenameCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (string.Equals(row!.Name, name, StringComparison.Ordinal))
        {
            // No change: no version move and no audit event; the key still records its answer.
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rename", metadata, groupId.ToString("D"), await StoreRefusalAsync(GroupRenameCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var before = GroupSnapshot(row);
        row.Name = name;
        Touch(row, metadata, bumpVersion: true);
        Audit(auditEvents, companyId, "settings.sales-discount-limit-group.renamed.v1", GroupEntityType, groupId.ToString("D"),
            "Rename", expectedVersion, row.Version, scope, before, GroupSnapshot(row), reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("rename", metadata, groupId.ToString("D"), await SaveChangeAsync(GroupRenameCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata,
            async token => await TakenRefusalAsync(null, name, groupId, token) ?? Answers.VersionConflict(),
            cancellationToken));
    }

    public async Task<SettingsCommandResultV1> DeactivateGroupAsync(Guid groupId,
        DeactivateSalesDiscountLimitGroupRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (groupId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Grup ve beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            GroupId = groupId, ExpectedVersion = expectedVersion, Reason = reason,
        });
        if (await FindExecutionAsync(GroupDeactivateCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("deactivate", metadata, groupId.ToString("D"), Replay(existing, requestHash, metadata));
        }

        var row = await database.SalesDiscountLimitGroups.SingleOrDefaultAsync(group => group.Id == groupId, cancellationToken);
        var refusal = row is null ? Answers.NotFound()
            : row.Version != expectedVersion ? Answers.VersionConflict()
            : !row.IsActive ? Answers.GroupInactive()
            : null;
        if (refusal is not null)
        {
            return Done("deactivate", metadata, groupId.ToString("D"),
                await StoreRefusalAsync(GroupDeactivateCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        var before = GroupSnapshot(row!);
        row!.IsActive = false;
        Touch(row, metadata, bumpVersion: true);
        Audit(auditEvents, companyId, "settings.sales-discount-limit-group.deactivated.v1", GroupEntityType,
            groupId.ToString("D"), "Deactivate", expectedVersion, row.Version, scope, before, GroupSnapshot(row), reason,
            metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("deactivate", metadata, groupId.ToString("D"), await SaveChangeAsync(GroupDeactivateCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata,
            _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    private async Task<SettingsCommandAnswerV1?> TakenRefusalAsync(string? code, string name, Guid? exceptId,
        CancellationToken cancellationToken)
    {
        if (code is not null && await database.SalesDiscountLimitGroups.AsNoTracking()
                .AnyAsync(group => group.Code == code, cancellationToken))
        {
            return Answers.Problem(409, SalesDiscountLimitProblemCodesV1.GroupCodeTaken, "Kod kullanılıyor.",
                "Bu şirkette aynı kodlu bir limit grubu var.");
        }

        return await database.SalesDiscountLimitGroups.AsNoTracking()
            .AnyAsync(group => group.Name == name && (exceptId == null || group.Id != exceptId), cancellationToken)
            ? Answers.Problem(409, SalesDiscountLimitProblemCodesV1.GroupNameTaken, "Ad kullanılıyor.",
                "Bu şirkette aynı adlı bir limit grubu var.")
            : null;
    }

    private void Touch(SalesDiscountLimitGroupRow row, SettingsCommandMetadataV1 metadata, bool bumpVersion)
    {
        if (bumpVersion)
        {
            row.Version = checked(row.Version + 1);
        }

        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        row.UpdatedBy = metadata.ActorId;
    }

    private SettingsCommandResultV1 Done(string command, SettingsCommandMetadataV1 metadata, string subject,
        SettingsCommandResultV1 result)
    {
        Log(command, metadata, subject, result);
        return result;
    }

    private static object GroupSnapshot(SalesDiscountLimitGroupRow row) => new
    {
        row.Id,
        row.CompanyId,
        row.Code,
        row.Name,
        row.IsActive,
        Version = row.Version,
        LimitVersion = row.LimitVersion,
    };
}
