using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// API-01 (URT-D9, URT-D10): the Manufacturing parameter profiles — one header per version of a company × process × optional
/// work center scope and one typed row per value. The reviewed version-0 catalog (<see cref="ApprovedManufacturingProfileCatalog"/>)
/// is listed with them and is the base the read port serves until a Settings version replaces it.
/// </summary>
internal sealed partial class SettingsManufacturingProfileAdministration
{
    private const string ParameterEntityType = "Settings.ManufacturingParameterProfile";
    private const string ParameterPrepareCommand = "settings.manufacturing-parameter-profile.prepare.v1";
    private const string ParameterApproveCommand = "settings.manufacturing-parameter-profile.approve.v1";
    private const string ParameterPublishCommand = "settings.manufacturing-parameter-profile.publish.v1";
    private const string ParameterPreparedEvent = "settings.manufacturing-parameter-profile.prepared.v1";
    private const string ParameterApprovedEvent = "settings.manufacturing-parameter-profile.approved.v1";
    private const string ParameterPublishedEvent = "settings.manufacturing-parameter-profile.published.v1";

    async Task<ManufacturingParameterProfileListViewV1> ISettingsManufacturingParameterProfileAdministrationV1.ListAsync(
        CancellationToken cancellationToken)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(ManufacturingParameterProfileActionsV1.Read, companyId, cancellationToken);
        var (headers, values) = await ReadAsync(async () => (
            await database.ManufacturingParameterProfiles.AsNoTracking().ToListAsync(cancellationToken),
            await database.ManufacturingParameterProfileValues.AsNoTracking().ToListAsync(cancellationToken)));
        await RequireStillAsync(ManufacturingParameterProfileActionsV1.Read, companyId, cancellationToken);
        var byProfile = values.ToLookup(row => row.ProfileId);
        var profiles = ApprovedManufacturingProfileCatalog.ParametersFor(companyId).Select(CatalogView)
            .Concat(headers.Select(row => ParameterView(row, byProfile[row.ProfileId])))
            .OrderBy(view => view.Process, StringComparer.Ordinal)
            .ThenBy(view => view.WorkCenterId)
            .ThenByDescending(view => long.Parse(view.ProfileVersion, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var keys = ManufacturingParameterKeysV1.All
            .Select(definition => new ManufacturingParameterDefinitionViewV1(definition.Key, definition.Unit,
                definition.Kind.ToString(), definition.MaximumScale, definition.Processes, definition.Choices))
            .ToArray();
        return new ManufacturingParameterProfileListViewV1(companyId, SettingsBusinessDate.Today(timeProvider), keys, profiles);
    }

    async Task<ManufacturingParameterProfileViewV1?> ISettingsManufacturingParameterProfileAdministrationV1.GetAsync(
        Guid profileId, CancellationToken cancellationToken)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(ManufacturingParameterProfileActionsV1.Read, companyId, cancellationToken);
        if (ApprovedManufacturingProfileCatalog.ParametersFor(companyId).SingleOrDefault(entry => entry.ProfileId == profileId)
            is { } catalog)
        {
            await RequireStillAsync(ManufacturingParameterProfileActionsV1.Read, companyId, cancellationToken);
            return CatalogView(catalog);
        }

        var (header, values) = await ReadAsync(async () => (
            await database.ManufacturingParameterProfiles.AsNoTracking()
                .SingleOrDefaultAsync(row => row.ProfileId == profileId, cancellationToken),
            await database.ManufacturingParameterProfileValues.AsNoTracking()
                .Where(row => row.ProfileId == profileId).ToListAsync(cancellationToken)));
        await RequireStillAsync(ManufacturingParameterProfileActionsV1.Read, companyId, cancellationToken);
        return header is null ? null : ParameterView(header, values);
    }

    async Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingParameterProfileAdministrationV1.PrepareAsync(
        PrepareManufacturingParameterProfileRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        const string permission = ManufacturingParameterProfileActionsV1.Prepare;
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(permission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (request.Process is not { } process || !SettingsManufacturingProcessesV1.IsCanonical(process)
            || request.WorkCenterId == Guid.Empty)
        {
            return Unstored(Answers.Invalid(
                "Proses (Slitting, ColdRolling, TubeProfile, Paint, CutToLength) zorunludur; iş merkezi boş kimlik olamaz."),
                metadata);
        }

        if (!ManufacturingProfileValueRulesV1.TryReadValues(process, request.Values, out var values, out var detail))
        {
            return Unstored(Answers.Invalid(detail), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var today = SettingsBusinessDate.Today(timeProvider);
        var effectiveFrom = request.EffectiveFrom ?? today;
        if (request.EffectiveThrough is { } through && through < effectiveFrom)
        {
            return Unstored(Answers.Invalid("Bitiş tarihi başlangıç tarihinden önce olamaz."), metadata);
        }

        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new
        {
            ExpectedVersion = expectedVersion,
            Process = process,
            request.WorkCenterId,
            Values = values.Select(value => new { value.Key, Value = ManufacturingProfileValueRulesV1.Text(value) }).ToArray(),
            Reason = reason,
            request.EffectiveFrom,
            request.EffectiveThrough,
        });
        var subject = $"{process}:{request.WorkCenterId?.ToString() ?? "process"}";
        if (await FindExecutionAsync(ParameterPrepareCommand, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("parameter-prepare", metadata, subject, Replay(existing, requestHash, metadata));
        }

        var workCenterId = request.WorkCenterId;
        var latest = await ReadAsync(() => LatestParameterVersionAsync(process, workCenterId, cancellationToken));
        var refusal = latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("parameter-prepare", metadata, subject, await StoreRefusalAsync(ParameterPrepareCommand, refusal,
                keyHash, requestHash, metadata, cancellationToken));
        }

        ManufacturingToleranceProfileLifecycleV1 draft;
        try
        {
            draft = ManufacturingToleranceProfileLifecyclePolicyV1.Prepare(new PrepareManufacturingToleranceProfileV1(
                Guid.NewGuid(), process, checked(latest + 1), LifecycleContext(metadata, 0, reason)));
        }
        catch (ManufacturingToleranceProfileLifecycleException exception)
        {
            return Done("parameter-prepare", metadata, subject, Unstored(LifecycleRefusal(exception), metadata));
        }

        var header = new ManufacturingParameterProfileRow
        {
            CompanyId = companyId,
            ProfileId = draft.ProfileId,
            Process = process,
            WorkCenterId = workCenterId,
            ProfileVersion = draft.ProfileVersion,
            EffectiveFrom = effectiveFrom,
            EffectiveThrough = request.EffectiveThrough,
        };
        ApplyLifecycle(header, draft);
        database.ManufacturingParameterProfiles.Add(header);
        foreach (var value in values)
        {
            database.ManufacturingParameterProfileValues.Add(new ManufacturingParameterProfileValueRow
            {
                CompanyId = companyId,
                ProfileId = draft.ProfileId,
                ParameterKey = value.Key,
                Unit = value.Unit,
                NumericValue = value.Number,
                ChoiceValue = value.Choice,
            });
        }

        Audit(companyId, permission, ParameterPreparedEvent, ParameterEntityType, draft.ProfileId,
            "PrepareManufacturingParameterProfile", null, draft.StateVersion, scope, null, new
            {
                CompanyId = companyId,
                draft.ProfileId,
                Process = process,
                WorkCenterId = workCenterId,
                draft.ProfileVersion,
                Status = ManufacturingProfileStatusesV1.Draft,
                EffectiveFrom = effectiveFrom,
                request.EffectiveThrough,
                Values = values.Select(value => new { value.Key, value.Unit, Value = ManufacturingProfileValueRulesV1.Text(value) })
                    .ToArray(),
            }, reason, metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        return Done("parameter-prepare", metadata, subject, await SaveChangeAsync(ParameterPrepareCommand,
            Answers.Ok(Receipt(draft.ProfileId, draft.ProfileVersion, draft.StateVersion, ManufacturingProfileStatusesV1.Draft)),
            keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingParameterProfileAdministrationV1.ApproveAsync(
        Guid profileId, ManufacturingProfileLifecycleRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken) =>
        TransitionParameterAsync(profileId, request, metadata, publish: false, cancellationToken);

    Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingParameterProfileAdministrationV1.PublishAsync(
        Guid profileId, ManufacturingProfileLifecycleRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken) =>
        TransitionParameterAsync(profileId, request, metadata, publish: true, cancellationToken);

    private async Task<ManufacturingProfileCommandResultV1> TransitionParameterAsync(Guid profileId,
        ManufacturingProfileLifecycleRequestV1 request, SettingsCommandMetadataV1 metadata, bool publish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var permission = publish ? ManufacturingParameterProfileActionsV1.Publish : ManufacturingParameterProfileActionsV1.Approve;
        var commandName = publish ? ParameterPublishCommand : ParameterApproveCommand;
        var command = publish ? "parameter-publish" : "parameter-approve";
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(permission, companyId, cancellationToken);
        if (profileId == Guid.Empty || !TryParseVersion(request.ExpectedVersion, 1, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Profil ve beklenen sürüm (pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (ReasonRefusal(request.Reason, out var reason) is { } reasonRefusal)
        {
            return Unstored(reasonRefusal, metadata);
        }

        var subject = profileId.ToString("D");
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { ProfileId = profileId, ExpectedVersion = expectedVersion, Reason = reason });
        if (await FindExecutionAsync(commandName, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done(command, metadata, subject, Replay(existing, requestHash, metadata));
        }

        var row = await ReadAsync(() => database.ManufacturingParameterProfiles
            .SingleOrDefaultAsync(profile => profile.ProfileId == profileId, cancellationToken));
        var today = SettingsBusinessDate.Today(timeProvider);
        ManufacturingProfileCommandAnswerV1? refusal = null;
        ManufacturingToleranceProfileLifecycleV1? next = null;
        if (row is null)
        {
            refusal = Answers.NotFound();
        }
        else
        {
            try
            {
                var current = Lifecycle(row);
                var context = LifecycleContext(metadata, expectedVersion, reason);
                next = publish
                    ? ManufacturingToleranceProfileLifecyclePolicyV1.Publish(current, context)
                    : ManufacturingToleranceProfileLifecyclePolicyV1.Approve(current, context);
                refusal = publish && row.EffectiveFrom < today ? Answers.EffectiveFromPast() : null;
            }
            catch (ManufacturingToleranceProfileLifecycleException exception)
            {
                refusal = LifecycleRefusal(exception);
            }
        }

        if (refusal is not null)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done(command, metadata, subject, await StoreRefusalAsync(commandName, refusal, keyHash, requestHash, metadata,
                cancellationToken));
        }

        var stateBefore = row!.StateVersion;
        var statusBefore = row.Status;
        ApplyLifecycle(row, next!);
        Audit(companyId, permission, publish ? ParameterPublishedEvent : ParameterApprovedEvent, ParameterEntityType, profileId,
            publish ? "PublishManufacturingParameterProfile" : "ApproveManufacturingParameterProfile", stateBefore,
            row.StateVersion, scope, new { CompanyId = companyId, ProfileId = profileId, Status = statusBefore }, new
            {
                CompanyId = companyId,
                ProfileId = profileId,
                row.Process,
                row.WorkCenterId,
                row.ProfileVersion,
                row.Status,
                row.EffectiveFrom,
                row.EffectiveThrough,
            }, reason, metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        return Done(command, metadata, subject, await SaveChangeAsync(commandName,
            Answers.Ok(Receipt(row.ProfileId, row.ProfileVersion, row.StateVersion, row.Status)), keyHash, requestHash, metadata,
            _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    private async Task<long> LatestParameterVersionAsync(string process, Guid? workCenterId, CancellationToken cancellationToken) =>
        await database.ManufacturingParameterProfiles.AsNoTracking()
            .Where(row => row.Process == process && row.WorkCenterId == workCenterId)
            .MaxAsync(row => (long?)row.ProfileVersion, cancellationToken) ?? 0;

    private static ManufacturingToleranceProfileLifecycleV1 Lifecycle(ManufacturingParameterProfileRow row) => new(
        row.CompanyId, row.ProfileId, row.Process, row.ProfileVersion, row.StateVersion,
        Enum.Parse<ManufacturingToleranceProfileStatusV1>(row.Status), row.PreparedBy, row.PreparedAtUtc, row.PreparationReason,
        row.ApprovedBy, row.ApprovedAtUtc, row.ApprovalReason, row.PublishedBy, row.PublishedAtUtc, row.PublicationReason);

    private static void ApplyLifecycle(ManufacturingParameterProfileRow row, ManufacturingToleranceProfileLifecycleV1 state)
    {
        row.StateVersion = state.StateVersion;
        row.Status = state.Status.ToString();
        row.PreparedBy = state.PreparedBy;
        row.PreparedAtUtc = state.PreparedAtUtc;
        row.PreparationReason = state.PreparationReason;
        row.ApprovedBy = state.ApprovedBy;
        row.ApprovedAtUtc = state.ApprovedAtUtc;
        row.ApprovalReason = state.ApprovalReason;
        row.PublishedBy = state.PublishedBy;
        row.PublishedAtUtc = state.PublishedAtUtc;
        row.PublicationReason = state.PublicationReason;
    }

    private static ManufacturingParameterProfileViewV1 ParameterView(ManufacturingParameterProfileRow row,
        IEnumerable<ManufacturingParameterProfileValueRow> values) => new(
        row.ProfileId,
        ManufacturingProfileSourcesV1.Settings,
        row.Process,
        row.WorkCenterId,
        Text(row.ProfileVersion),
        Text(row.StateVersion),
        row.Status,
        row.EffectiveFrom,
        row.EffectiveThrough,
        ManufacturingParameterProfileReads.Values(values)
            .Select(value => new ManufacturingParameterValueViewV1(value.Key, value.Unit, ManufacturingProfileValueRulesV1.Text(value)))
            .ToArray(),
        row.PreparedBy,
        row.PreparedAtUtc,
        row.PreparationReason,
        row.ApprovedBy,
        row.ApprovedAtUtc,
        row.ApprovalReason,
        row.PublishedBy,
        row.PublishedAtUtc,
        row.PublicationReason);

    private static ManufacturingParameterProfileViewV1 CatalogView(ApprovedManufacturingParameterProfileEntry entry) => new(
        entry.ProfileId,
        ManufacturingProfileSourcesV1.Catalog,
        entry.Process,
        null,
        Text(ApprovedManufacturingProfileCatalog.CatalogProfileVersion),
        Text(ApprovedManufacturingProfileCatalog.CatalogProfileVersion),
        ManufacturingProfileStatusesV1.Published,
        entry.EffectiveFrom,
        null,
        entry.Values
            .Select(value => new ManufacturingParameterValueViewV1(value.Key, value.Unit, ManufacturingProfileValueRulesV1.Text(value)))
            .ToArray(),
        null,
        null,
        ApprovedManufacturingProfileCatalog.Source,
        null,
        null,
        null,
        null,
        null,
        null);
}
