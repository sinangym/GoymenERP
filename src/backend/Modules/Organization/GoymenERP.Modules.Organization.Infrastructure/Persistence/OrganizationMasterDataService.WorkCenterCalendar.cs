using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// B-15 work-centre calendar master. The command follows the master edit's protocol (key looked up first, every
/// terminal answer stored with the key, one audit event per applied command in the same transaction) on its own
/// write permission and the shared work-centre scope predicate. A change writes a new append-only revision; an
/// unchanged save is an audited "NoChange" that writes no revision and keeps the version.
/// </summary>
internal sealed partial class OrganizationMasterDataService
{
    internal const string SetWorkCenterCalendarCommandName = "organization.work-center.calendar.set.v1";
    internal const string WorkCenterCalendarChangedEventType = "organization.work-center.calendar.changed.v1";
    private const string WorkCenterCalendarEntityType = "Organization.WorkCenterCalendar";

    public async Task<WorkCenterCalendarDetail?> GetAsync(Guid workCenterId, CancellationToken cancellationToken)
    {
        var scope = await ReadScopeAsync(ErpPermissions.OrganizationWorkCentersRead, cancellationToken);
        var inScope = await OrganizationScopes
            .ApplyWorkCenterScope(dbContext, dbContext.WorkCenters.AsNoTracking(), scope)
            .AnyAsync(workCenter => workCenter.Id == workCenterId, cancellationToken);
        if (!inScope)
        {
            return null;
        }

        var current = await OrganizationWorkCenterCalendarQueries.ReadCurrentAsync(
            dbContext,
            workCenterId,
            cancellationToken);
        return OrganizationWorkCenterCalendarRules.ToDetail(
            workCenterId,
            current.Source,
            current.Version,
            current.Definition);
    }

    public async Task<CommandExecutionResult> SetAsync(
        Guid workCenterId,
        SetWorkCenterCalendar command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        if (!OrganizationWorkCenterCalendarRules.TryParse(command, out var parsed, out _) || parsed is null)
        {
            throw new ArgumentException("Çalışma takvimi tanımlı kurallara uymalıdır.", nameof(command));
        }

        if (command.ExpectedVersion < OrganizationWorkCenterCalendarRules.DefaultVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Beklenen takvim sürümü negatif olamaz.");
        }

        var definition = parsed;
        var reason = RequireReason(command.Reason);
        const string permission = OrganizationPermissionManifestV1.WorkCenterCalendarsWrite;
        var scope = await ReadScopeAsync(permission, cancellationToken);

        // The fingerprint is the CANONICAL content (sorted shifts and dates), so the same calendar sent in another order
        // under the same key replays instead of answering an idempotency conflict.
        var request = CreateRequest(
            SetWorkCenterCalendarCommandName,
            metadata,
            new SetWorkCenterCalendarFingerprint(
                workCenterId,
                definition.Shifts.Select(OrganizationWorkCenterCalendarRules.ToWire).ToArray(),
                definition.ClosedDates.Select(OrganizationWorkCenterCalendarRules.FormatDate).ToArray(),
                command.ExpectedVersion,
                reason));

        var scopedWorkCenter = OrganizationScopes
            .ApplyWorkCenterScope(dbContext, dbContext.WorkCenters.AsNoTracking(), scope)
            .Where(workCenter => workCenter.Id == workCenterId);
        var inScope = await scopedWorkCenter.AnyAsync(cancellationToken);

        var existing = await FindCommandExecutionAsync(request, cancellationToken);
        if (existing is not null)
        {
            return !scope.IsCompanyWide && !inScope
                ? NotRecordedNotFound(metadata)
                : ResolveExistingExecution(existing, request, metadata);
        }

        if (!inScope)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        var calendar = await dbContext.WorkCenterCalendars
            .SingleOrDefaultAsync(x => x.WorkCenterId == workCenterId, cancellationToken);
        var versionBefore = calendar?.Version ?? OrganizationWorkCenterCalendarRules.DefaultVersion;
        if (versionBefore != command.ExpectedVersion)
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.VersionConflict(command.ExpectedVersion, versionBefore),
                cancellationToken);
        }

        var before = calendar is null
            ? WorkCenterCalendarDefinition.Urtd11Default
            : await OrganizationWorkCenterCalendarQueries.ReadRevisionAsync(
                dbContext,
                calendar.Id,
                calendar.Version,
                cancellationToken);
        var sourceBefore = calendar is null
            ? OrganizationWorkCenterCalendarRules.DefaultSource
            : OrganizationWorkCenterCalendarRules.WorkCenterSource;
        var beforeJson = SerializeWorkCenterCalendar(workCenterId, sourceBefore, versionBefore, before);

        string operation;
        var versionAfter = versionBefore;
        var sourceAfter = sourceBefore;
        if (before.Equals(definition))
        {
            // Saving the content already in force (including the URT-D11 default while none is saved) changes nothing.
            operation = "NoChange";
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            if (calendar is null)
            {
                calendar = WorkCenterCalendar.Create(Guid.NewGuid(), metadata.CompanyId, workCenterId, now);
                dbContext.WorkCenterCalendars.Add(calendar);
                operation = "Create";
            }
            else
            {
                calendar.Revise(now);
                operation = "Update";
            }

            var rows = calendar.RowsFor(definition);
            dbContext.WorkCenterCalendarShifts.AddRange(rows.Shifts);
            dbContext.WorkCenterCalendarClosedDates.AddRange(rows.ClosedDates);
            versionAfter = calendar.Version;
            sourceAfter = OrganizationWorkCenterCalendarRules.WorkCenterSource;
        }

        EnqueueAudit(
            metadata,
            scope,
            permission,
            WorkCenterCalendarChangedEventType,
            WorkCenterCalendarEntityType,
            workCenterId,
            operation,
            operation == "Create" ? null : versionBefore,
            versionAfter,
            beforeJson,
            SerializeWorkCenterCalendar(workCenterId, sourceAfter, versionAfter, definition),
            reason);

        var expectedVersion = command.ExpectedVersion;
        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.For(CommandResult.Succeeded),
            async (exception, token) =>
            {
                if (!await scopedWorkCenter.AnyAsync(token))
                {
                    return CommandResponses.For(CommandResult.NotFound);
                }

                // A lost race is either a concurrent revision (the head's version token) or two first saves meeting on
                // the (CompanyId, WorkCenterId) unique index; both answer the version that won.
                var currentVersion = await dbContext.WorkCenterCalendars
                    .AsNoTracking()
                    .Where(x => x.WorkCenterId == workCenterId)
                    .Select(x => (long?)x.Version)
                    .SingleOrDefaultAsync(token) ?? OrganizationWorkCenterCalendarRules.DefaultVersion;
                return exception is DbUpdateConcurrencyException || currentVersion != expectedVersion
                    ? CommandResponses.VersionConflict(expectedVersion, currentVersion)
                    : null;
            },
            workCenterId,
            cancellationToken);
    }

    private static string SerializeWorkCenterCalendar(
        Guid workCenterId,
        string source,
        long version,
        WorkCenterCalendarDefinition definition) =>
        JsonSerializer.Serialize(new
        {
            WorkCenterId = workCenterId,
            Source = source,
            Version = version,
            TimeZoneId = WorkCenterCalendarDefinition.BusinessTimeZoneId,
            Shifts = definition.Shifts.Select(OrganizationWorkCenterCalendarRules.ToWire).ToArray(),
            ClosedDates = definition.ClosedDates.Select(OrganizationWorkCenterCalendarRules.FormatDate).ToArray(),
        });

    private sealed record SetWorkCenterCalendarFingerprint(
        Guid WorkCenterId,
        IReadOnlyList<WorkCenterCalendarShiftV1> Shifts,
        IReadOnlyList<string> ClosedDates,
        long ExpectedVersion,
        string Reason);
}
