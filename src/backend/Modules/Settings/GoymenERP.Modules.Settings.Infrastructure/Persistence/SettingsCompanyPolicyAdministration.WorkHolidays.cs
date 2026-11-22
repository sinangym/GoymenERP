using System.Globalization;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// YOL-D22 (W1-A): the company's yearly official holiday / eve list, on the approval duration authority
/// (<see cref="SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage"/>). The same K3 path as the registry keys:
/// company-wide permission measured first and fresh before every save, mandatory reason, one Idempotency-Key per command
/// (stored terminal answer, another body is 409), expected version per year, one audit event per real change, insert-only.
/// </summary>
internal sealed partial class SettingsCompanyPolicyAdministration
{
    /// <summary>The spec key names the command family; holiday rows live in their own tables, not the policy registry.</summary>
    private const string WorkHolidayCalendarSpecKey = "work.holiday-calendar";

    private static readonly KeySpec WorkHolidayCalendar = new(WorkHolidayCalendarSpecKey,
        SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage, "settings.work-holiday-calendar.record.v1",
        "settings.work-holiday-calendar.recorded.v1", "Settings.WorkHolidayCalendar", "Resmî tatil takvimi sürümü kaydedildi");

    public async Task<WorkHolidayCalendarViewV1> GetWorkHolidayCalendarAsync(int year, CancellationToken cancellationToken = default)
    {
        if (!ValidYear(year)) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);
        var companyId = RequireContextCompany();
        var actor = currentUser.UserId;
        await RequireAsync(WorkHolidayCalendar.Permission, companyId, cancellationToken);
        var versions = await ReadAsync(() => database.WorkHolidayCalendarVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Year == year)
            .OrderByDescending(row => row.Version).ToArrayAsync(cancellationToken));
        var latest = versions.FirstOrDefault();
        var days = latest is null
            ? []
            : await ReadAsync(() => database.WorkHolidayCalendarDays.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Year == year && row.Version == latest.Version)
                .OrderBy(row => row.Date).ToArrayAsync(cancellationToken));
        await RequireStillAsync(WorkHolidayCalendar.Permission, companyId, cancellationToken);
        if (RequireContextCompany() != companyId || currentUser.UserId != actor) throw new UnauthorizedAccessException();
        if (latest is not null && (days.Length != latest.DayCount || days.Any(day => !Enum.IsDefined((WorkHolidayKindV1)day.Kind))))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, year, latest is not null, Text(latest?.Version ?? 0),
            days.Select(day => new WorkHolidayCalendarDayViewV1(day.Date, ((WorkHolidayKindV1)day.Kind).ToString())).ToArray(),
            versions.Select(row => new WorkHolidayCalendarVersionViewV1(Text(row.Version), row.DayCount, row.RecordedBy,
                row.RecordedAtUtc, row.Reason)).ToArray());
    }

    public async Task<CompanyPolicyCommandResultV1> RecordWorkHolidayCalendarAsync(int year, RecordWorkHolidayCalendarRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (auditWriter is null) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        ValidateMetadata(metadata);
        var spec = WorkHolidayCalendar;
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(spec.Permission, companyId, cancellationToken);
        if (!ValidYear(year))
            return Unstored(Answers.Invalid(string.Create(CultureInfo.InvariantCulture,
                $"Yıl {SettingsWorkHolidayCalendarLimitsV1.MinimumYear} ile {SettingsWorkHolidayCalendarLimitsV1.MaximumYear} arasında olmalıdır.")), metadata);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        if (!TryReadDays(year, request.Days, out var days, out var detail))
            return Unstored(Answers.Invalid(detail), metadata);
        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
            return Unstored(reasonRefusal, metadata);

        var subject = string.Create(CultureInfo.InvariantCulture, $"{companyId:D}:{spec.Key}:{year}");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var payload = days.Select(day => new { Date = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind = day.Kind.ToString() })
            .ToArray();
        var requestHash = IdempotencyHash.ComputePayload(new { spec.Key, Year = year, ExpectedVersion = expectedVersion, Days = payload, Reason = reason });
        if (await FindExecutionAsync(spec, keyHash, cancellationToken) is { } existing)
        {
            await RequireReplayStillAsync(spec, metadata, cancellationToken);
            return Done(spec, metadata, subject, Replay(existing, requestHash, metadata));
        }

        var latest = await ReadAsync(() => database.WorkHolidayCalendarVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Year == year)
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
            var current = await ReadAsync(() => database.WorkHolidayCalendarDays.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Year == year && row.Version == latest.Version)
                .OrderBy(row => row.Date).Select(row => new { row.Date, row.Kind }).ToArrayAsync(cancellationToken));
            if (current.Length == days.Count
                && current.Zip(days).All(pair => pair.First.Date == pair.Second.Date && pair.First.Kind == (int)pair.Second.Kind))
            {
                await RequireReplayStillAsync(spec, metadata, cancellationToken);
                return Done(spec, metadata, subject,
                    await StoreRefusalAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata, cancellationToken));
            }
        }

        var version = checked(latestVersion + 1);
        database.WorkHolidayCalendarVersions.Add(new WorkHolidayCalendarVersionRow
        {
            CompanyId = companyId,
            Year = year,
            Version = version,
            DayCount = days.Count,
            RecordedAtUtc = timeProvider.GetUtcNow(),
            RecordedBy = metadata.ActorId,
            Reason = reason,
        });
        foreach (var day in days)
            database.WorkHolidayCalendarDays.Add(new WorkHolidayCalendarDayRow
            {
                CompanyId = companyId,
                Year = year,
                Version = version,
                Date = day.Date,
                Kind = (int)day.Kind,
            });
        Audit(spec, companyId, subject, latestVersion == 0 ? null : latestVersion, version, scope,
            new { CompanyId = companyId, Year = year, Version = version, Days = payload }, reason, metadata);
        await RequireReplayStillAsync(spec, metadata, cancellationToken);
        return Done(spec, metadata, subject, await SaveChangeAsync(spec, Answers.NoContent(), keyHash, requestHash, metadata,
            Answers.VersionConflict(), cancellationToken));
    }

    private static bool ValidYear(int year) =>
        year is >= SettingsWorkHolidayCalendarLimitsV1.MinimumYear and <= SettingsWorkHolidayCalendarLimitsV1.MaximumYear;

    private static bool TryReadDays(int year, IReadOnlyList<RecordWorkHolidayDayV1>? input,
        out IReadOnlyList<(DateOnly Date, WorkHolidayKindV1 Kind)> days, out string detail)
    {
        days = [];
        detail = string.Empty;
        if (input is null)
        {
            detail = "Tatil günleri listesi zorunludur (boş liste \"tatil yok\" demektir).";
            return false;
        }
        if (input.Count > SettingsWorkHolidayCalendarLimitsV1.MaximumDays)
        {
            detail = string.Create(CultureInfo.InvariantCulture, $"Bir yılda en fazla {SettingsWorkHolidayCalendarLimitsV1.MaximumDays} gün girilebilir.");
            return false;
        }
        var read = new List<(DateOnly Date, WorkHolidayKindV1 Kind)>(input.Count);
        foreach (var day in input)
        {
            if (day?.Date is not { } date || date.Year != year)
            {
                detail = "Her gün seçilen yıl içinde bir tarih olmalıdır.";
                return false;
            }
            if (!Enum.TryParse<WorkHolidayKindV1>(day.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind)
                || !string.Equals(kind.ToString(), day.Kind, StringComparison.Ordinal))
            {
                detail = "Gün türü \"Holiday\" (resmî tatil) veya \"Eve\" (arife) olmalıdır.";
                return false;
            }
            if (read.Any(existing => existing.Date == date))
            {
                detail = "Aynı tarih birden fazla girilemez.";
                return false;
            }
            read.Add((date, kind));
        }
        days = read.OrderBy(day => day.Date).ToArray();
        return true;
    }
}
