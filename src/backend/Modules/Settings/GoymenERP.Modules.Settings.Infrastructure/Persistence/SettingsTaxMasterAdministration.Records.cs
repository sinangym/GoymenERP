using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The rate records (SAT-D82 VAT, SAT-D74 withholding) and the code list versions (EK13 UBL-TR lists) on their timelines.
/// A record or list version row never changes; a withdrawal is an insert-only row. Every change moves the owning class's
/// (or list's) version, which fences concurrent changes of the same series.
/// </summary>
internal sealed partial class SettingsTaxMasterAdministration
{
    private const string VatRateRecordCommandName = "settings.tax-rate.record-vat.v1";
    private const string WithholdingRateRecordCommandName = "settings.tax-rate.record-withholding.v1";
    private const string RateWithdrawCommandName = "settings.tax-rate.withdraw.v1";
    private const string CodeListPublishCommandName = "settings.tax-code-list.publish.v1";
    private const string CodeListWithdrawCommandName = "settings.tax-code-list.withdraw.v1";

    public async Task<TaxMasterCommandResultV1> RecordVatRateAsync(Guid vatClassId, RecordVatRateRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (vatClassId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("KDV sınıfı ve beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!TaxMasterCalculatorV1.TryParsePercent(request.RatePercent, allowZero: true, out var rate))
        {
            return Unstored(Answers.Invalid("KDV oranı 0-100 arası, en çok 4 ondalıklı bir sayı metni olmalıdır."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ClassId = vatClassId, ExpectedVersion = expectedVersion, RatePercent = rate, Reason = reason, request.EffectiveFrom,
        });
        var change = new RateChange(VatRateRecordCommandName, TaxRateKindsV1.Vat, TaxCategoryKindsV1.VatClass, vatClassId,
            expectedVersion, null, true, rate, null, request.EffectiveFrom, reason);
        return await RecordRateAsync(change, requestHash, companyId, scope, metadata, cancellationToken);
    }

    public async Task<TaxMasterCommandResultV1> RecordWithholdingRateAsync(Guid withholdingClassId,
        RecordWithholdingRateRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (withholdingClassId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion)
            || request.BuyerCategoryId is not { } buyerCategoryId || buyerCategoryId == Guid.Empty
            || request.Applies is not { } applies)
        {
            return Unstored(Answers.Invalid(
                "Tevkifat sınıfı, beklenen sürüm, alıcı kategorisi ve uygulanıp uygulanmadığı zorunludur."), metadata);
        }

        decimal? rate = null;
        string? code = null;
        if (applies)
        {
            if (!TaxMasterCalculatorV1.TryParsePercent(request.RatePercent, allowZero: false, out var parsed)
                || !TaxMasterCalculatorV1.TryNormalizeListCode(request.WithholdingCode, out var normalized))
            {
                return Unstored(Answers.Invalid(
                    "Tevkifat uygulanıyorsa 0'dan büyük, en fazla 100, en çok 4 ondalıklı oran ve tevkifat kodu (A-Z, 0-9; en çok 16) girilir."),
                    metadata);
            }

            (rate, code) = (parsed, normalized);
        }
        else if (request.RatePercent is not null || request.WithholdingCode is not null)
        {
            return Unstored(Answers.Invalid("Tevkifat uygulanmıyorsa oran ve kod girilmez."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ClassId = withholdingClassId,
            ExpectedVersion = expectedVersion,
            BuyerCategoryId = buyerCategoryId,
            Applies = applies,
            RatePercent = rate,
            WithholdingCode = code,
            Reason = reason,
            request.EffectiveFrom,
        });
        var change = new RateChange(WithholdingRateRecordCommandName, TaxRateKindsV1.Withholding,
            TaxCategoryKindsV1.WithholdingClass, withholdingClassId, expectedVersion, buyerCategoryId, applies, rate, code,
            request.EffectiveFrom, reason);
        return await RecordRateAsync(change, requestHash, companyId, scope, metadata, cancellationToken);
    }

    public async Task<TaxMasterCommandResultV1> WithdrawRateRecordAsync(Guid recordId, WithdrawTaxRateRecordRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (recordId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Kayıt ve sınıfın beklenen sürümü (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = recordId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { RecordId = recordId, ExpectedVersion = expectedVersion, Reason = reason });
        if (await FindExecutionAsync(RateWithdrawCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-withdraw", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await TaxMasterRows.ReadAsync(database, trackCategoriesAndLists: true, cancellationToken);
        var data = rows.ToData();
        var today = SettingsBusinessDate.Today(timeProvider);
        var record = rows.Records.SingleOrDefault(row => row.Id == recordId);
        var owner = record is null ? null : rows.Categories.SingleOrDefault(row => row.Id == record.ClassId);
        var target = data.Records.SingleOrDefault(row => row.RecordId == recordId);
        var refusal = record is null || owner is null || target is null ? Answers.NotFound()
            : owner.Version != expectedVersion ? Answers.VersionConflict()
            : target.IsWithdrawn ? Answers.RecordWithdrawn()
            : target.EffectiveFrom <= today ? Answers.RecordInEffect()
            : null;
        if (refusal is not null)
        {
            return Done("rate-withdraw", metadata, subject,
                await StoreRefusalAsync(RateWithdrawCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("rate-withdraw", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var versionBefore = owner!.Version;
        database.TaxRateRecordWithdrawals.Add(new TaxRateRecordWithdrawalRow
        {
            CompanyId = companyId,
            RecordId = recordId,
            ReplacedByRecordId = null,
            WithdrawnAtUtc = timeProvider.GetUtcNow(),
            WithdrawnBy = metadata.ActorId,
            Reason = reason,
        });
        Touch(owner, metadata);
        Audit(companyId, "settings.tax-rate.withdrawn.v1", CategoryEntityType, owner.Id.ToString("D"), "WithdrawRate",
            versionBefore, owner.Version, scope, RecordSnapshot(owner, record!, []), new
            {
                ClassId = owner.Id,
                owner.CompanyId,
                WithdrawnRecordId = recordId,
                record!.RecordVersion,
                record.EffectiveFrom,
                Withdrawn = true,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("rate-withdraw", metadata, subject, await SaveChangeAsync(RateWithdrawCommandName, Answers.NoContent(),
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

        var rows = await TaxMasterRows.ReadAsync(database, trackCategoriesAndLists: true, cancellationToken);
        var data = rows.ToData();
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

        var rows = await TaxMasterRows.ReadAsync(database, trackCategoriesAndLists: true, cancellationToken);
        var data = rows.ToData();
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

    /// <summary>A rate command after its input checks.</summary>
    private sealed record RateChange(
        string CommandName,
        string RateKind,
        string ClassKind,
        Guid ClassId,
        long ExpectedVersion,
        Guid? BuyerCategoryId,
        bool Applies,
        decimal? RatePercent,
        string? WithholdingCode,
        DateOnly? EffectiveFrom,
        string Reason);

    private async Task<TaxMasterCommandResultV1> RecordRateAsync(RateChange change, string requestHash, Guid companyId,
        PermissionScope scope, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var subject = change.ClassId.ToString("D");
        if (await FindExecutionAsync(change.CommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-record", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await TaxMasterRows.ReadAsync(database, trackCategoriesAndLists: true, cancellationToken);
        var data = rows.ToData();
        var today = SettingsBusinessDate.Today(timeProvider);
        var owner = rows.Categories.SingleOrDefault(row => row.Id == change.ClassId);
        var effectiveFrom = change.EffectiveFrom ?? today;
        var refusal = owner is null ? Answers.NotFound()
            : owner.Kind != change.ClassKind
                ? Answers.Problem(422, TaxMasterProblemCodesV1.CategoryKindMismatch, "Kategori türü uyuşmuyor.",
                    "Bu kayıt yalnız ilgili türdeki sınıfa girilir.")
            : owner.Version != change.ExpectedVersion ? Answers.VersionConflict()
            : change.BuyerCategoryId is { } buyer
              && !rows.Categories.Any(row => row.Id == buyer && row.Kind == TaxCategoryKindsV1.BuyerCategory)
                ? Answers.Problem(422, TaxMasterProblemCodesV1.BuyerCategoryNotFound, "Alıcı kategorisi bulunamadı.",
                    "Alıcı kategorisi bu şirkette yok.")
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : change.WithholdingCode is { } code
              && !(TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Withholding, effectiveFrom)?.Entries
                  .Any(entry => string.Equals(entry.Code, code, StringComparison.Ordinal)) ?? false)
                ? Answers.Problem(422, TaxMasterProblemCodesV1.WithholdingCodeUnknown, "Tevkifat kodu listede yok.",
                    "Tevkifat kodu, kaydın başladığı gün yürürlükteki tevkifat kod listesinde bulunmalıdır.")
            : null;
        if (refusal is not null)
        {
            return Done("rate-record", metadata, subject,
                await StoreRefusalAsync(change.CommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        // The same value already in force on the date changes nothing (no record, no version, no audit).
        if (TaxMasterCalculatorV1.RateInForce(data, change.RateKind, change.ClassId, change.BuyerCategoryId, effectiveFrom) is
                { } inForce
            && inForce.Applies == change.Applies && inForce.RatePercent == change.RatePercent
            && string.Equals(inForce.WithholdingCode, change.WithholdingCode, StringComparison.Ordinal))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-record", metadata, subject, await StoreRefusalAsync(change.CommandName, Answers.NoContent(),
                keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            // The business day turned while the command was checked: nothing is written; the editor re-reads.
            return Done("rate-record", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = owner!.Version;
        var record = new TaxRateRecordRow
        {
            CompanyId = companyId,
            Id = Guid.NewGuid(),
            Kind = change.RateKind,
            ClassId = change.ClassId,
            BuyerCategoryId = change.BuyerCategoryId,
            RecordVersion = checked(owner.RecordVersion + 1),
            EffectiveFrom = effectiveFrom,
            Applies = change.Applies,
            RatePercent = change.RatePercent,
            WithholdingCode = change.WithholdingCode,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId,
            Reason = change.Reason,
        };
        database.TaxRateRecords.Add(record);
        owner.RecordVersion = record.RecordVersion;

        // SAT-D84 precedent: a record on the date of a not-yet-effective one of the same series REPLACES it, so a later
        // withdrawal can never bring a hidden record back.
        var superseded = data.Records
            .Where(existingRecord => !existingRecord.IsWithdrawn && existingRecord.Kind == change.RateKind
                                     && existingRecord.ClassId == change.ClassId
                                     && existingRecord.BuyerCategoryId == change.BuyerCategoryId
                                     && existingRecord.EffectiveFrom == effectiveFrom && existingRecord.EffectiveFrom > today)
            .Select(existingRecord => existingRecord.RecordId)
            .ToArray();
        foreach (var replaced in superseded)
        {
            database.TaxRateRecordWithdrawals.Add(new TaxRateRecordWithdrawalRow
            {
                CompanyId = companyId, RecordId = replaced, ReplacedByRecordId = record.Id, WithdrawnAtUtc = now,
                WithdrawnBy = metadata.ActorId, Reason = change.Reason,
            });
        }

        Touch(owner, metadata);
        var previous = TaxMasterCalculatorV1.RateInForce(data, change.RateKind, change.ClassId, change.BuyerCategoryId,
            effectiveFrom) is { } before
            ? rows.Records.Single(row => row.Id == before.RecordId)
            : null;
        Audit(companyId, superseded.Length == 0 ? "settings.tax-rate.recorded.v1" : "settings.tax-rate.corrected.v1",
            CategoryEntityType, subject, superseded.Length == 0 ? "RecordRate" : "CorrectRate", versionBefore, owner.Version,
            scope, previous is null ? null : RecordSnapshot(owner, previous, []), RecordSnapshot(owner, record, superseded),
            change.Reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("rate-record", metadata, subject, await SaveChangeAsync(change.CommandName, Answers.NoContent(), keyHash,
            requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    private static object RecordSnapshot(TaxCategoryRow owner, TaxRateRecordRow record, IReadOnlyList<Guid> replacesRecordIds) => new
    {
        ClassId = owner.Id,
        owner.CompanyId,
        ClassCode = owner.Code,
        RecordId = record.Id,
        record.Kind,
        record.BuyerCategoryId,
        record.RecordVersion,
        record.EffectiveFrom,
        record.Applies,
        RatePercent = record.RatePercent is { } rate ? TaxMasterCalculatorV1.Format(rate) : null,
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
