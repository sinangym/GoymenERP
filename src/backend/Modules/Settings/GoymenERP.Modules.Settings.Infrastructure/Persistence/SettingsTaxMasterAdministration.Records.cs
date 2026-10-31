using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The per-item-type default records (SAT-D139) and the code list versions (EK13 UBL-TR lists) on their timelines. A record or
/// list version row never changes; a withdrawal is an insert-only row. Every change moves the owning series' (or list's)
/// version, which fences concurrent changes of the same series.
/// </summary>
internal sealed partial class SettingsTaxMasterAdministration
{
    private const string DefaultRecordCommandName = "settings.tax-item-type-default.record.v1";
    private const string DefaultWithdrawCommandName = "settings.tax-item-type-default.withdraw.v1";
    private const string CodeListPublishCommandName = "settings.tax-code-list.publish.v1";
    private const string CodeListWithdrawCommandName = "settings.tax-code-list.withdraw.v1";

    public async Task<TaxMasterCommandResultV1> RecordItemTypeDefaultAsync(string itemType, RecordItemTypeDefaultRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TaxItemTypesV1.IsKnown(itemType) || !TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion)
            || request.VatApplies is not { } vatApplies || request.WithholdingApplies is not { } withholdingApplies)
        {
            return Unstored(Answers.Invalid(
                "Kalem tipi (Steel/Scrap/Service), beklenen sürüm, KDV ve tevkifat uygulanıp uygulanmadığı zorunludur."), metadata);
        }

        if (!TryReadDefault(vatApplies, request.VatPercent, request.ExemptionCode, withholdingApplies, request.WithholdingPercent,
                request.WithholdingCode, out var values, out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = itemType;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ItemType = itemType,
            ExpectedVersion = expectedVersion,
            values.VatApplies,
            values.VatPercent,
            values.ExemptionCode,
            values.WithholdingApplies,
            values.WithholdingPercent,
            values.WithholdingCode,
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(DefaultRecordCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("default-record", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await TaxMasterRows.ReadAsync(database, track: true, cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var series = rows.Series.SingleOrDefault(row => row.ItemType == itemType);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = (series?.Version ?? 0) != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : values.ExemptionCode is { } exemption && !TaxMasterCalculatorV1.IsListed(data, TaxCodeListKindsV1.Exemption, exemption,
                effectiveFrom)
                ? Answers.Problem(422, TaxMasterProblemCodesV1.ExemptionCodeUnknown, "İstisna kodu listede yok.",
                    "İstisna kodu, kaydın başladığı gün yürürlükteki istisna kod listesinde bulunmalıdır.")
            : values.WithholdingCode is { } withholding && !TaxMasterCalculatorV1.IsListed(data, TaxCodeListKindsV1.Withholding,
                withholding, effectiveFrom)
                ? Answers.Problem(422, TaxMasterProblemCodesV1.WithholdingCodeUnknown, "Tevkifat kodu listede yok.",
                    "Tevkifat kodu, kaydın başladığı gün yürürlükteki tevkifat kod listesinde bulunmalıdır.")
            : null;
        if (refusal is not null)
        {
            return Done("default-record", metadata, subject,
                await StoreRefusalAsync(DefaultRecordCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        // The same default already in force on the date (catalog or record) changes nothing (no record, version or audit).
        if (TaxMasterCalculatorV1.DefaultInForce(data, itemType, effectiveFrom) is { } inForce && SameDefault(inForce, values))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("default-record", metadata, subject, await StoreRefusalAsync(DefaultRecordCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            // The business day turned while the command was checked: nothing is written; the editor re-reads.
            return Done("default-record", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = series?.Version;
        var previous = TaxMasterCalculatorV1.DefaultInForce(data, itemType, effectiveFrom);
        if (series is null)
        {
            series = new TaxItemTypeDefaultSeriesRow
            {
                CompanyId = companyId, ItemType = itemType, Version = 1, LastRecordVersion = 1, UpdatedAtUtc = now,
                UpdatedBy = metadata.ActorId,
            };
            database.TaxItemTypeDefaultSeries.Add(series);
        }
        else
        {
            series.Version = checked(series.Version + 1);
            series.LastRecordVersion = checked(series.LastRecordVersion + 1);
            series.UpdatedAtUtc = now;
            series.UpdatedBy = metadata.ActorId;
        }

        var record = new TaxItemTypeDefaultRecordRow
        {
            CompanyId = companyId,
            Id = Guid.NewGuid(),
            ItemType = itemType,
            RecordVersion = series.LastRecordVersion,
            EffectiveFrom = effectiveFrom,
            VatApplies = values.VatApplies,
            VatPercent = values.VatPercent,
            ExemptionCode = values.ExemptionCode,
            WithholdingApplies = values.WithholdingApplies,
            WithholdingPercent = values.WithholdingPercent,
            WithholdingCode = values.WithholdingCode,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.TaxItemTypeDefaultRecords.Add(record);

        // SAT-D84 precedent: a record on the date of a not-yet-effective Settings record REPLACES it (withdrawn in this save),
        // so a later withdrawal can never bring a hidden record back. A catalog entry is never withdrawn.
        var superseded = data.Defaults
            .Where(existingRecord => !existingRecord.IsWithdrawn && existingRecord.Source == TaxDefaultSourcesV1.Record
                                     && existingRecord.ItemType == itemType && existingRecord.EffectiveFrom == effectiveFrom
                                     && existingRecord.EffectiveFrom > today)
            .Select(existingRecord => existingRecord.RecordId)
            .ToArray();
        foreach (var replaced in superseded)
        {
            database.TaxItemTypeDefaultWithdrawals.Add(new TaxItemTypeDefaultWithdrawalRow
            {
                CompanyId = companyId, RecordId = replaced, ReplacedByRecordId = record.Id, WithdrawnAtUtc = now,
                WithdrawnBy = metadata.ActorId, Reason = reason,
            });
        }

        Audit(companyId, superseded.Length == 0 ? "settings.tax-item-type-default.recorded.v1" : "settings.tax-item-type-default.corrected.v1",
            DefaultEntityType, subject, superseded.Length == 0 ? "RecordDefault" : "CorrectDefault", versionBefore, series.Version,
            scope, previous is null ? null : DefaultSnapshot(companyId, previous, []),
            DefaultSnapshot(companyId, ToData(record), superseded), reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("default-record", metadata, subject, await SaveChangeAsync(DefaultRecordCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    public async Task<TaxMasterCommandResultV1> WithdrawItemTypeDefaultAsync(Guid recordId, WithdrawTaxRecordRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (recordId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Kayıt ve kalem tipinin beklenen sürümü (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = recordId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { RecordId = recordId, ExpectedVersion = expectedVersion, Reason = reason });
        if (await FindExecutionAsync(DefaultWithdrawCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("default-withdraw", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await TaxMasterRows.ReadAsync(database, track: true, cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        // Only a Settings record can be withdrawn; a catalog id is "not found" here (it is reviewed source, not a row).
        var target = data.Defaults.SingleOrDefault(record => record.RecordId == recordId && record.Source == TaxDefaultSourcesV1.Record);
        var series = target is null ? null : rows.Series.SingleOrDefault(row => row.ItemType == target.ItemType);
        var refusal = target is null || series is null ? Answers.NotFound()
            : series.Version != expectedVersion ? Answers.VersionConflict()
            : target.IsWithdrawn ? Answers.RecordWithdrawn()
            : target.EffectiveFrom <= today ? Answers.RecordInEffect()
            : null;
        if (refusal is not null)
        {
            return Done("default-withdraw", metadata, subject,
                await StoreRefusalAsync(DefaultWithdrawCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("default-withdraw", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = series!.Version;
        database.TaxItemTypeDefaultWithdrawals.Add(new TaxItemTypeDefaultWithdrawalRow
        {
            CompanyId = companyId, RecordId = recordId, ReplacedByRecordId = null, WithdrawnAtUtc = now,
            WithdrawnBy = metadata.ActorId, Reason = reason,
        });
        series.Version = checked(series.Version + 1);
        series.UpdatedAtUtc = now;
        series.UpdatedBy = metadata.ActorId;
        Audit(companyId, "settings.tax-item-type-default.withdrawn.v1", DefaultEntityType, target!.ItemType, "WithdrawDefault",
            versionBefore, series.Version, scope, DefaultSnapshot(companyId, target, []), new
            {
                CompanyId = companyId,
                target.ItemType,
                WithdrawnRecordId = recordId,
                target.RecordVersion,
                target.EffectiveFrom,
                Withdrawn = true,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("default-withdraw", metadata, subject, await SaveChangeAsync(DefaultWithdrawCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    public async Task<TaxMasterCommandResultV1> PublishCodeListAsync(string listKind, PublishTaxCodeListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TaxCodeListKindsV1.IsKnown(listKind) || !TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Liste türü (Exemption/Withholding) ve beklenen sürüm (\"0\" veya pozitif tam sayı) zorunludur."),
                metadata);
        }

        if (!TryReadEntries(request.Entries, out var entries, out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Kind = listKind,
            ExpectedVersion = expectedVersion,
            Entries = entries.Select(entry => new { entry.Code, entry.Name }).ToArray(),
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(CodeListPublishCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("list-publish", metadata, listKind, Replay(existing, requestHash, metadata));
        }

        var rows = await TaxMasterRows.ReadAsync(database, track: true, cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var list = rows.Lists.SingleOrDefault(row => row.Kind == listKind);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = (list?.Version ?? 0) != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            return Done("list-publish", metadata, listKind,
                await StoreRefusalAsync(CodeListPublishCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        // The same list again from the same date changes nothing (no version, no audit).
        if (TaxMasterCalculatorV1.ListInForce(data, listKind, effectiveFrom) is { } inForce
            && inForce.Entries.Count == entries.Count
            && inForce.Entries.Zip(entries).All(pair => pair.First == pair.Second))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("list-publish", metadata, listKind, await StoreRefusalAsync(CodeListPublishCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("list-publish", metadata, listKind, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = list?.Version;
        if (list is null)
        {
            list = new TaxCodeListRow
            {
                CompanyId = companyId, Kind = listKind, Version = 1, LastListVersion = 1, UpdatedAtUtc = now,
                UpdatedBy = metadata.ActorId,
            };
            database.TaxCodeLists.Add(list);
        }
        else
        {
            list.Version = checked(list.Version + 1);
            list.LastListVersion = checked(list.LastListVersion + 1);
            list.UpdatedAtUtc = now;
            list.UpdatedBy = metadata.ActorId;
        }

        var listVersion = list.LastListVersion;
        database.TaxCodeListVersions.Add(new TaxCodeListVersionRow
        {
            CompanyId = companyId, Kind = listKind, ListVersion = listVersion, EffectiveFrom = effectiveFrom,
            RecordedAtUtc = now, RecordedBy = metadata.ActorId, Reason = reason,
        });
        foreach (var entry in entries)
        {
            database.TaxCodeListEntries.Add(new TaxCodeListEntryRow
            {
                CompanyId = companyId, Kind = listKind, ListVersion = listVersion, Code = entry.Code, Name = entry.Name,
            });
        }

        // A version on the date of a not-yet-effective one REPLACES it (withdrawn in this save).
        var superseded = data.CodeLists
            .Where(version => version.Kind == listKind && !version.IsWithdrawn && version.EffectiveFrom == effectiveFrom
                              && version.EffectiveFrom > today)
            .Select(version => version.ListVersion)
            .ToArray();
        foreach (var replaced in superseded)
        {
            database.TaxCodeListWithdrawals.Add(new TaxCodeListWithdrawalRow
            {
                CompanyId = companyId, Kind = listKind, ListVersion = replaced, ReplacedByListVersion = listVersion,
                WithdrawnAtUtc = now, WithdrawnBy = metadata.ActorId, Reason = reason,
            });
        }

        var after = new
        {
            CompanyId = companyId,
            Kind = listKind,
            ListVersion = listVersion,
            EffectiveFrom = effectiveFrom,
            Entries = entries,
            ReplacesListVersions = superseded,
        };
        Audit(companyId, superseded.Length == 0 ? "settings.tax-code-list.published.v1" : "settings.tax-code-list.corrected.v1",
            CodeListEntityType, listKind, superseded.Length == 0 ? "PublishCodeList" : "CorrectCodeList", versionBefore,
            list.Version, scope, null, after, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("list-publish", metadata, listKind, await SaveChangeAsync(CodeListPublishCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    public async Task<TaxMasterCommandResultV1> WithdrawCodeListAsync(string listKind, long listVersion,
        WithdrawTaxCodeListRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TaxCodeListKindsV1.IsKnown(listKind) || listVersion < 1
            || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Liste türü, liste sürümü ve beklenen sürüm (pozitif tam sayı metni) zorunludur."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Kind = listKind, ListVersion = listVersion, ExpectedVersion = expectedVersion, Reason = reason,
        });
        if (await FindExecutionAsync(CodeListWithdrawCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("list-withdraw", metadata, listKind, Replay(existing, requestHash, metadata));
        }

        var rows = await TaxMasterRows.ReadAsync(database, track: true, cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var list = rows.Lists.SingleOrDefault(row => row.Kind == listKind);
        var target = data.CodeLists.SingleOrDefault(version => version.Kind == listKind && version.ListVersion == listVersion);
        var refusal = list is null || target is null ? Answers.NotFound()
            : list.Version != expectedVersion ? Answers.VersionConflict()
            : target.IsWithdrawn ? Answers.RecordWithdrawn()
            : target.EffectiveFrom <= today ? Answers.RecordInEffect()
            : null;
        if (refusal is not null)
        {
            return Done("list-withdraw", metadata, listKind,
                await StoreRefusalAsync(CodeListWithdrawCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("list-withdraw", metadata, listKind, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = list!.Version;
        database.TaxCodeListWithdrawals.Add(new TaxCodeListWithdrawalRow
        {
            CompanyId = companyId, Kind = listKind, ListVersion = listVersion, ReplacedByListVersion = null,
            WithdrawnAtUtc = now, WithdrawnBy = metadata.ActorId, Reason = reason,
        });
        list.Version = checked(list.Version + 1);
        list.UpdatedAtUtc = now;
        list.UpdatedBy = metadata.ActorId;
        Audit(companyId, "settings.tax-code-list.withdrawn.v1", CodeListEntityType, listKind, "WithdrawCodeList", versionBefore,
            list.Version, scope, null, new
            {
                CompanyId = companyId, Kind = listKind, WithdrawnListVersion = listVersion, target!.EffectiveFrom, Withdrawn = true,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("list-withdraw", metadata, listKind, await SaveChangeAsync(CodeListWithdrawCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    private sealed record DefaultValues(bool VatApplies, decimal? VatPercent, string? ExemptionCode, bool WithholdingApplies,
        decimal? WithholdingPercent, string? WithholdingCode);

    private static bool TryReadDefault(bool vatApplies, string? vatPercentText, string? exemptionCodeText, bool withholdingApplies,
        string? withholdingPercentText, string? withholdingCodeText, out DefaultValues values, out string detail)
    {
        values = new DefaultValues(false, null, null, false, null, null);
        detail = string.Empty;
        decimal? vatPercent = null;
        string? exemptionCode = null;
        if (vatApplies)
        {
            if (!TaxMasterCalculatorV1.TryParsePercent(vatPercentText, allowZero: true, out var parsed) || exemptionCodeText is not null)
            {
                detail = "KDV uygulanıyorsa 0-100 arası, en çok 4 ondalıklı oran girilir; istisna kodu girilmez.";
                return false;
            }

            vatPercent = parsed;
        }
        else
        {
            if (vatPercentText is not null || withholdingApplies)
            {
                detail = "KDV uygulanmıyorsa oran ve tevkifat girilmez; istisna kodu isteğe bağlıdır.";
                return false;
            }

            if (exemptionCodeText is not null)
            {
                if (!TaxMasterCalculatorV1.TryNormalizeListCode(exemptionCodeText, out var code))
                {
                    detail = "İstisna kodu yalnız büyük harf ve rakam (en çok 16) olabilir.";
                    return false;
                }

                exemptionCode = code;
            }
        }

        decimal? withholdingPercent = null;
        string? withholdingCode = null;
        if (withholdingApplies)
        {
            if (!TaxMasterCalculatorV1.TryParsePercent(withholdingPercentText, allowZero: false, out var parsed))
            {
                detail = "Tevkifat uygulanıyorsa 0'dan büyük, en fazla 100, en çok 4 ondalıklı oran girilir.";
                return false;
            }

            withholdingPercent = parsed;
            if (withholdingCodeText is not null)
            {
                if (!TaxMasterCalculatorV1.TryNormalizeListCode(withholdingCodeText, out var code))
                {
                    detail = "Tevkifat kodu yalnız büyük harf ve rakam (en çok 16) olabilir.";
                    return false;
                }

                withholdingCode = code;
            }
        }
        else if (withholdingPercentText is not null || withholdingCodeText is not null)
        {
            detail = "Tevkifat uygulanmıyorsa oran ve kod girilmez.";
            return false;
        }

        values = new DefaultValues(vatApplies, vatPercent, exemptionCode, withholdingApplies, withholdingPercent, withholdingCode);
        return true;
    }

    private static bool SameDefault(TaxItemTypeDefaultDataV1 record, DefaultValues values) =>
        record.VatApplies == values.VatApplies && record.VatPercent == values.VatPercent
        && string.Equals(record.ExemptionCode, values.ExemptionCode, StringComparison.Ordinal)
        && record.WithholdingApplies == values.WithholdingApplies && record.WithholdingPercent == values.WithholdingPercent
        && string.Equals(record.WithholdingCode, values.WithholdingCode, StringComparison.Ordinal);

    private static TaxItemTypeDefaultDataV1 ToData(TaxItemTypeDefaultRecordRow row) =>
        new TaxItemTypeDefaultDataV1(row.Id, row.CompanyId, row.ItemType, TaxDefaultSourcesV1.Record, row.RecordVersion,
            row.EffectiveFrom, row.VatApplies, row.VatPercent, row.ExemptionCode, row.WithholdingApplies, row.WithholdingPercent,
            row.WithholdingCode);

    private static object DefaultSnapshot(Guid companyId, TaxItemTypeDefaultDataV1 record, IReadOnlyList<Guid> replacesRecordIds) => new
    {
        CompanyId = companyId,
        record.ItemType,
        record.RecordId,
        record.Source,
        record.RecordVersion,
        record.EffectiveFrom,
        record.VatApplies,
        VatPercent = record.VatPercent is { } vat ? TaxMasterCalculatorV1.Format(vat) : null,
        record.ExemptionCode,
        record.WithholdingApplies,
        WithholdingPercent = record.WithholdingPercent is { } withholding ? TaxMasterCalculatorV1.Format(withholding) : null,
        record.WithholdingCode,
        ReplacesRecordIds = replacesRecordIds,
    };

    private static bool TryReadEntries(IReadOnlyList<TaxCodeListEntryV1>? input, out IReadOnlyList<TaxCodeListEntryViewV1> entries,
        out string detail)
    {
        entries = [];
        detail = string.Empty;
        if (input is null || input.Count is 0 || input.Count > TaxMasterInputLimitsV1.ListMaximumEntries)
        {
            detail = $"Liste 1-{TaxMasterInputLimitsV1.ListMaximumEntries} kod içermelidir.";
            return false;
        }

        var result = new List<TaxCodeListEntryViewV1>(input.Count);
        foreach (var entry in input)
        {
            if (entry is null || !TaxMasterCalculatorV1.TryNormalizeListCode(entry.Code, out var code)
                || !TryNormalizeName(entry.Name, TaxMasterInputLimitsV1.ListEntryNameMaximumLength, out var name))
            {
                detail = "Her kod yalnız büyük harf ve rakam (en çok 16), her ad 1-200 karakter olmalıdır.";
                return false;
            }

            result.Add(new TaxCodeListEntryViewV1(code, name));
        }

        if (result.Select(entry => entry.Code).Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            detail = "Bir kod listede bir kez yer alır.";
            return false;
        }

        entries = result.OrderBy(entry => entry.Code, StringComparer.Ordinal).ToArray();
        return true;
    }
}
