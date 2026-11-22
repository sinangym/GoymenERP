using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// B-13 (ADR-SET-002): the K3 administration of the company policy registry behind
/// <see cref="ISettingsCompanyPolicyAdministrationV1"/>.
/// <list type="bullet">
/// <item><b>Finance opening period</b>: open/closed; authority <see cref="SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage"/>
/// (registered, granted to no role; NEEDS-SINAN). Every open or close is a new version, so the version text Finance sees
/// changes each time.</item>
/// <item><b>Manufacturing due-date buffer</b> (URT-D96/URT-D100): whole working days ≥ 0, company default, start value 0
/// without a row; authority <see cref="ErpPermissions.ManufacturingPlansApprove"/> (URT-D100).</item>
/// <item><b>Sales subcontract tax default</b> (SAT-D381): one default VAT/withholding code a line may change; authority
/// <see cref="ErpPermissions.SalesOrdersApprove"/> alone (SAT-D469, splitting SAT-D395 by module; #1189 6947126872).
/// Purchasing uses its separate <see cref="ErpPermissions.PurchasingOrdersApprove"/> authority and record (ALS-D105, SAT-D469).
/// Codes must be in the tax master list in force today.</item>
/// <item><b>Every command</b>: the key's permission company-wide, measured first and re-measured fresh before every save;
/// a mandatory reason (≤ 4000); one Idempotency-Key per command (the stored terminal answer replays; another body under the
/// key is 409); the expected version as decimal text; correlation/causation; one audit event per real change, none for a
/// no-op. Rows are insert-only.</item>
/// </list>
/// </summary>
internal sealed partial class SettingsCompanyPolicyAdministration(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    ILogger<SettingsCompanyPolicyAdministration> logger,
    IAuditTransactionWriterV1? auditWriter = null) : ISettingsCompanyPolicyAdministrationV1, ISettingsSalesContractDiscountLimitAdministrationV1
{
    private const int CommandExecutionSchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static readonly KeySpec FinanceOpeningPeriod = new KeySpec(
        CompanyPolicyKeysV1.FinanceOpeningPeriod,
        SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage,
        "settings.finance-opening-period.record.v1",
        "settings.finance-opening-period.recorded.v1",
        "Settings.FinanceOpeningPeriod",
        "Finans açılış dönemi ayarı sürümü kaydedildi; gerekçe kayıt içeriğindedir");

    private static readonly KeySpec ManufacturingDueDateBuffer = new KeySpec(
        CompanyPolicyKeysV1.ManufacturingDueDateBuffer,
        ErpPermissions.ManufacturingPlansApprove,
        "settings.manufacturing-due-date-buffer.record.v1",
        "settings.manufacturing-due-date-buffer.recorded.v1",
        "Settings.ManufacturingDueDateBuffer",
        "Üretim termin tamponu ayarı sürümü kaydedildi; gerekçe kayıt içeriğindedir");

    private static readonly KeySpec SalesSubcontractTaxDefault = new KeySpec(
        CompanyPolicyKeysV1.SalesSubcontractTaxDefault,
        ErpPermissions.SalesOrdersApprove,
        "settings.sales-subcontract-tax-default.record.v1",
        "settings.sales-subcontract-tax-default.recorded.v1",
        "Settings.SalesSubcontractTaxDefault",
        "Satış fason vergi varsayılanı sürümü kaydedildi; gerekçe kayıt içeriğindedir");

    private static readonly KeySpec PurchasingSubcontractTaxDefault = new KeySpec(
        CompanyPolicyKeysV1.PurchasingSubcontractTaxDefault,
        ErpPermissions.PurchasingOrdersApprove,
        "settings.purchasing-subcontract-tax-default.record.v1",
        "settings.purchasing-subcontract-tax-default.recorded.v1",
        "Settings.PurchasingSubcontractTaxDefault",
        "Alış fason vergi varsayılanı sürümü kaydedildi; gerekçe kayıt içeriğindedir");

    private static readonly KeySpec SalesContractDiscountLimit = new KeySpec(
        CompanyPolicyKeysV1.SalesContractDiscountLimit,
        SettingsSalesContractDiscountLimitPermissionsV1.Manage,
        "settings.sales-contract-discount-limit.record.v1",
        "settings.sales-contract-discount-limit.recorded.v1",
        "Settings.SalesContractDiscountLimit",
        "Sözleşme iskonto sınırı sürümü kaydedildi; gerekçe kayıt içeriğindedir");

    public async Task<SalesContractDiscountLimitViewV1> GetSalesContractDiscountLimitAsync(CancellationToken cancellationToken = default)
    {
        var (companyId, rows) = await ReadHistoryAsync(SalesContractDiscountLimit, cancellationToken);
        var versions = rows.Select(row => new SalesContractDiscountLimitVersionViewV1(Text(row.Version),
            TaxMasterCalculatorV1.Format(ContractRate(row)), row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray();
        return new SalesContractDiscountLimitViewV1(companyId, versions.FirstOrDefault()?.LimitPercent, Text(rows.FirstOrDefault()?.Version ?? 0), versions);
    }

    public async Task<CompanyPolicyCommandResultV1> RecordSalesContractDiscountLimitAsync(RecordSalesContractDiscountLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        try
        {
            if (!TaxMasterCalculatorV1.TryParsePercent(request.LimitPercent, allowZero: true, out var rate))
                return await RecordAsync(SalesContractDiscountLimit, request.ExpectedVersion, request.Reason, metadata,
                    Answers.Invalid("Sözleşme iskonto sınırı 0–100 arasında, en çok 4 ondalıklı oran olmalıdır."), null, cancellationToken);
            return await RecordAsync(SalesContractDiscountLimit, request.ExpectedVersion, request.Reason, metadata, null,
                new Change(new { LimitPercent = rate }, current => current is not null && ContractRate(current) == rate,
                    null, row => row.PercentValue = rate,
                    current => new { LimitPercent = rate, PreviousLimitPercent = current is null ? (decimal?)null : ContractRate(current) }), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException
            and not SettingsPrerequisiteException)
        { throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable); }
        finally { auditWriter.DiscardPending(); database.ChangeTracker.Clear(); }
    }

    private static decimal ContractRate(CompanyPolicyVersionRow row) =>
        row.PercentValue is { } rate && rate >= 0 && rate <= 100 && decimal.Round(rate, 4) == rate
            && row.BooleanValue is null && row.WholeNumberValue is null && row.VatApplies is null && row.VatPercent is null
            && row.ExemptionCode is null && row.WithholdingApplies is null && row.WithholdingPercent is null && row.WithholdingCode is null
        ? rate : throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);

    public async Task<FinanceOpeningPeriodViewV1> GetFinanceOpeningPeriodAsync(CancellationToken cancellationToken = default)
    {
        var (companyId, rows) = await ReadHistoryAsync(FinanceOpeningPeriod, cancellationToken);
        var latest = rows.Length == 0 ? null : rows[0];
        var state = latest?.BooleanValue switch
        {
            null => FinanceOpeningPeriodStateV1.NotRecorded,
            true => FinanceOpeningPeriodStateV1.Open,
            false => FinanceOpeningPeriodStateV1.Closed,
        };
        return new FinanceOpeningPeriodViewV1(companyId, state, Text(latest?.Version ?? 0),
            rows.Select(row => new FinanceOpeningPeriodVersionViewV1(Text(row.Version), row.BooleanValue == true,
                row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray());
    }

    public Task<CompanyPolicyCommandResultV1> RecordFinanceOpeningPeriodAsync(RecordFinanceOpeningPeriodRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Open is not { } open)
        {
            return RecordAsync(FinanceOpeningPeriod, request.ExpectedVersion, request.Reason, metadata,
                Answers.Invalid("\"Açılış dönemi açık\" seçimi (evet/hayır) zorunludur."), null, cancellationToken);
        }

        return RecordAsync(FinanceOpeningPeriod, request.ExpectedVersion, request.Reason, metadata, null, new Change(
            new { Open = open },
            current => current?.BooleanValue == open,
            null,
            row => row.BooleanValue = open,
            current => new { Open = open, PreviousOpen = current?.BooleanValue }), cancellationToken);
    }

    public async Task<ManufacturingDueDateBufferViewV1> GetManufacturingDueDateBufferAsync(
        CancellationToken cancellationToken = default)
    {
        var (companyId, rows) = await ReadHistoryAsync(ManufacturingDueDateBuffer, cancellationToken);
        var latest = rows.Length == 0 ? null : rows[0];
        return new ManufacturingDueDateBufferViewV1(companyId,
            latest?.WholeNumberValue ?? CompanyPolicyInputLimitsV1.DefaultDueDateBufferWorkingDays,
            Text(latest?.Version ?? 0),
            rows.Select(row => new ManufacturingDueDateBufferVersionViewV1(Text(row.Version), row.WholeNumberValue ?? 0,
                row.RecordedBy, row.RecordedAtUtc, row.Reason)).ToArray());
    }

    public Task<CompanyPolicyCommandResultV1> RecordManufacturingDueDateBufferAsync(
        RecordManufacturingDueDateBufferRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkingDays is not { } days || !CompanyPolicyRulesV1.IsValidDueDateBuffer(days))
        {
            return RecordAsync(ManufacturingDueDateBuffer, request.ExpectedVersion, request.Reason, metadata,
                Answers.Invalid("Termin tamponu 0 veya pozitif tam iş günü olarak girilir."), null, cancellationToken);
        }

        return RecordAsync(ManufacturingDueDateBuffer, request.ExpectedVersion, request.Reason, metadata, null, new Change(
            new { WorkingDays = days },
            current => (current?.WholeNumberValue ?? CompanyPolicyInputLimitsV1.DefaultDueDateBufferWorkingDays) == days,
            null,
            row => row.WholeNumberValue = days,
            current => new
            {
                WorkingDays = days,
                PreviousWorkingDays = current?.WholeNumberValue ?? CompanyPolicyInputLimitsV1.DefaultDueDateBufferWorkingDays,
            }), cancellationToken);
    }

    public Task<SubcontractTaxDefaultViewV1> GetSalesSubcontractTaxDefaultAsync(CancellationToken cancellationToken = default) =>
        ReadSubcontractTaxDefaultAsync(SalesSubcontractTaxDefault, cancellationToken);

    public Task<SubcontractTaxDefaultViewV1> GetPurchasingSubcontractTaxDefaultAsync(CancellationToken cancellationToken = default) =>
        ReadSubcontractTaxDefaultAsync(PurchasingSubcontractTaxDefault, cancellationToken);

    private async Task<SubcontractTaxDefaultViewV1> ReadSubcontractTaxDefaultAsync(KeySpec spec, CancellationToken cancellationToken)
    {
        var (companyId, rows) = await ReadHistoryAsync(spec, cancellationToken);
        var versions = rows.Select(row => (Row: row, Value: CompanyPolicyRowValues.TaxDefault(row))).ToArray();
        if (versions.Any(version => version.Value is null))
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        }

        return new SubcontractTaxDefaultViewV1(
            companyId,
            versions.Length == 0 ? null : View(versions[0].Value!),
            Text(versions.Length == 0 ? 0 : versions[0].Row.Version),
            versions.Select(version => new SubcontractTaxDefaultVersionViewV1(Text(version.Row.Version),
                View(version.Value!), version.Row.RecordedBy, version.Row.RecordedAtUtc, version.Row.Reason)).ToArray());
    }

    public Task<CompanyPolicyCommandResultV1> RecordSalesSubcontractTaxDefaultAsync(
        RecordSubcontractTaxDefaultRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default) =>
        RecordSubcontractTaxDefaultAsync(SalesSubcontractTaxDefault, request, metadata, cancellationToken);

    public Task<CompanyPolicyCommandResultV1> RecordPurchasingSubcontractTaxDefaultAsync(
        RecordSubcontractTaxDefaultRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default) =>
        RecordSubcontractTaxDefaultAsync(PurchasingSubcontractTaxDefault, request, metadata, cancellationToken);

    private Task<CompanyPolicyCommandResultV1> RecordSubcontractTaxDefaultAsync(KeySpec spec,
        RecordSubcontractTaxDefaultRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.VatApplies is not { } vatApplies || request.WithholdingApplies is not { } withholdingApplies)
        {
            return RecordAsync(spec, request.ExpectedVersion, request.Reason, metadata,
                Answers.Invalid("\"KDV uygulanır\" ve \"tevkifat uygulanır\" seçimleri (evet/hayır) zorunludur."), null,
                cancellationToken);
        }

        if (!CompanyPolicyRulesV1.TryReadTaxDefault(vatApplies, request.VatPercent, request.ExemptionCode, withholdingApplies,
                request.WithholdingPercent, request.WithholdingCode, out var value, out var detail))
        {
            return RecordAsync(spec, request.ExpectedVersion, request.Reason, metadata,
                Answers.Invalid(detail), null, cancellationToken);
        }

        return RecordAsync(spec, request.ExpectedVersion, request.Reason, metadata, null, new Change(
            new
            {
                value.VatApplies,
                VatPercent = value.VatPercent is { } vat ? TaxMasterCalculatorV1.Format(vat) : null,
                value.ExemptionCode,
                value.WithholdingApplies,
                WithholdingPercent = value.WithholdingPercent is { } withholding ? TaxMasterCalculatorV1.Format(withholding) : null,
                value.WithholdingCode,
            },
            current => current is not null && CompanyPolicyRowValues.TaxDefault(current) == value,
            token => CheckListedCodesAsync(value, metadata.CompanyId, token),
            row =>
            {
                row.VatApplies = value.VatApplies;
                row.VatPercent = value.VatPercent;
                row.ExemptionCode = value.ExemptionCode;
                row.WithholdingApplies = value.WithholdingApplies;
                row.WithholdingPercent = value.WithholdingPercent;
                row.WithholdingCode = value.WithholdingCode;
            },
            current => new
            {
                Value = View(value),
                Previous = current is null ? null : CompanyPolicyRowValues.TaxDefault(current) is { } previous
                    ? View(previous)
                    : null,
            }), cancellationToken);
    }

    /// <summary>SAT-D381: a code is accepted only while it is in the company's tax master list in force today.</summary>
    private async Task<CompanyPolicyCommandAnswerV1?> CheckListedCodesAsync(SettingsSubcontractTaxDefaultV1 value,
        Guid companyId, CancellationToken cancellationToken)
    {
        var data = (await ReadAsync(() => TaxMasterRows.ReadAsync(database, track: false, cancellationToken))).ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var versions = (await ReadAsync(() => TaxRateListRows.ReadAsync(database, track: false, cancellationToken))).ToVersions(companyId);
        var vatRates = ApprovedTaxRateListCatalog.InForce(versions, TaxRateListKindsV1.VatRate, today);
        var ratios = ApprovedTaxRateListCatalog.InForce(versions, TaxRateListKindsV1.WithholdingRatio, today);
        if (value.VatApplies && vatRates?.Entries.Any(x => x.Percent == value.VatPercent) != true
            || value.WithholdingApplies && ratios?.Entries.Any(x => x.Percent == value.WithholdingPercent && x.TaxCode == value.WithholdingCode) != true)
            return Answers.Problem(422, CompanyPolicyProblemCodesV1.RateUnknown, "Vergi oranı listede yok.",
                "KDV ve tevkifat oranı bugün yürürlükteki gerçek Ayarlar listesinden seçilmelidir.");
        if (value.ExemptionCode is { } exemption
            && !TaxMasterCalculatorV1.IsListed(data, TaxCodeListKindsV1.Exemption, exemption, today))
        {
            return Answers.Problem(422, CompanyPolicyProblemCodesV1.ExemptionCodeUnknown, "İstisna kodu listede yok.",
                "İstisna kodu bugün yürürlükteki istisna listesinde bulunmuyor.");
        }

        if (value.WithholdingCode is { } withholdingCode
            && !TaxMasterCalculatorV1.IsListed(data, TaxCodeListKindsV1.Withholding, withholdingCode, today))
        {
            return Answers.Problem(422, CompanyPolicyProblemCodesV1.WithholdingCodeUnknown, "Tevkifat kodu listede yok.",
                "Tevkifat kodu bugün yürürlükteki tevkifat listesinde bulunmuyor.");
        }

        return null;
    }

    public async Task<SubcontractTaxOptionsV1> GetSubcontractTaxOptionsAsync(SubcontractTaxSideV1 side, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(side)) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);
        var spec = side == SubcontractTaxSideV1.Sales ? SalesSubcontractTaxDefault : PurchasingSubcontractTaxDefault;
        var (companyId, _) = await ReadHistoryAsync(spec, cancellationToken);
        var actor = currentUser.UserId;
        var result = await new SettingsSubcontractTaxCatalogV1(database, companyContext, currentUser)
            .GetAsync(companyId, side, SettingsBusinessDate.Today(timeProvider), cancellationToken);
        await RequireStillAsync(spec.Permission, companyId, cancellationToken);
        if (RequireContextCompany() != companyId || currentUser.UserId != actor) throw new UnauthorizedAccessException();
        return result;
    }

    private static SubcontractTaxDefaultValueViewV1 View(SettingsSubcontractTaxDefaultV1 value) =>
        new SubcontractTaxDefaultValueViewV1(
            value.VatApplies,
            value.VatPercent is { } vat ? TaxMasterCalculatorV1.Format(vat) : null,
            value.ExemptionCode,
            value.WithholdingApplies,
            value.WithholdingPercent is { } withholding ? TaxMasterCalculatorV1.Format(withholding) : null,
            value.WithholdingCode);

    private async Task<(Guid CompanyId, CompanyPolicyVersionRow[] Rows)> ReadHistoryAsync(KeySpec spec,
        CancellationToken cancellationToken)
    {
        var companyId = RequireContextCompany();
        var actor = currentUser.UserId;
        await RequireAsync(spec.Permission, companyId, cancellationToken);
        if (RequireContextCompany() != companyId || currentUser.UserId != actor) throw new UnauthorizedAccessException();
        var rows = await ReadAsync(() => CompanyPolicyReads.LoadAsync(database, spec.Key, cancellationToken));
        await RequireStillAsync(spec.Permission, companyId, cancellationToken);
        if (RequireContextCompany() != companyId || currentUser.UserId != actor) throw new UnauthorizedAccessException();
        return (companyId, rows);
    }

    /// <summary>
    /// The shared K3 record path. <paramref name="invalid"/> is a request refusal found before any read (not stored);
    /// otherwise <paramref name="change"/> describes the key's value.
    /// </summary>
    private async Task<CompanyPolicyCommandResultV1> RecordAsync(KeySpec spec, string? expectedVersionText, string? reasonText,
        SettingsCommandMetadataV1 metadata, CompanyPolicyCommandAnswerV1? invalid, Change? change,
        CancellationToken cancellationToken)
    {
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(spec.Permission, companyId, cancellationToken);
        if (!TryParseVersion(expectedVersionText, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (invalid is not null || change is null)
        {
            return Unstored(invalid ?? Answers.Invalid("İstek geçersiz."), metadata);
        }

        if (ReasonRefusal(reasonText, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = string.Create(CultureInfo.InvariantCulture, $"{companyId:D}:{spec.Key}");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            spec.Key,
            ExpectedVersion = expectedVersion,
            Value = change.Payload,
            Reason = reason,
        });
        if (await FindExecutionAsync(spec, keyHash, cancellationToken) is { } existing)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject, Replay(existing, requestHash, metadata));
        }

        var current = await ReadAsync(() => CompanyPolicyReads.LatestAsync(database, spec.Key, cancellationToken));
        var latest = current?.Version ?? 0;
        if (latest != expectedVersion)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject,
                await StoreRefusalAsync(spec, Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (change.Check is not null && await change.Check(cancellationToken) is { } refusal)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject,
                await StoreRefusalAsync(spec, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (change.SameAsInForce(current))
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject,
                await StoreRefusalAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        var version = checked(latest + 1);
        var row = new CompanyPolicyVersionRow
        {
            CompanyId = companyId,
            PolicyKey = spec.Key,
            Version = version,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        change.Fill(row);
        database.CompanyPolicyVersions.Add(row);
        Audit(spec, companyId, subject, latest == 0 ? null : latest, version, scope, new
        {
            CompanyId = companyId,
            PolicyKey = spec.Key,
            row.Version,
            Change = change.After(current),
        }, reason, metadata);
        await RequireReplayStillAsync(spec, metadata, cancellationToken);
        return Done(spec, metadata, subject, await SaveChangeAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private Guid RequireContextCompany()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException();
        }

        return companyContext.CompanyId;
    }

    private async Task<PermissionScope> RequireAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await authorization.GetPermissionScopeAsync(permission, companyId, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true } ? scope : throw new UnauthorizedAccessException();
    }

    /// <summary>The same measurement, fresh from the authority rows (a grant revoked meanwhile is seen).</summary>
    private async Task RequireStillAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        if (!currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException();
        }

        var scope = await revalidation.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (scope is not { IsGranted: true, IsCompanyWide: true })
        {
            throw new UnauthorizedAccessException();
        }
    }

    private async Task RequireReplayStillAsync(KeySpec spec, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken)
    {
        RequireOriginalIdentity();
        await RequireStillAsync(spec.Permission, metadata.CompanyId, cancellationToken);
        RequireOriginalIdentity();

        void RequireOriginalIdentity()
        {
            if (!currentUser.IsAuthenticated || !companyContext.IsResolved || companyContext.CompanyId != metadata.CompanyId
                || !string.Equals(currentUser.UserId, metadata.ActorId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException();
        }
    }

    /// <summary>A read of the store; an unreadable store is <c>settings_provider_unavailable</c> (503), never a 500.</summary>
    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    private void ValidateMetadata(SettingsCommandMetadataV1 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Ayarlar komut bağlamı geçersiz.");
        }
    }

    private async Task<CommandExecution?> FindExecutionAsync(KeySpec spec, string keyHash, CancellationToken cancellationToken)
    {
        try
        {
            return await database.CommandExecutions.AsNoTracking()
                .SingleOrDefaultAsync(execution => execution.CommandName == spec.CommandName && execution.IdempotencyKeyHash == keyHash,
                    cancellationToken);
        }
        catch (Exception exception) when (exception is SqlException
            or InvalidOperationException { InnerException: SqlException })
        {
            // Covers direct and EF-wrapped receipt reads, including reads inside a save-race catch.
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    private static CompanyPolicyCommandResultV1 Replay(CommandExecution execution, string requestHash,
        SettingsCommandMetadataV1 metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new CompanyPolicyCommandResultV1(Answers.Problem(409, CompanyPolicyProblemCodesV1.IdempotencyConflict,
                    "İşlem anahtarı kullanılmış.", "Bu Idempotency-Key başka bir istekle kullanıldı."),
                false, metadata.CorrelationId);
        }

        var answer = JsonSerializer.Deserialize<CompanyPolicyCommandAnswerV1>(execution.ResponseJson, Json);
        if (answer is null || answer.StatusCode != execution.HttpStatusCode
            || !string.Equals(Outcome(answer), execution.Outcome, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kalıcı idempotency sonucu tanımlı şirket politikası komut sözleşmesiyle uyumsuz.");
        }

        return new CompanyPolicyCommandResultV1(answer, true, execution.OriginalCorrelationId);
    }

    private void AddExecution(KeySpec spec, CompanyPolicyCommandAnswerV1 answer, string keyHash, string requestHash,
        SettingsCommandMetadataV1 metadata) =>
        database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            spec.CommandName,
            keyHash,
            requestHash,
            metadata.ActorId,
            Outcome(answer),
            answer.StatusCode,
            JsonSerializer.Serialize(answer, Json),
            metadata.CorrelationId,
            timeProvider.GetUtcNow(),
            CommandExecutionSchemaVersion)));

    /// <summary>Stores a refusal or no-op (no business change) as the key's terminal answer; a same-key winner replays.</summary>
    private async Task<CompanyPolicyCommandResultV1> StoreRefusalAsync(KeySpec spec, CompanyPolicyCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        AddExecution(spec, answer, keyHash, requestHash, metadata);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new CompanyPolicyCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(spec, keyHash, cancellationToken);
            if (winner is null)
            {
                throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
            }

            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Replay(winner, requestHash, metadata);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException
            or InvalidOperationException { InnerException: DbUpdateException or SqlException })
        {
            // A provider failure is not a terminal business refusal and never consumes the retry key.
            database.ChangeTracker.Clear();
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    /// <summary>
    /// Saves the new version with its terminal answer and audit in one unit. A lost race (the version primary key) re-reads:
    /// a same-key winner replays, otherwise <paramref name="onConflict"/> is stored as the key's answer.
    /// </summary>
    private async Task<CompanyPolicyCommandResultV1> SaveChangeAsync(KeySpec spec, CompanyPolicyCommandAnswerV1 answer,
        string keyHash, string requestHash, SettingsCommandMetadataV1 metadata, CompanyPolicyCommandAnswerV1 onConflict,
        CancellationToken cancellationToken)
    {
        AddExecution(spec, answer, keyHash, requestHash, metadata);
        if (UsesAuditOwner(spec))
            return await SaveContractChangeOwnedAsync(spec, answer, keyHash, requestHash, metadata, onConflict, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new CompanyPolicyCommandResultV1(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(spec, keyHash, cancellationToken);
            if (winner is not null)
            {
                await RequireReplayStillAsync(spec, metadata, cancellationToken);
                return Replay(winner, requestHash, metadata);
            }

            return await StoreRefusalAsync(spec, onConflict, keyHash, requestHash, metadata, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException
            or InvalidOperationException { InnerException: DbUpdateException or SqlException })
        {
            // A provider failure is not a terminal business refusal and never consumes the retry key.
            database.ChangeTracker.Clear();
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    private async Task<CompanyPolicyCommandResultV1> SaveContractChangeOwnedAsync(KeySpec spec,
        CompanyPolicyCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        CompanyPolicyCommandAnswerV1 onConflict, CancellationToken cancellationToken)
    {
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        var conflict = false;
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            // The owner clears the collector BEFORE Settings' legacy interceptor can attach Audit rows.
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
            { conflict = true; }
            if (conflict) await transaction.RollbackAsync(CancellationToken.None);
            else { await RequireReplayStillAsync(spec, metadata, cancellationToken); await transaction.CommitAsync(cancellationToken); }
        }
        if (!conflict) return new CompanyPolicyCommandResultV1(answer, false, metadata.CorrelationId);
        database.ChangeTracker.Clear();
        var winner = await FindExecutionAsync(spec, keyHash, cancellationToken);
        if (winner is not null)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Replay(winner, requestHash, metadata);
        }
        await RequireReplayStillAsync(spec, metadata, cancellationToken);
        return await StoreRefusalAsync(spec, onConflict, keyHash, requestHash, metadata, cancellationToken);
    }

    private static string Outcome(CompanyPolicyCommandAnswerV1 answer) => answer.Code ?? answer.StatusCode switch
    {
        204 => "Succeeded",
        _ => "Refused",
    };

    /// <summary>One audit event: the person's whole reason in the after-image, a fixed description in the reason column.</summary>
    private void Audit(KeySpec spec, Guid companyId, string entityId, long? versionBefore, long? versionAfter,
        PermissionScope scope, object after, string reason, SettingsCommandMetadataV1 metadata) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            spec.RecordedEvent,
            spec.EntityType,
            entityId,
            "RecordVersion",
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            spec.Permission,
            scope.RoleCodes,
            "{}",
            AfterJson(after, reason),
            spec.AuditDescription,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

    private static string AfterJson(object after, string reason)
    {
        var node = JsonSerializer.SerializeToNode(after, Json)!.AsObject();
        node["reason"] = reason;
        return node.ToJsonString(Json);
    }

    private CompanyPolicyCommandResultV1 Done(KeySpec spec, SettingsCommandMetadataV1 metadata, string subject,
        CompanyPolicyCommandResultV1 result)
    {
        logger.LogInformation(
            "Settings company policy command completed. Command={Command} CompanyId={CompanyId} Subject={Subject} ActorId={ActorId} CorrelationId={CorrelationId} Status={Status} Code={Code} IsReplay={IsReplay}",
            spec.CommandName, metadata.CompanyId, subject, metadata.ActorId, metadata.CorrelationId, result.Answer.StatusCode,
            result.Answer.Code, result.IsReplay);
        return result;
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A version as decimal text: digits only, no sign, no leading zero (other than "0"), within Int64.</summary>
    private static bool TryParseVersion(string? text, long minimum, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= minimum;
    }

    private static CompanyPolicyCommandAnswerV1? ReasonRefusal(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Answers.Problem(400, CompanyPolicyProblemCodesV1.ReasonRequired, "Gerekçe gerekli.",
                "Bu değişiklik için gerekçe zorunludur.");
        }

        return normalized.Length > CompanyPolicyInputLimitsV1.ReasonMaximumLength
            ? Answers.Problem(400, CompanyPolicyProblemCodesV1.ReasonTooLong, "Gerekçe çok uzun.",
                $"Gerekçe en fazla {CompanyPolicyInputLimitsV1.ReasonMaximumLength.ToString(CultureInfo.InvariantCulture)} karakter olabilir.")
            : null;
    }

    private static CompanyPolicyCommandResultV1 Unstored(CompanyPolicyCommandAnswerV1 answer,
        SettingsCommandMetadataV1 metadata) =>
        new CompanyPolicyCommandResultV1(answer, false, metadata.CorrelationId);

    private sealed record KeySpec(string Key, string Permission, string CommandName, string RecordedEvent, string EntityType,
        string AuditDescription);

    /// <summary>
    /// One requested value: its idempotency payload, whether it equals the value in force (a no-op), an optional stored
    /// refusal check, how it fills the new row and the audit after-image.
    /// </summary>
    private sealed record Change(
        object Payload,
        Func<CompanyPolicyVersionRow?, bool> SameAsInForce,
        Func<CancellationToken, Task<CompanyPolicyCommandAnswerV1?>>? Check,
        Action<CompanyPolicyVersionRow> Fill,
        Func<CompanyPolicyVersionRow?, object> After);

    private static class Answers
    {
        public static CompanyPolicyCommandAnswerV1 NoContent() =>
            new CompanyPolicyCommandAnswerV1(204, null, "Kaydedildi.", "Değişiklik kaydedildi.");

        public static CompanyPolicyCommandAnswerV1 Invalid(string detail) =>
            Problem(400, CompanyPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);

        public static CompanyPolicyCommandAnswerV1 VersionConflict() =>
            Problem(409, CompanyPolicyProblemCodesV1.VersionConflict, "Kayıt değişmiş.",
                "Kayıt siz açtıktan sonra değişti; güncel hâlini okuyup yeniden deneyin.");

        public static CompanyPolicyCommandAnswerV1 Problem(int status, string code, string title, string detail) =>
            new CompanyPolicyCommandAnswerV1(status, code, title, detail);
    }
}
