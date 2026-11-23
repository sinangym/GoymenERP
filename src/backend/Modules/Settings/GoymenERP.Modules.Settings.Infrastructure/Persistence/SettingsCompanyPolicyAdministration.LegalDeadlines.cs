using System.Globalization;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16, YOL-D4 (c)): the company's legal deadline calendar on its own authority
/// (<see cref="SettingsLegalDeadlinePermissionsV1.CalendarManage"/>). The same K3 path as the holiday calendar: company-wide
/// permission measured first and fresh before every save, mandatory reason, one Idempotency-Key per command (stored terminal
/// answer, another body is 409), expected version, one audit event per real change, insert-only. Nothing is seeded: the
/// values come after the financial advisor's confirmation (MALI_MUSAVIR_SORULARI_v1).
/// </summary>
internal sealed partial class SettingsCompanyPolicyAdministration : ISettingsLegalDeadlineCalendarAdministrationV1
{
    /// <summary>The spec key names the command family; calendar rows live in their own tables, not the policy registry.</summary>
    private const string LegalDeadlineCalendarSpecKey = "legal.deadline-calendar";

    private static readonly KeySpec LegalDeadlineCalendar = new(LegalDeadlineCalendarSpecKey,
        SettingsLegalDeadlinePermissionsV1.CalendarManage, "settings.legal-deadline-calendar.record.v1",
        "settings.legal-deadline-calendar.recorded.v1", "Settings.LegalDeadlineCalendar", "Yasal süre takvimi sürümü kaydedildi");

    public async Task<LegalDeadlineCalendarViewV1> GetLegalDeadlineCalendarAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireContextCompany();
        var actor = currentUser.UserId;
        await RequireAsync(LegalDeadlineCalendar.Permission, companyId, cancellationToken);
        var versions = await ReadAsync(() => database.LegalDeadlineCalendarVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId)
            .OrderByDescending(row => row.Version).ToArrayAsync(cancellationToken));
        var latest = versions.FirstOrDefault();
        var entries = latest is null
            ? []
            : await ReadAsync(() => database.LegalDeadlineCalendarEntries.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Version == latest.Version)
                .OrderBy(row => row.Obligation).ToArrayAsync(cancellationToken));
        await RequireStillAsync(LegalDeadlineCalendar.Permission, companyId, cancellationToken);
        if (RequireContextCompany() != companyId || currentUser.UserId != actor) throw new UnauthorizedAccessException();
        if (latest is not null && (entries.Length != latest.EntryCount || entries.Any(entry => !LegalDeadlineCalculatorV1.IsValid(Entry(entry)))))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, latest is not null, Text(latest?.Version ?? 0),
            entries.Select(entry => new LegalDeadlineEntryViewV1(((LegalDeadlineObligationV1)entry.Obligation).ToString(),
                entry.PeriodMonths, entry.MonthsAfterPeriodEnd, entry.DueDayOfMonth)).ToArray(),
            versions.Select(row => new LegalDeadlineCalendarVersionViewV1(Text(row.Version), row.EntryCount, row.RecordedBy,
                row.RecordedAtUtc, row.Reason)).ToArray());
    }

    public async Task<CompanyPolicyCommandResultV1> RecordLegalDeadlineCalendarAsync(RecordLegalDeadlineCalendarRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        ValidateMetadata(metadata);
        var spec = LegalDeadlineCalendar;
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(spec.Permission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        if (!TryReadLegalDeadlineEntries(request.Entries, out var entries, out var detail))
            return Unstored(Answers.Invalid(detail), metadata);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
            return Unstored(reasonRefusal, metadata);

        var subject = string.Create(CultureInfo.InvariantCulture, $"{companyId:D}:{spec.Key}");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var payload = entries.Select(entry => new
        {
            Obligation = entry.Obligation.ToString(),
            entry.PeriodMonths,
            entry.MonthsAfterPeriodEnd,
            entry.DueDayOfMonth,
        }).ToArray();
        var requestHash = IdempotencyHash.ComputePayload(new { spec.Key, ExpectedVersion = expectedVersion, Entries = payload, Reason = reason });
        if (await FindExecutionAsync(spec, keyHash, cancellationToken) is { } existing)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject, Replay(existing, requestHash, metadata));
        }

        var latest = await ReadAsync(() => database.LegalDeadlineCalendarVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId)
            .OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken));
        var latestVersion = latest?.Version ?? 0;
        if (latestVersion != expectedVersion)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject,
                await StoreRefusalAsync(spec, Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken));
        }

        if (latest is not null)
        {
            var current = await ReadAsync(() => database.LegalDeadlineCalendarEntries.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Version == latest.Version)
                .OrderBy(row => row.Obligation).ToArrayAsync(cancellationToken));
            if (current.Select(Entry).SequenceEqual(entries))
            {
                await RequireReplayStillAsync(spec, metadata, cancellationToken);
                return Done(spec, metadata, subject,
                    await StoreRefusalAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
            }
        }

        var version = checked(latestVersion + 1);
        database.LegalDeadlineCalendarVersions.Add(new LegalDeadlineCalendarVersionRow
        {
            CompanyId = companyId,
            Version = version,
            EntryCount = entries.Count,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        });
        foreach (var entry in entries)
            database.LegalDeadlineCalendarEntries.Add(new LegalDeadlineCalendarEntryRow
            {
                CompanyId = companyId,
                Version = version,
                Obligation = (int)entry.Obligation,
                PeriodMonths = entry.PeriodMonths,
                MonthsAfterPeriodEnd = entry.MonthsAfterPeriodEnd,
                DueDayOfMonth = entry.DueDayOfMonth,
            });
        Audit(spec, companyId, subject, latestVersion == 0 ? null : latestVersion, version, scope,
            new { CompanyId = companyId, Version = version, Entries = payload }, reason, metadata);
        await RequireReplayStillAsync(spec, metadata, cancellationToken);
        return Done(spec, metadata, subject, await SaveChangeAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static SettingsLegalDeadlineEntryV1 Entry(LegalDeadlineCalendarEntryRow row) =>
        new((LegalDeadlineObligationV1)row.Obligation, row.PeriodMonths, row.MonthsAfterPeriodEnd, row.DueDayOfMonth);

    private static bool TryReadLegalDeadlineEntries(IReadOnlyList<RecordLegalDeadlineEntryV1>? input,
        out IReadOnlyList<SettingsLegalDeadlineEntryV1> entries, out string detail)
    {
        entries = [];
        detail = string.Empty;
        if (input is null)
        {
            detail = "Yükümlülük listesi zorunludur (boş liste \"izlenen yükümlülük yok\" demektir).";
            return false;
        }
        var read = new List<SettingsLegalDeadlineEntryV1>(input.Count);
        foreach (var item in input)
        {
            if (item is null || !Enum.TryParse<LegalDeadlineObligationV1>(item.Obligation, ignoreCase: false, out var obligation)
                || !Enum.IsDefined(obligation) || !string.Equals(obligation.ToString(), item.Obligation, StringComparison.Ordinal))
            {
                detail = "Yükümlülük türü tanımlı listeden olmalıdır.";
                return false;
            }
            if (item.PeriodMonths is not { } period || item.MonthsAfterPeriodEnd is not { } offset || item.DueDayOfMonth is not { } day)
            {
                detail = "Her yükümlülükte dönem uzunluğu, dönem sonundan sonraki ay ve son gün zorunludur.";
                return false;
            }
            var entry = new SettingsLegalDeadlineEntryV1(obligation, period, offset, day);
            if (!LegalDeadlineCalculatorV1.IsValid(entry))
            {
                detail = "Dönem 1, 3 veya 12 ay; dönem sonundan sonraki ay 0–12; son gün 1–31 olmalıdır.";
                return false;
            }
            if (read.Any(existing => existing.Obligation == obligation))
            {
                detail = "Aynı yükümlülük birden fazla girilemez.";
                return false;
            }
            read.Add(entry);
        }
        entries = read.OrderBy(entry => entry.Obligation).ToArray();
        return true;
    }
}
