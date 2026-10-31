using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The withholding amount threshold (VUK 232 fatura sınırı, SAT-D135, EK19): a dated Settings value that starts
/// EMPTY and is entered by an authorized person (amount + the currency it is in, never assumed). The same timeline rules as
/// the rate records: a later date applies by itself, a same-date record replaces a not-yet-effective one, a not-yet-effective
/// record can be withdrawn, a record in force never changes.
/// </summary>
internal sealed partial class SettingsTaxMasterAdministration
{
    private const string ThresholdEntityType = "Settings.TaxWithholdingThreshold";
    private const string ThresholdRecordCommandName = "settings.tax-withholding-threshold.record.v1";
    private const string ThresholdWithdrawCommandName = "settings.tax-withholding-threshold.withdraw.v1";

    public async Task<TaxMasterCommandResultV1> RecordWithholdingThresholdAsync(RecordWithholdingThresholdRequestV1 request,
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

        if (!TaxMasterCalculatorV1.TryParseAmount(request.Amount, out var amount)
            || !TaxMasterCalculatorV1.TryNormalizeCurrencyCode(request.CurrencyCode, out var currencyCode))
        {
            return Unstored(Answers.Invalid(
                "Tutar 0'dan büyük, en çok 15 tam ve 4 ondalık basamaklı sayı metni; para birimi üç harfli ISO 4217 kodu olmalıdır."),
                metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = companyId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion, Amount = amount, CurrencyCode = currencyCode, Reason = reason, request.EffectiveFrom,
        });
        if (await FindExecutionAsync(ThresholdRecordCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("threshold-record", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await ReadForCommandAsync(cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var header = rows.Threshold;
        var effectiveFrom = request.EffectiveFrom ?? today;
        var refusal = (header?.Version ?? 0) != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("threshold-record", metadata, subject,
                await StoreRefusalAsync(ThresholdRecordCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (TaxMasterCalculatorV1.ThresholdInForce(data, effectiveFrom) is { } inForce && inForce.Amount == amount
            && string.Equals(inForce.CurrencyCode, currencyCode, StringComparison.Ordinal))
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("threshold-record", metadata, subject, await StoreRefusalAsync(ThresholdRecordCommandName,
                Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("threshold-record", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = header?.Version;
        if (header is null)
        {
            header = new TaxWithholdingThresholdRow
            {
                CompanyId = companyId, Version = 1, LastRecordVersion = 1, UpdatedAtUtc = now, UpdatedBy = metadata.ActorId,
            };
            database.TaxWithholdingThresholds.Add(header);
        }
        else
        {
            header.Version = checked(header.Version + 1);
            header.LastRecordVersion = checked(header.LastRecordVersion + 1);
            header.UpdatedAtUtc = now;
            header.UpdatedBy = metadata.ActorId;
        }

        var record = new TaxWithholdingThresholdRecordRow
        {
            CompanyId = companyId,
            Id = Guid.NewGuid(),
            RecordVersion = header.LastRecordVersion,
            EffectiveFrom = effectiveFrom,
            Amount = amount,
            CurrencyCode = currencyCode,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId,
            Reason = reason,
        };
        database.TaxWithholdingThresholdRecords.Add(record);
        var superseded = data.Thresholds
            .Where(existingRecord => !existingRecord.IsWithdrawn && existingRecord.EffectiveFrom == effectiveFrom
                                     && existingRecord.EffectiveFrom > today)
            .Select(existingRecord => existingRecord.RecordId)
            .ToArray();
        foreach (var replaced in superseded)
        {
            database.TaxWithholdingThresholdWithdrawals.Add(new TaxWithholdingThresholdWithdrawalRow
            {
                CompanyId = companyId, RecordId = replaced, ReplacedByRecordId = record.Id, WithdrawnAtUtc = now,
                WithdrawnBy = metadata.ActorId, Reason = reason,
            });
        }

        Audit(companyId,
            superseded.Length == 0 ? "settings.tax-withholding-threshold.recorded.v1" : "settings.tax-withholding-threshold.corrected.v1",
            ThresholdEntityType, subject, superseded.Length == 0 ? "RecordThreshold" : "CorrectThreshold", versionBefore,
            header.Version, scope, null, new
            {
                CompanyId = companyId,
                RecordId = record.Id,
                record.RecordVersion,
                record.EffectiveFrom,
                Amount = TaxMasterCalculatorV1.Format(amount),
                CurrencyCode = currencyCode,
                ReplacesRecordIds = superseded,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("threshold-record", metadata, subject, await SaveChangeAsync(ThresholdRecordCommandName, Answers.NoContent(),
            keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    public async Task<TaxMasterCommandResultV1> WithdrawWithholdingThresholdAsync(Guid recordId,
        WithdrawTaxRecordRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(companyId, cancellationToken);
        if (recordId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Kayıt ve beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = recordId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { RecordId = recordId, ExpectedVersion = expectedVersion, Reason = reason });
        if (await FindExecutionAsync(ThresholdWithdrawCommandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("threshold-withdraw", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var rows = await ReadForCommandAsync(cancellationToken);
        var data = rows.ToData(companyId);
        var today = SettingsBusinessDate.Today(timeProvider);
        var header = rows.Threshold;
        var target = data.Thresholds.SingleOrDefault(record => record.RecordId == recordId);
        var refusal = header is null || target is null ? Answers.NotFound()
            : header.Version != expectedVersion ? Answers.VersionConflict()
            : target.IsWithdrawn ? Answers.RecordWithdrawn()
            : target.EffectiveFrom <= today ? Answers.RecordInEffect()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(companyId, cancellationToken);
            return Done("threshold-withdraw", metadata, subject,
                await StoreRefusalAsync(ThresholdWithdrawCommandName, refusal, keyHash, requestHash, metadata, cancellationToken));
        }

        if (SettingsBusinessDate.Today(timeProvider) != today)
        {
            return Done("threshold-withdraw", metadata, subject, Unstored(Answers.VersionConflict(), metadata));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = header!.Version;
        database.TaxWithholdingThresholdWithdrawals.Add(new TaxWithholdingThresholdWithdrawalRow
        {
            CompanyId = companyId, RecordId = recordId, ReplacedByRecordId = null, WithdrawnAtUtc = now,
            WithdrawnBy = metadata.ActorId, Reason = reason,
        });
        header.Version = checked(header.Version + 1);
        header.UpdatedAtUtc = now;
        header.UpdatedBy = metadata.ActorId;
        Audit(companyId, "settings.tax-withholding-threshold.withdrawn.v1", ThresholdEntityType, companyId.ToString("D"),
            "WithdrawThreshold", versionBefore, header.Version, scope, null, new
            {
                CompanyId = companyId, WithdrawnRecordId = recordId, target!.RecordVersion, target.EffectiveFrom, Withdrawn = true,
            }, reason, metadata);
        await RequireStillAsync(companyId, cancellationToken);
        return Done("threshold-withdraw", metadata, subject, await SaveChangeAsync(ThresholdWithdrawCommandName,
            Answers.NoContent(), keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }
}
