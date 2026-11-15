using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D181 (EK26) / F-029 S9: the company's VAT rate and withholding ratio lists — effective-dated Settings data a Sales line's
/// reasoned tax change picks from (read through <see cref="ISettingsTaxRateListReaderV1"/>). The lists start EMPTY (SAT-D148:
/// nothing is seeded; the reviewed catalog <see cref="ApprovedTaxRateListCatalog"/> is empty) and are entered directly under the
/// tax master authority (SAT-D145). The same timeline rules as the code lists: a whole version replaces the previous one from
/// its date, a same-date version replaces a not-yet-effective one, a not-yet-effective version can be withdrawn, a version in
/// force never changes. A withholding ratio may name the GİB withholding code it is invoiced under; that code must be in the
/// SAT-D148 withholding code list in force on the version's date. The same authority, reason, key, version and audit rules as
/// every tax master command.
/// </summary>
internal sealed partial class SettingsTaxMasterAdministration
{
    private const string RateListEntityType = "Settings.TaxRateList";
    private const string RateListPublishCommandName = "settings.tax-rate-list.publish.v1";
    private const string RateListWithdrawCommandName = "settings.tax-rate-list.withdraw.v1";

    public async Task<TaxRateListsViewV1> GetRateListsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        TaxRateListRows rows;
        try
        {
            rows = await TaxRateListRows.ReadAsync(database, track: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }

        await RequireStillAsync(companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        var versions = rows.ToVersions(companyId);
        var versionRows = rows.Versions.ToDictionary(row => (row.Kind, row.ListVersion));
        var lists = TaxRateListKindsV1.All
            .Select(kind => new TaxRateListViewV1(
                kind,
                Text(rows.Lists.SingleOrDefault(list => list.Kind == kind)?.Version ?? 0),
                ApprovedTaxRateListCatalog.InForce(versions, kind, today) is { } inForce
                    ? RateListView(versionRows[(kind, inForce.ListVersion)], inForce)
                    : null,
                ApprovedTaxRateListCatalog.Scheduled(versions, kind, today)
                    .Select(version => RateListView(versionRows[(kind, version.ListVersion)], version))
                    .ToArray()))
            .ToArray();
        return new TaxRateListsViewV1(companyId, today, lists);
    }

    public async Task<TaxMasterCommandResultV1> PublishRateListAsync(string listKind, PublishTaxRateListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TaxRateListKindsV1.IsKnown(listKind))
        {
            return Unstored(Answers.Invalid("Oran listesi türü VatRate veya WithholdingRatio olmalıdır."), metadata);
        }

        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!TryReadRates(listKind, request.Entries, out var entries, out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = listKind;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Kind = listKind,
            ExpectedVersion = expectedVersion,
            Entries = entries
                .Select(entry => new { entry.Code, entry.Name, Percent = TaxMasterCalculatorV1.Format(entry.Percent), entry.TaxCode })
                .ToArray(),
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(RateListPublishCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-list-publish", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (rows, codeLists) = await ReadRateListsForCommandAsync(companyId, cancellationToken);
        var versions = rows.ToVersions(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var list = rows.Lists.SingleOrDefault(row => row.Kind == listKind);
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = (list?.Version ?? 0) != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : entries.Any(entry => entry.TaxCode is { } code
                && !TaxMasterCalculatorV1.IsListed(codeLists, TaxCodeListKindsV1.Withholding, code, effectiveFrom))
                ? Answers.Problem(422, TaxMasterProblemCodesV1.WithholdingCodeUnknown, "Tevkifat kodu listede yok.",
                    "Tevkifat oranının GİB kodu, sürümün başladığı tarihte yürürlükteki tevkifat kod listesinde olmalıdır.")
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-list-publish", metadata, subject, await StoreRefusalAsync(RateListPublishCommandName, refusal,
                keyHash, requestHash, metadata, cancellationToken));
        }

        // The same list again from the same date changes nothing (no version, no audit).
        var previous = ApprovedTaxRateListCatalog.InForce(versions, listKind, effectiveFrom);
        if (previous is not null && previous.Entries.Count == entries.Count
            && previous.Entries.Zip(entries).All(pair => pair.First == pair.Second))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-list-publish", metadata, subject, await StoreRefusalAsync(RateListPublishCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("rate-list-publish", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = list?.Version;
        if (list is null)
        {
            list = new TaxRateListRow
            {
                CompanyId = companyId, Kind = listKind, Version = 1, LastListVersion = 1, UpdatedAtUtc = now,
                UpdatedBy = metadata.ActorId,
            };
            database.TaxRateLists.Add(list);
        }
        else
        {
            list.Version = checked(list.Version + 1);
            list.LastListVersion = checked(list.LastListVersion + 1);
            list.UpdatedAtUtc = now;
            list.UpdatedBy = metadata.ActorId;
        }

        var listVersion = list.LastListVersion;
        database.TaxRateListVersions.Add(new TaxRateListVersionRow
        {
            CompanyId = companyId, Kind = listKind, ListVersion = listVersion, EffectiveFrom = effectiveFrom, RecordedAtUtc = now,
            RecordedBy = metadata.ActorId, Reason = reason,
        });
        foreach (var entry in entries)
        {
            database.TaxRateListEntries.Add(new TaxRateListEntryRow
            {
                CompanyId = companyId, Kind = listKind, ListVersion = listVersion, Code = entry.Code, Name = entry.Name,
                Percent = entry.Percent, TaxCode = entry.TaxCode,
            });
        }

        // A version on the date of a not-yet-effective version REPLACES it (withdrawn in this save).
        var superseded = versions
            .Where(version => !version.IsWithdrawn && version.Kind == listKind
                              && version.EffectiveFrom == effectiveFrom && version.EffectiveFrom > today)
            .Select(version => version.ListVersion)
            .ToArray();
        foreach (var replaced in superseded)
        {
            database.TaxRateListWithdrawals.Add(new TaxRateListWithdrawalRow
            {
                CompanyId = companyId, Kind = listKind, ListVersion = replaced, ReplacedByListVersion = listVersion,
                WithdrawnAtUtc = now, WithdrawnBy = metadata.ActorId, Reason = reason,
            });
        }

        Audit(companyId,
            superseded.Length == 0 ? "settings.tax-rate-list.published.v1" : "settings.tax-rate-list.corrected.v1",
            RateListEntityType, subject, superseded.Length == 0 ? "PublishTaxRateList" : "CorrectTaxRateList",
            versionBefore, list.Version, scope,
            previous is null ? null : new
            {
                CompanyId = companyId, Kind = listKind, previous.ListVersion, previous.EffectiveFrom,
                Entries = previous.Entries.Select(EntryWire).ToArray(),
            },
            new
            {
                CompanyId = companyId,
                Kind = listKind,
                ListVersion = listVersion,
                EffectiveFrom = effectiveFrom,
                Entries = entries.Select(EntryWire).ToArray(),
                ReplacesListVersions = superseded,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("rate-list-publish", metadata, subject, await SaveChangeAsync(RateListPublishCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    public async Task<TaxMasterCommandResultV1> WithdrawRateListAsync(string listKind, long listVersion,
        WithdrawTaxCodeListRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TaxRateListKindsV1.IsKnown(listKind) || listVersion < 1
            || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid(
                "Oran listesi türü (VatRate / WithholdingRatio), liste sürümü ve beklenen sürüm (pozitif tam sayı metni) zorunludur."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = listKind;
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            Kind = listKind, ListVersion = listVersion, ExpectedVersion = expectedVersion, Reason = reason,
        });
        if (await FindExecutionAsync(RateListWithdrawCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-list-withdraw", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var (rows, _) = await ReadRateListsForCommandAsync(companyId, cancellationToken);
        var versions = rows.ToVersions(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var list = rows.Lists.SingleOrDefault(row => row.Kind == listKind);
        var target = versions.SingleOrDefault(version => version.Kind == listKind && version.ListVersion == listVersion);
        var refusal = list is null || target is null ? Answers.NotFound()
            : list.Version != expectedVersion ? Answers.VersionConflict()
            : target.IsWithdrawn ? Answers.RecordWithdrawn()
            : target.EffectiveFrom <= today ? Answers.RecordInEffect()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("rate-list-withdraw", metadata, subject, await StoreRefusalAsync(RateListWithdrawCommandName, refusal,
                keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("rate-list-withdraw", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = list!.Version;
        database.TaxRateListWithdrawals.Add(new TaxRateListWithdrawalRow
        {
            CompanyId = companyId, Kind = listKind, ListVersion = listVersion, ReplacedByListVersion = null,
            WithdrawnAtUtc = now, WithdrawnBy = metadata.ActorId, Reason = reason,
        });
        list.Version = checked(list.Version + 1);
        list.UpdatedAtUtc = now;
        list.UpdatedBy = metadata.ActorId;
        Audit(companyId, "settings.tax-rate-list.withdrawn.v1", RateListEntityType, subject, "WithdrawTaxRateList",
            versionBefore, list.Version, scope, null, new
            {
                CompanyId = companyId, Kind = listKind, WithdrawnListVersion = listVersion, target!.EffectiveFrom, Withdrawn = true,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("rate-list-withdraw", metadata, subject, await SaveChangeAsync(RateListWithdrawCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    /// <summary>
    /// A rate list command's reads: the rate list rows tracked (the list row is the edit fence) and the company's tax master data
    /// (the withholding code list a mapped GİB code is checked against). An unreadable store is
    /// <c>settings_provider_unavailable</c> (503), never a 500.
    /// </summary>
    private async Task<(TaxRateListRows Rows, TaxMasterDataV1 CodeLists)> ReadRateListsForCommandAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await TaxRateListRows.ReadAsync(database, track: true, cancellationToken);
            var taxMaster = await TaxMasterRows.ReadAsync(database, track: false, cancellationToken);
            return (rows, taxMaster.ToData(companyId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }
    }

    private static TaxRateListVersionViewV1 RateListView(TaxRateListVersionRow row, TaxRateListVersionDataV1 data) =>
        new TaxRateListVersionViewV1(
            Text(row.ListVersion),
            row.EffectiveFrom,
            data.Entries.Select(EntryWire).ToArray(),
            row.RecordedBy,
            row.RecordedAtUtc,
            row.Reason);

    private static TaxRateListEntryWireViewV1 EntryWire(TaxRateListEntryViewV1 entry) =>
        new TaxRateListEntryWireViewV1(entry.Code, entry.Name, TaxMasterCalculatorV1.Format(entry.Percent), entry.TaxCode);

    /// <summary>
    /// A rate list's entries: 1…<see cref="TaxRateListInputLimitsV1.MaximumEntries"/>, each a well-formed unique code, a name, a
    /// lossless percent (a VAT rate 0…100, a withholding ratio above 0…100) and — on a withholding ratio only — an optional
    /// well-formed GİB code; ordered by code (ordinal) so the same list always compares and stores the same way.
    /// </summary>
    private static bool TryReadRates(string kind, IReadOnlyList<TaxRateListEntryV1>? input,
        out IReadOnlyList<TaxRateListEntryViewV1> entries, out string detail)
    {
        entries = [];
        detail = string.Empty;
        if (input is null || input.Count is 0 || input.Count > TaxRateListInputLimitsV1.MaximumEntries)
        {
            detail = $"Liste 1-{TaxRateListInputLimitsV1.MaximumEntries} oran içermelidir.";
            return false;
        }

        var isVat = kind == TaxRateListKindsV1.VatRate;
        var result = new List<TaxRateListEntryViewV1>(input.Count);
        foreach (var entry in input)
        {
            if (entry is null || !TryNormalizeRateCode(entry.Code, out var code)
                || !TryNormalizeName(entry.Name, TaxMasterInputLimitsV1.ListEntryNameMaximumLength, out var name)
                || !TaxMasterCalculatorV1.TryParsePercent(entry.Percent, allowZero: isVat, out var percent))
            {
                detail = "Her oranda büyük harf/rakam/'/' kod (en çok "
                    + $"{TaxRateListInputLimitsV1.CodeMaximumLength}), 1-{TaxMasterInputLimitsV1.ListEntryNameMaximumLength} "
                    + $"karakter ad ve en çok {TaxMasterInputLimitsV1.PercentMaximumScale} ondalıklı oran (KDV 0-100, tevkifat "
                    + "0'dan büyük-100) zorunludur.";
                return false;
            }

            string? taxCode = null;
            if (entry.TaxCode is not null)
            {
                if (isVat)
                {
                    detail = "KDV oranı tevkifat kodu taşımaz.";
                    return false;
                }

                if (!TaxMasterCalculatorV1.TryNormalizeListCode(entry.TaxCode, out var normalizedTaxCode))
                {
                    detail = "Tevkifat kodu büyük harf ve rakamlardan oluşmalıdır (en çok "
                        + $"{TaxMasterInputLimitsV1.ListCodeMaximumLength}).";
                    return false;
                }

                taxCode = normalizedTaxCode;
            }

            result.Add(new TaxRateListEntryViewV1(code, name, percent, taxCode));
        }

        if (result.Select(entry => entry.Code).Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            detail = "Bir oran kodu listede bir kez yer alır.";
            return false;
        }

        entries = result.OrderBy(entry => entry.Code, StringComparer.Ordinal).ToArray();
        return true;
    }

    /// <summary>A rate code: upper-case ASCII letters, digits and '/', 1…16 (e.g. "20", "5/10").</summary>
    private static bool TryNormalizeRateCode(string? code, out string normalized)
    {
        normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var value = normalized;
        return value.Length > 0 && value.Length <= TaxRateListInputLimitsV1.CodeMaximumLength
            && value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '/');
    }
}
