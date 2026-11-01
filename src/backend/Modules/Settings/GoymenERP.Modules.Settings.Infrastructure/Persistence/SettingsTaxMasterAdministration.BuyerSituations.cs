using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D158 (EK21): the company's buyer situation list — an effective-dated Settings list over the reviewed version-0 catalog
/// (<see cref="ApprovedTaxBuyerSituationCatalog"/>), each situation with its tax effect as data (SAT-D74/D93/D147). The same
/// timeline rules as the code lists: a whole version replaces the previous one from its date, a same-date version replaces a
/// not-yet-effective one, a not-yet-effective version can be withdrawn, a version in force never changes, the catalog is never
/// withdrawn. The same authority (<see cref="SettingsTaxMasterPermissionManifestV1.TaxMasterManage"/>), reason, key, version
/// and audit rules as every tax master command.
/// </summary>
internal sealed partial class SettingsTaxMasterAdministration
{
    private const string BuyerSituationEntityType = "Settings.TaxBuyerSituationList";
    private const string BuyerSituationPublishCommandName = "settings.tax-buyer-situation-list.publish.v1";
    private const string BuyerSituationWithdrawCommandName = "settings.tax-buyer-situation-list.withdraw.v1";

    public async Task<TaxBuyerSituationListViewV1> GetBuyerSituationsAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(companyId, cancellationToken);
        TaxMasterRows rows;
        try
        {
            rows = await TaxMasterRows.ReadAsync(database, track: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }

        await RequireStillAsync(companyId, cancellationToken);
        var today = SettingsBusinessDate.Today(timeProvider);
        return BuyerSituationView(companyId, today, rows, rows.ToData(companyId));
    }

    public async Task<TaxMasterCommandResultV1> PublishBuyerSituationListAsync(PublishTaxBuyerSituationListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (!TryReadSituations(request.Entries, out var entries, out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        const string subject = "buyer-situations";
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion,
            Entries = entries
                .Select(entry => new { entry.Code, entry.Name, entry.WithholdingApplies, entry.VatMode, entry.DocumentRequired })
                .ToArray(),
            Reason = reason,
            request.EffectiveFrom,
        });
        if (await FindExecutionAsync(BuyerSituationPublishCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("situation-publish", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await ReadForCommandAsync(cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var list = rows.SituationList;
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = (list?.Version ?? 0) != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("situation-publish", metadata, subject, await StoreRefusalAsync(BuyerSituationPublishCommandName, refusal,
                keyHash, requestHash, metadata, cancellationToken));
        }

        // The same list again from the same date (catalog or version) changes nothing (no version, no audit).
        if (TaxMasterCalculatorV1.BuyerSituationListInForce(data, effectiveFrom) is { } inForce
            && inForce.Entries.Count == entries.Count
            && inForce.Entries.Zip(entries).All(pair => pair.First == pair.Second))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("situation-publish", metadata, subject, await StoreRefusalAsync(BuyerSituationPublishCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("situation-publish", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = list?.Version;
        if (list is null)
        {
            list = new TaxBuyerSituationListRow
            {
                CompanyId = companyId, Version = 1, LastListVersion = 1, UpdatedAtUtc = now, UpdatedBy = metadata.ActorId,
            };
            database.TaxBuyerSituationLists.Add(list);
        }
        else
        {
            list.Version = checked(list.Version + 1);
            list.LastListVersion = checked(list.LastListVersion + 1);
            list.UpdatedAtUtc = now;
            list.UpdatedBy = metadata.ActorId;
        }

        var listVersion = list.LastListVersion;
        database.TaxBuyerSituationListVersions.Add(new TaxBuyerSituationListVersionRow
        {
            CompanyId = companyId, ListVersion = listVersion, EffectiveFrom = effectiveFrom, RecordedAtUtc = now,
            RecordedBy = metadata.ActorId, Reason = reason,
        });
        foreach (var entry in entries)
        {
            database.TaxBuyerSituationEntries.Add(new TaxBuyerSituationEntryRow
            {
                CompanyId = companyId, ListVersion = listVersion, Code = entry.Code, Name = entry.Name,
                WithholdingApplies = entry.WithholdingApplies, VatMode = entry.VatMode, DocumentRequired = entry.DocumentRequired,
            });
        }

        // A version on the date of a not-yet-effective Settings version REPLACES it (withdrawn in this save); the catalog never.
        var superseded = data.BuyerSituationLists
            .Where(version => !version.IsWithdrawn && version.Source == TaxDefaultSourcesV1.Record
                              && version.EffectiveFrom == effectiveFrom && version.EffectiveFrom > today)
            .Select(version => version.ListVersion)
            .ToArray();
        foreach (var replaced in superseded)
        {
            database.TaxBuyerSituationListWithdrawals.Add(new TaxBuyerSituationListWithdrawalRow
            {
                CompanyId = companyId, ListVersion = replaced, ReplacedByListVersion = listVersion, WithdrawnAtUtc = now,
                WithdrawnBy = metadata.ActorId, Reason = reason,
            });
        }

        var previous = TaxMasterCalculatorV1.BuyerSituationListInForce(data, effectiveFrom);
        Audit(companyId,
            superseded.Length == 0 ? "settings.tax-buyer-situation-list.published.v1" : "settings.tax-buyer-situation-list.corrected.v1",
            BuyerSituationEntityType, subject, superseded.Length == 0 ? "PublishBuyerSituationList" : "CorrectBuyerSituationList",
            versionBefore, list.Version, scope,
            previous is null ? null : new
            {
                CompanyId = companyId, previous.ListVersion, previous.Source, previous.EffectiveFrom, previous.Entries,
            },
            new
            {
                CompanyId = companyId,
                ListVersion = listVersion,
                EffectiveFrom = effectiveFrom,
                Entries = entries,
                ReplacesListVersions = superseded,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("situation-publish", metadata, subject, await SaveChangeAsync(BuyerSituationPublishCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    public async Task<TaxMasterCommandResultV1> WithdrawBuyerSituationListAsync(long listVersion,
        WithdrawTaxCodeListRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (listVersion < 1 || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Liste sürümü ve beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        const string subject = "buyer-situations";
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ListVersion = listVersion, ExpectedVersion = expectedVersion, Reason = reason,
        });
        if (await FindExecutionAsync(BuyerSituationWithdrawCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("situation-withdraw", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await ReadForCommandAsync(cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var list = rows.SituationList;
        // Only a Settings version can be withdrawn; version 0 is the reviewed catalog (refused above as invalid).
        var target = data.BuyerSituationLists.SingleOrDefault(version =>
            version.ListVersion == listVersion && version.Source == TaxDefaultSourcesV1.Record);
        var refusal = list is null || target is null ? Answers.NotFound()
            : list.Version != expectedVersion ? Answers.VersionConflict()
            : target.IsWithdrawn ? Answers.RecordWithdrawn()
            : target.EffectiveFrom <= today ? Answers.RecordInEffect()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("situation-withdraw", metadata, subject, await StoreRefusalAsync(BuyerSituationWithdrawCommandName, refusal,
                keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("situation-withdraw", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = list!.Version;
        database.TaxBuyerSituationListWithdrawals.Add(new TaxBuyerSituationListWithdrawalRow
        {
            CompanyId = companyId, ListVersion = listVersion, ReplacedByListVersion = null, WithdrawnAtUtc = now,
            WithdrawnBy = metadata.ActorId, Reason = reason,
        });
        list.Version = checked(list.Version + 1);
        list.UpdatedAtUtc = now;
        list.UpdatedBy = metadata.ActorId;
        Audit(companyId, "settings.tax-buyer-situation-list.withdrawn.v1", BuyerSituationEntityType, subject,
            "WithdrawBuyerSituationList", versionBefore, list.Version, scope, null, new
            {
                CompanyId = companyId, WithdrawnListVersion = listVersion, target!.EffectiveFrom, Withdrawn = true,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("situation-withdraw", metadata, subject, await SaveChangeAsync(BuyerSituationWithdrawCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    private static TaxBuyerSituationListViewV1 BuyerSituationView(Guid companyId, DateOnly today, TaxMasterRows rows,
        TaxMasterDataV1 data)
    {
        var versionRows = rows.SituationListVersions.ToDictionary(row => row.ListVersion);
        return new TaxBuyerSituationListViewV1(
            companyId,
            today,
            Text(rows.SituationList?.Version ?? 0),
            TaxMasterCalculatorV1.BuyerSituationListInForce(data, today) is { } inForce
                ? SituationListView(inForce, versionRows)
                : null,
            TaxMasterCalculatorV1.ScheduledBuyerSituationLists(data, today)
                .Select(version => SituationListView(version, versionRows))
                .ToArray());
    }

    private static TaxBuyerSituationListVersionViewV1 SituationListView(TaxBuyerSituationListVersionDataV1 version,
        IReadOnlyDictionary<long, TaxBuyerSituationListVersionRow> rows)
    {
        var row = version.Source == TaxDefaultSourcesV1.Record ? rows.GetValueOrDefault(version.ListVersion) : null;
        return new TaxBuyerSituationListVersionViewV1(
            Text(version.ListVersion),
            version.Source,
            version.EffectiveFrom,
            version.Entries,
            row?.RecordedBy,
            row?.RecordedAtUtc,
            row?.Reason ?? ApprovedTaxBuyerSituationCatalog.Source);
    }

    private static bool TryReadSituations(IReadOnlyList<TaxBuyerSituationEntryV1>? input,
        out IReadOnlyList<TaxBuyerSituationEntryViewV1> entries, out string detail)
    {
        entries = [];
        detail = string.Empty;
        if (input is null || input.Count is 0 || input.Count > TaxMasterInputLimitsV1.BuyerSituationMaximumEntries)
        {
            detail = $"Liste 1-{TaxMasterInputLimitsV1.BuyerSituationMaximumEntries} alıcı durumu içermelidir.";
            return false;
        }

        var result = new List<TaxBuyerSituationEntryViewV1>(input.Count);
        foreach (var entry in input)
        {
            if (entry is null || !TaxMasterCalculatorV1.IsWellFormedSituationCode(entry.Code)
                || !TryNormalizeName(entry.Name, TaxMasterInputLimitsV1.ListEntryNameMaximumLength, out var name)
                || entry.WithholdingApplies is not { } withholdingApplies
                || !TaxBuyerSituationVatModesV1.IsKnown(entry.VatMode)
                || entry.DocumentRequired is not { } documentRequired)
            {
                detail = "Her alıcı durumunda harfle başlayan harf/rakam kod (en çok "
                    + $"{TaxMasterInputLimitsV1.BuyerSituationCodeMaximumLength}), 1-200 karakter ad, tevkifat uygulanıp "
                    + "uygulanmadığı, KDV etkisi (Normal / ZeroWithExemptionCode) ve belge gerekip gerekmediği zorunludur.";
                return false;
            }

            if (entry.VatMode == TaxBuyerSituationVatModesV1.ZeroWithExemptionCode && withholdingApplies)
            {
                detail = "KDV %0 (istisna kodlu) alıcı durumunda tevkifat uygulanmaz.";
                return false;
            }

            result.Add(new TaxBuyerSituationEntryViewV1(entry.Code!, name, withholdingApplies, entry.VatMode!, documentRequired));
        }

        if (result.Select(entry => entry.Code).Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            detail = "Bir alıcı durumu listede bir kez yer alır.";
            return false;
        }

        entries = result.OrderBy(entry => entry.Code, StringComparer.Ordinal).ToArray();
        return true;
    }
}
