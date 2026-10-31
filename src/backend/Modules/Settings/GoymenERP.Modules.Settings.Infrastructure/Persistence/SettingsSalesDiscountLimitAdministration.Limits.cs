using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The limit records of a group (SAT-D36, SAT-D62) on one timeline (SAT-D78) and the withdrawal or correction of a record
/// that has not taken effect yet (SAT-D84, <c>SINAN_KARARLARI_2026-10-28_SATIS_EK12.md</c>).
/// <list type="bullet">
/// <item><b>Dates</b>: a record takes effect from its business date (omitted = today's Europe/Istanbul business date) and
/// applies by itself from then on until the next later-dated record starts. A date before today is refused
/// (<c>…_effective_from_past</c>); any other date is placed on the timeline (ordered by date, then version), also before an
/// already scheduled record (review A P2-1). A new record on the same date as a not-yet-effective record REPLACES it: that
/// record is withdrawn in the same save (a correction, audited as such), so no withdrawal can ever bring back a hidden
/// record (review B P2-1).</item>
/// <item><b>Immutability</b>: a record row never changes. ANY record whose date is still ahead can be withdrawn (an
/// insert-only withdrawal row) or corrected (withdrawn and replaced by a new record in the same save; its date is kept
/// unless another is given) — also when later records are scheduled (SAT-D84); a record in force (its date is today or
/// earlier) is refused with <c>…_record_in_effect</c> — a new record is entered instead.</item>
/// <item><b>Links</b>: a linked limit must point at an ACTIVE group of the same company, and the group's records after the
/// change must not close a cycle on any date from the change on (EK10,
/// <see cref="SalesDiscountLimitCalculatorV1.CheckGroupLinks"/>) — a withdrawal can close one too, by extending an earlier
/// linked record. The chains reached are fenced by their rows' versions in the same save, so two concurrent changes cannot
/// close a cycle together.</item>
/// </list>
/// Every change moves the group's version and writes one audit event with the whole reason; a no-op writes nothing.
/// </summary>
internal sealed partial class SettingsSalesDiscountLimitAdministration
{
    private const string LimitRecordCommandName = "settings.sales-discount-limit.record.v1";
    private const string LimitWithdrawCommandName = "settings.sales-discount-limit.withdraw.v1";
    private const string LimitCorrectCommandName = "settings.sales-discount-limit.correct.v1";

    public async Task<SettingsCommandResultV1> SetLimitAsync(Guid groupId, SetSalesDiscountLimitRequestV1 request,
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

        if (!TryReadLimit(request.Kind, request.Percent, request.LinkedGroupId, request.ExtraPoints, out var values,
                out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var change = new LimitChange(LimitRecordCommandName, "limit", groupId, expectedVersion, null,
            values with { EffectiveFrom = request.EffectiveFrom }, reason);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            GroupId = groupId,
            ExpectedVersion = expectedVersion,
            values.Kind,
            values.Percent,
            values.LinkedGroupId,
            values.ExtraPoints,
            Reason = reason,
            request.EffectiveFrom,
        });
        return await ChangeLimitAsync(change, requestHash, companyId, scope, metadata, cancellationToken);
    }

    public async Task<SettingsCommandResultV1> WithdrawLimitAsync(Guid groupId, WithdrawSalesDiscountLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (groupId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion)
            || !TryParseVersion(request.LimitVersion, 1, out var limitVersion))
        {
            return Unstored(Answers.Invalid("Grup, beklenen sürüm ve limit kaydı sürümü (pozitif tam sayı metni) zorunludur."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var change = new LimitChange(LimitWithdrawCommandName, "limit-withdraw", groupId, expectedVersion, limitVersion, null,
            reason);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            GroupId = groupId,
            ExpectedVersion = expectedVersion,
            LimitVersion = limitVersion,
            Reason = reason,
        });
        return await ChangeLimitAsync(change, requestHash, companyId, scope, metadata, cancellationToken);
    }

    public async Task<SettingsCommandResultV1> CorrectLimitAsync(Guid groupId, CorrectSalesDiscountLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (groupId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion)
            || !TryParseVersion(request.LimitVersion, 1, out var limitVersion))
        {
            return Unstored(Answers.Invalid("Grup, beklenen sürüm ve limit kaydı sürümü (pozitif tam sayı metni) zorunludur."),
                metadata);
        }

        if (!TryReadLimit(request.Kind, request.Percent, request.LinkedGroupId, request.ExtraPoints, out var values,
                out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var change = new LimitChange(LimitCorrectCommandName, "limit-correct", groupId, expectedVersion, limitVersion,
            values with { EffectiveFrom = request.EffectiveFrom }, reason);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            GroupId = groupId,
            ExpectedVersion = expectedVersion,
            LimitVersion = limitVersion,
            values.Kind,
            values.Percent,
            values.LinkedGroupId,
            values.ExtraPoints,
            Reason = reason,
            request.EffectiveFrom,
        });
        return await ChangeLimitAsync(change, requestHash, companyId, scope, metadata, cancellationToken);
    }

    /// <summary>A limit command after its input checks: a new record, a withdrawal, or both (a correction).</summary>
    private sealed record LimitChange(
        string CommandName,
        string LogName,
        Guid GroupId,
        long ExpectedVersion,
        long? WithdrawVersion,
        LimitValues? Record,
        string Reason);

    private sealed record LimitValues(string Kind, decimal? Percent, Guid? LinkedGroupId, decimal? ExtraPoints,
        DateOnly? EffectiveFrom);

    private async Task<SettingsCommandResultV1> ChangeLimitAsync(LimitChange change, string requestHash, Guid companyId,
        PermissionScope scope, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var subject = change.GroupId.ToString("D");
        if (await FindExecutionAsync(change.CommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done(change.LogName, metadata, subject, Replay(existing, requestHash, metadata));
        }

        var groups = await database.SalesDiscountLimitGroups.ToDictionaryAsync(group => group.Id, cancellationToken);
        var versions = await database.SalesDiscountLimitVersions.AsNoTracking().ToListAsync(cancellationToken);
        var withdrawals = await database.SalesDiscountLimitWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        var data = SalesDiscountLimitReads.ToGroupData(groups.Values, versions, withdrawals);
        var records = versions.ToDictionary(version => (version.LimitGroupId, version.Version));
        var today = SettingsBusinessDate.Today(timeProvider);
        var row = groups.GetValueOrDefault(change.GroupId);
        SettingsCommandAnswerV1? refusal = row is null ? Answers.NotFound()
            : row.Version != change.ExpectedVersion ? Answers.VersionConflict()
            : !row.IsActive ? Answers.GroupInactive()
            : null;

        // The record a withdrawal or correction is about: this group's, not withdrawn, and not in force yet.
        SalesDiscountLimitVersionDataV1? target = null;
        if (refusal is null && change.WithdrawVersion is { } withdrawVersion)
        {
            target = data[change.GroupId].Versions.FirstOrDefault(version => version.Version == withdrawVersion);
            refusal = target is null ? Answers.NotFound()
                : target.IsWithdrawn
                    ? Answers.Problem(409, SalesDiscountLimitProblemCodesV1.RecordWithdrawn, "Kayıt geri çekilmiş.",
                        "Bu limit kaydı daha önce geri çekildi.")
                : target.EffectiveFrom <= today
                    ? Answers.Problem(409, SalesDiscountLimitProblemCodesV1.RecordInEffect, "Kayıt yürürlükte.",
                        "Yürürlüğe girmiş limit kaydı değiştirilemez veya geri çekilemez; yeni bir limit kaydı girin.")
                : null;
        }

        // The group's records as they would be after the change.
        SalesDiscountLimitVersionDataV1? candidate = null;
        IReadOnlyList<SalesDiscountLimitVersionDataV1> superseded = [];
        IReadOnlyList<SalesDiscountLimitGroupRow> chain = [];
        if (refusal is null)
        {
            var group = data[change.GroupId];
            var remaining = group.Versions
                .Select(version => target is not null && version.Version == target.Version
                    ? version with { IsWithdrawn = true }
                    : version)
                .ToArray();
            if (change.Record is { } values)
            {
                var effectiveFrom = values.EffectiveFrom ?? target?.EffectiveFrom ?? today;
                candidate = new SalesDiscountLimitVersionDataV1(change.GroupId, row!.LimitVersion + 1, Guid.NewGuid(),
                    values.Kind, values.Percent, values.LinkedGroupId, values.ExtraPoints, effectiveFrom);
                refusal = effectiveFrom < today
                    ? Answers.Problem(422, SalesDiscountLimitProblemCodesV1.EffectiveFromPast, "Geçmiş tarih.",
                        "Limit değişikliği bugünden önceki bir tarihten başlatılamaz.")
                    : values.LinkedGroupId is { } linkedId && !groups.ContainsKey(linkedId)
                        ? Answers.Problem(422, SalesDiscountLimitProblemCodesV1.LinkedGroupNotFound, "Bağlı grup bulunamadı.",
                            "Bağlanan limit grubu bu şirkette yok.")
                    : values.LinkedGroupId is { } inactiveId && !groups[inactiveId].IsActive
                        ? Answers.Problem(422, SalesDiscountLimitProblemCodesV1.LinkedGroupInactive, "Bağlı grup pasif.",
                            "Pasif bir limit grubuna yeni bağlantı kurulamaz.")
                    : null;

                // The record in force on the new record's date without it: the same limit there changes nothing (a no-op
                // below). A correction is a no-op only when it keeps the corrected record's value AND date.
                var inForce = SalesDiscountLimitCalculatorV1.RecordInForce(group with { Versions = remaining }, effectiveFrom);
                var unchanged = target is null
                    ? inForce is not null && SameLimit(inForce, candidate)
                    : SameLimit(target, candidate) && target.EffectiveFrom == effectiveFrom;
                if (refusal is null && unchanged)
                {
                    await RequireStillAsync(companyId, cancellationToken);
                    return Done(change.LogName, metadata, subject, await StoreRefusalAsync(change.CommandName,
                        Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
                }

                // SAT-D84 (review B P2-1): a new record on the same date as a not-yet-effective record REPLACES it — that
                // record is withdrawn in the same save — so a later withdrawal can never bring a hidden record back.
                superseded = remaining
                    .Where(version => !version.IsWithdrawn && version.EffectiveFrom == effectiveFrom
                                      && version.EffectiveFrom > today)
                    .ToArray();
                remaining = remaining
                    .Select(version => superseded.Any(replaced => replaced.Version == version.Version)
                        ? version with { IsWithdrawn = true }
                        : version)
                    .Append(candidate)
                    .ToArray();
            }

            if (refusal is null)
            {
                var from = new[] { target?.EffectiveFrom, candidate?.EffectiveFrom }.OfType<DateOnly>().Min();
                var check = SalesDiscountLimitCalculatorV1.CheckGroupLinks(
                    SalesDiscountLimitCalculatorV1.With(data, group with { Versions = remaining }), change.GroupId, from);
                if (check.CreatesCycle)
                {
                    refusal = Answers.Problem(422, SalesDiscountLimitProblemCodesV1.LinkCycle, "Döngüsel bağlantı.",
                        "Bu değişiklik limit grupları arasında döngü oluşturur (örn. A→B→A), bugün veya planlı bir tarihten sonra.");
                }
                else
                {
                    chain = check.Chain.Where(groups.ContainsKey).Select(id => groups[id]).ToArray();
                }
            }
        }

        if (refusal is not null)
        {
            return Done(change.LogName, metadata, subject,
                await StoreRefusalAsync(change.CommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            // The business day turned while the command was checked (review P3-6): a record may have just taken effect.
            // Nothing is written or stored; the editor re-reads and decides again.
            return Done(change.LogName, metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row!.Version;
        var before = data[change.GroupId];
        var replaced = new[] { target }.OfType<SalesDiscountLimitVersionDataV1>().Concat(superseded).ToArray();
        var previous = replaced.Length > 0
            ? records[(change.GroupId, replaced[0].Version)]
            : SalesDiscountLimitCalculatorV1.LatestRecord(before) is { } latestBefore
                ? records[(change.GroupId, latestBefore.Version)]
                : null;
        SalesDiscountLimitVersionRow? record = null;
        if (candidate is not null)
        {
            record = new SalesDiscountLimitVersionRow
            {
                CompanyId = companyId,
                LimitGroupId = change.GroupId,
                Version = candidate.Version,
                ProfileId = candidate.ProfileId,
                Kind = candidate.Kind,
                Percent = candidate.Percent,
                LinkedGroupId = candidate.LinkedGroupId,
                ExtraPoints = candidate.ExtraPoints,
                EffectiveFrom = candidate.EffectiveFrom,
                RecordedAtUtc = now,
                RecordedBy = metadata.ActorId,
                Reason = change.Reason,
            };
            database.SalesDiscountLimitVersions.Add(record);
            row.LimitVersion = record.Version;
        }

        foreach (var withdrawn in replaced)
        {
            database.SalesDiscountLimitWithdrawals.Add(new SalesDiscountLimitWithdrawalRow
            {
                CompanyId = companyId,
                LimitGroupId = change.GroupId,
                LimitVersion = withdrawn.Version,
                ReplacedByVersion = record?.Version,
                WithdrawnAtUtc = now,
                WithdrawnBy = metadata.ActorId,
                Reason = change.Reason,
            });
        }

        Touch(row, metadata, bumpVersion: true);
        foreach (var fenced in chain)
        {
            // Unchanged value, but the UPDATE carries the row's version predicate: a concurrent change of the chain
            // (another link, a withdrawal, a deactivation) makes this save fail instead of committing a cycle.
            database.Entry(fenced).Property(group => group.UpdatedAtUtc).IsModified = true;
        }

        var replacedVersions = replaced.Select(version => version.Version).ToArray();
        var (eventType, operation, after) = (replaced.Length, record) switch
        {
            (0, { } added) => ("settings.sales-discount-limit.recorded.v1", "RecordLimit", LimitSnapshot(row, added, [])),
            (_, null) => ("settings.sales-discount-limit.withdrawn.v1", "WithdrawLimit",
                WithdrawalSnapshot(row, records[(change.GroupId, target!.Version)])),
            (_, { } replacement) => ("settings.sales-discount-limit.corrected.v1", "CorrectLimit",
                LimitSnapshot(row, replacement, replacedVersions)),
        };
        Audit(auditEvents, companyId, eventType, GroupEntityType, subject, operation, versionBefore, row.Version, scope,
            previous is null ? null : LimitSnapshot(row, previous, []), after, change.Reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done(change.LogName, metadata, subject, await SaveChangeAsync(change.CommandName, Answers.NoContent(), keyHash,
            requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    private static bool SameLimit(SalesDiscountLimitVersionDataV1 left, SalesDiscountLimitVersionDataV1 right) =>
        left.Kind == right.Kind && left.Percent == right.Percent && left.LinkedGroupId == right.LinkedGroupId
        && left.ExtraPoints == right.ExtraPoints;

    private static bool TryReadLimit(string? kind, string? percentText, Guid? linkedGroupId, string? extraPointsText,
        out LimitValues values, out string detail)
    {
        values = new LimitValues(kind ?? string.Empty, null, null, null, null);
        detail = string.Empty;
        switch (kind)
        {
            case SalesDiscountLimitKindsV1.Fixed:
                if (linkedGroupId is not null || extraPointsText is not null
                    || !SalesDiscountLimitCalculatorV1.TryParsePercent(percentText, out var fixedPercent))
                {
                    detail = "Sabit limit için yalnız 0-100 arası yüzde (en çok 4 ondalık) girilir.";
                    return false;
                }

                values = new LimitValues(kind, fixedPercent, null, null, null);
                return true;
            case SalesDiscountLimitKindsV1.Linked:
                if (percentText is not null || linkedGroupId is not { } linked || linked == Guid.Empty
                    || !SalesDiscountLimitCalculatorV1.TryParsePercent(extraPointsText, out var extra))
                {
                    detail = "Bağlı limit için bağlı grup ve 0-100 arası ek puan (en çok 4 ondalık) girilir.";
                    return false;
                }

                values = new LimitValues(kind, null, linked, extra, null);
                return true;
            default:
                detail = "Limit türü 'Fixed' veya 'Linked' olmalıdır.";
                return false;
        }
    }

    private static object LimitSnapshot(SalesDiscountLimitGroupRow group, SalesDiscountLimitVersionRow record,
        IReadOnlyList<long> replacesLimitVersions) => new
    {
        GroupId = group.Id,
        group.CompanyId,
        group.Code,
        LimitVersion = record.Version,
        record.ProfileId,
        record.Kind,
        Percent = record.Percent is { } value ? SalesDiscountLimitCalculatorV1.Format(value) : null,
        record.LinkedGroupId,
        ExtraPoints = record.ExtraPoints is { } extra ? SalesDiscountLimitCalculatorV1.Format(extra) : null,
        record.EffectiveFrom,
        ReplacesLimitVersions = replacesLimitVersions,
    };

    private static object WithdrawalSnapshot(SalesDiscountLimitGroupRow group, SalesDiscountLimitVersionRow record) => new
    {
        GroupId = group.Id,
        group.CompanyId,
        group.Code,
        WithdrawnLimitVersion = record.Version,
        record.ProfileId,
        record.EffectiveFrom,
        Withdrawn = true,
    };
}
