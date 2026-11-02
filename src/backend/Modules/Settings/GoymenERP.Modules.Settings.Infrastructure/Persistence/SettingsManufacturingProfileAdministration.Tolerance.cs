using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// API-02 (URT-D6, URT-D7): the write path of the Manufacturing tolerance registry. A version is prepared and approved in
/// <c>settings.ManufacturingToleranceProfileVersions</c>; publishing inserts the immutable
/// <c>settings.ManufacturingTolerancePolicies</c> row (same profile id and version, the approver as its approval, active) that the
/// tolerance provider and the close path read, in the same transaction. The registry trigger keeps the windows of one process
/// from overlapping (a lost race answers <see cref="ManufacturingProfileProblemCodesV1.WindowOverlap"/>).
/// </summary>
internal sealed partial class SettingsManufacturingProfileAdministration
{
    private const string ToleranceEntityType = "Settings.ManufacturingToleranceProfile";
    private const string TolerancePrepareCommand = "settings.manufacturing-tolerance-profile.prepare.v1";
    private const string ToleranceApproveCommand = "settings.manufacturing-tolerance-profile.approve.v1";
    private const string TolerancePublishCommand = "settings.manufacturing-tolerance-profile.publish.v1";
    private const string TolerancePreparedEvent = "settings.manufacturing-tolerance-profile.prepared.v1";
    private const string ToleranceApprovedEvent = "settings.manufacturing-tolerance-profile.approved.v1";
    private const string TolerancePublishedEvent = "settings.manufacturing-tolerance-profile.published.v1";
    private const string RegistryApprovalStatus = "Approved";

    async Task<ManufacturingToleranceProfileListViewV1> ISettingsManufacturingToleranceProfileAdministrationV1.ListAsync(
        CancellationToken cancellationToken)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(ManufacturingToleranceProfileReadActionV1.Read, companyId, cancellationToken);
        var rows = await ReadAsync(() =>
            database.ManufacturingToleranceProfileVersions.AsNoTracking().ToListAsync(cancellationToken));
        var windows = await ReadAsync(() => RegistryWindowsAsync(cancellationToken));
        await RequireStillAsync(ManufacturingToleranceProfileReadActionV1.Read, companyId, cancellationToken);
        return new ManufacturingToleranceProfileListViewV1(
            companyId,
            SettingsBusinessDate.Today(timeProvider),
            rows.OrderBy(row => row.Process, StringComparer.Ordinal).ThenByDescending(row => row.ProfileVersion)
                .Select(row => ToleranceView(row, windows)).ToArray(),
            ApprovedManufacturingProfileCatalog.ToleranceFor(companyId).Select(StartingValueView).ToArray());
    }

    async Task<ManufacturingToleranceProfileViewV1?> ISettingsManufacturingToleranceProfileAdministrationV1.GetAsync(
        Guid profileId, CancellationToken cancellationToken)
    {
        var companyId = RequireContextCompany();
        await RequireAsync(ManufacturingToleranceProfileReadActionV1.Read, companyId, cancellationToken);
        var row = await ReadAsync(() => database.ManufacturingToleranceProfileVersions.AsNoTracking()
            .SingleOrDefaultAsync(version => version.ProfileId == profileId, cancellationToken));
        var windows = await ReadAsync(() => RegistryWindowsAsync(cancellationToken));
        await RequireStillAsync(ManufacturingToleranceProfileReadActionV1.Read, companyId, cancellationToken);
        if (row is not null)
        {
            return ToleranceView(row, windows);
        }

        return ApprovedManufacturingProfileCatalog.ToleranceFor(companyId)
            .Where(entry => entry.ProfileId == profileId)
            .Select(StartingValueView)
            .SingleOrDefault();
    }

    async Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingToleranceProfileAdministrationV1.PrepareAsync(
        PrepareManufacturingToleranceProfileRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        const string permission = ManufacturingToleranceProfileActionsV1.Prepare;
        var companyId = RequireContextCompany();
        var scope = await RequireAsync(permission, companyId, cancellationToken);
        if (!TryParseVersion(request.ExpectedVersion, 0, out var expectedVersion))
        {
            return Unstored(Answers.Invalid("Beklenen sürüm (\"0\" veya pozitif tam sayı metni) zorunludur."), metadata);
        }

        if (request.Process is not { } process || !SettingsManufacturingProcessesV1.IsCanonical(process)
            || request.WorkCenterId == Guid.Empty
            || !ManufacturingProfileValueRulesV1.TryReadPercent(request.MaximumFirePercent, out var maximumFire)
            || !ManufacturingProfileValueRulesV1.TryReadPercent(request.MassBalanceDifferenceTolerancePercent, out var massBalance))
        {
            return Unstored(Answers.Invalid(
                "Proses ve iki yüzde (azami fire ile kütle dengesi farkı: 0-100, en çok 6 ondalık, nokta ayırıcı) zorunludur."),
                metadata);
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
        // A process-wide request keeps the U1-a payload shape (its stored hash stays valid); a line scope adds its id.
        var workCenterId = request.WorkCenterId;
        var requestHash = workCenterId is null
            ? IdempotencyHash.ComputePayload(new
            {
                ExpectedVersion = expectedVersion,
                Process = process,
                MaximumFirePercent = ManufacturingProfileValueRulesV1.Format(maximumFire),
                MassBalanceDifferenceTolerancePercent = ManufacturingProfileValueRulesV1.Format(massBalance),
                Reason = reason,
                request.EffectiveFrom,
                request.EffectiveThrough,
            })
            : IdempotencyHash.ComputePayload(new
            {
                ExpectedVersion = expectedVersion,
                Process = process,
                MaximumFirePercent = ManufacturingProfileValueRulesV1.Format(maximumFire),
                MassBalanceDifferenceTolerancePercent = ManufacturingProfileValueRulesV1.Format(massBalance),
                Reason = reason,
                request.EffectiveFrom,
                request.EffectiveThrough,
                WorkCenterId = workCenterId,
            });
        if (await FindExecutionAsync(TolerancePrepareCommand, keyHash, cancellationToken) is { } existing)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("tolerance-prepare", metadata, process, Replay(existing, requestHash, metadata));
        }

        var latest = await ReadAsync(() => LatestToleranceVersionAsync(process, cancellationToken));
        var refusal = latest != expectedVersion ? Answers.VersionConflict()
            : effectiveFrom < today ? Answers.EffectiveFromPast()
            : null;
        if (refusal is not null)
        {
            await RequireStillAsync(permission, companyId, cancellationToken);
            return Done("tolerance-prepare", metadata, process, await StoreRefusalAsync(TolerancePrepareCommand, refusal,
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
            return Done("tolerance-prepare", metadata, process, Unstored(LifecycleRefusal(exception), metadata));
        }

        var row = new ManufacturingToleranceProfileVersionRow
        {
            CompanyId = companyId,
            ProfileId = draft.ProfileId,
            Process = process,
            WorkCenterId = workCenterId,
            ProfileVersion = draft.ProfileVersion,
            MaximumFirePercent = maximumFire,
            MassBalanceDifferenceTolerancePercent = massBalance,
            EffectiveFrom = effectiveFrom,
            EffectiveThrough = request.EffectiveThrough,
        };
        ApplyLifecycle(row, draft);
        database.ManufacturingToleranceProfileVersions.Add(row);
        Audit(companyId, permission, TolerancePreparedEvent, ToleranceEntityType, draft.ProfileId,
            "PrepareManufacturingToleranceProfile", null, draft.StateVersion, scope, null, new
            {
                CompanyId = companyId,
                draft.ProfileId,
                Process = process,
                WorkCenterId = workCenterId,
                draft.ProfileVersion,
                Status = ManufacturingProfileStatusesV1.Draft,
                MaximumFirePercent = ManufacturingProfileValueRulesV1.Format(maximumFire),
                MassBalanceDifferenceTolerancePercent = ManufacturingProfileValueRulesV1.Format(massBalance),
                EffectiveFrom = effectiveFrom,
                request.EffectiveThrough,
            }, reason, metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        return Done("tolerance-prepare", metadata, process, await SaveChangeAsync(TolerancePrepareCommand,
            Answers.Ok(Receipt(draft.ProfileId, draft.ProfileVersion, draft.StateVersion, ManufacturingProfileStatusesV1.Draft)),
            keyHash, requestHash, metadata, _ => Task.FromResult(Answers.VersionConflict()), cancellationToken));
    }

    Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingToleranceProfileAdministrationV1.ApproveAsync(
        Guid profileId, ManufacturingProfileLifecycleRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken) =>
        TransitionToleranceAsync(profileId, request, metadata, publish: false, cancellationToken);

    Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingToleranceProfileAdministrationV1.PublishAsync(
        Guid profileId, ManufacturingProfileLifecycleRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken) =>
        TransitionToleranceAsync(profileId, request, metadata, publish: true, cancellationToken);

    private async Task<ManufacturingProfileCommandResultV1> TransitionToleranceAsync(Guid profileId,
        ManufacturingProfileLifecycleRequestV1 request, SettingsCommandMetadataV1 metadata, bool publish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var permission = publish ? ManufacturingToleranceProfileActionsV1.Publish : ManufacturingToleranceProfileActionsV1.Approve;
        var commandName = publish ? TolerancePublishCommand : ToleranceApproveCommand;
        var command = publish ? "tolerance-publish" : "tolerance-approve";
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

        var row = await ReadAsync(() => database.ManufacturingToleranceProfileVersions
            .SingleOrDefaultAsync(version => version.ProfileId == profileId, cancellationToken));
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
                var current = ToleranceLifecycle(row);
                var context = LifecycleContext(metadata, expectedVersion, reason);
                next = publish
                    ? ManufacturingToleranceProfileLifecyclePolicyV1.Publish(current, context)
                    : ManufacturingToleranceProfileLifecyclePolicyV1.Approve(current, context);
                refusal = !publish ? null
                    : row.EffectiveFrom < today ? Answers.EffectiveFromPast()
                    : await ReadAsync(() => OverlapsPublishedAsync(row, cancellationToken)) ? Answers.WindowOverlap()
                    : null;
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
        ManufacturingTolerancePolicyRow? superseded = null;
        if (publish)
        {
            // U1-b: an open-ended published predecessor of the same scope that starts earlier is superseded — its window
            // closes on the day before this version starts, in this same save (the registry trigger's one permitted change).
            superseded = await ReadAsync(() => SupersedablePredecessorAsync(row, cancellationToken));
            if (superseded is not null)
            {
                superseded.EffectiveThrough = row.EffectiveFrom.AddDays(-1);
            }

            database.ManufacturingTolerancePolicies.Add(new ManufacturingTolerancePolicyRow
            {
                CompanyId = companyId,
                ProfileId = row.ProfileId,
                Process = row.Process,
                WorkCenterId = row.WorkCenterId,
                Version = row.ProfileVersion,
                MaximumFirePercent = row.MaximumFirePercent,
                MassBalanceDifferenceTolerancePercent = row.MassBalanceDifferenceTolerancePercent,
                IsActive = true,
                EffectiveFrom = row.EffectiveFrom,
                EffectiveThrough = row.EffectiveThrough,
                ApprovalStatus = RegistryApprovalStatus,
                ApprovedBy = row.ApprovedBy!,
                ApprovedAtUtc = row.ApprovedAtUtc!.Value,
                ApprovalReason = row.ApprovalReason!,
            });
        }

        Audit(companyId, permission, publish ? TolerancePublishedEvent : ToleranceApprovedEvent, ToleranceEntityType, profileId,
            publish ? "PublishManufacturingToleranceProfile" : "ApproveManufacturingToleranceProfile", stateBefore,
            row.StateVersion, scope, new { CompanyId = companyId, ProfileId = profileId, Status = statusBefore }, new
            {
                CompanyId = companyId,
                ProfileId = profileId,
                row.Process,
                row.WorkCenterId,
                row.ProfileVersion,
                row.Status,
                MaximumFirePercent = ManufacturingProfileValueRulesV1.Format(row.MaximumFirePercent),
                MassBalanceDifferenceTolerancePercent = ManufacturingProfileValueRulesV1.Format(row.MassBalanceDifferenceTolerancePercent),
                row.EffectiveFrom,
                row.EffectiveThrough,
                Superseded = superseded is null ? null : new
                {
                    superseded.ProfileId,
                    superseded.Version,
                    superseded.EffectiveFrom,
                    superseded.EffectiveThrough,
                },
            }, reason, metadata);
        await RequireStillAsync(permission, companyId, cancellationToken);
        var published = row;
        return Done(command, metadata, subject, await SaveChangeAsync(commandName,
            Answers.Ok(Receipt(row.ProfileId, row.ProfileVersion, row.StateVersion, row.Status)), keyHash, requestHash, metadata,
            async token => publish && await OverlapsPublishedAsync(published, token)
                ? Answers.WindowOverlap()
                : Answers.VersionConflict(),
            cancellationToken));
    }

    /// <summary>The highest version of the process in the lifecycle and in the registry (rows published before this path).</summary>
    private async Task<long> LatestToleranceVersionAsync(string process, CancellationToken cancellationToken)
    {
        var lifecycle = await database.ManufacturingToleranceProfileVersions.AsNoTracking()
            .Where(row => row.Process == process)
            .MaxAsync(row => (long?)row.ProfileVersion, cancellationToken) ?? 0;
        var registry = await database.ManufacturingTolerancePolicies.AsNoTracking()
            .Where(row => row.Process == process)
            .MaxAsync(row => (long?)row.Version, cancellationToken) ?? 0;
        return Math.Max(lifecycle, registry);
    }

    /// <summary>
    /// Whether a published registry row of the same process and line scope (another profile) shares a day with this version's
    /// window — except an open-ended predecessor that starts earlier, which publication supersedes (U1-b).
    /// </summary>
    private Task<bool> OverlapsPublishedAsync(ManufacturingToleranceProfileVersionRow version, CancellationToken cancellationToken)
    {
        var from = version.EffectiveFrom;
        var through = version.EffectiveThrough ?? DateOnly.MaxValue;
        var workCenterId = version.WorkCenterId;
        return database.ManufacturingTolerancePolicies.AsNoTracking()
            .AnyAsync(row => row.Process == version.Process && row.WorkCenterId == workCenterId
                             && row.ProfileId != version.ProfileId
                             && row.EffectiveFrom <= through
                             && (row.EffectiveThrough == null || row.EffectiveThrough >= from)
                             && !(row.EffectiveThrough == null && row.EffectiveFrom < from), cancellationToken);
    }

    /// <summary>The open-ended published predecessor of the same scope that starts before this version (tracked), if any.</summary>
    private Task<ManufacturingTolerancePolicyRow?> SupersedablePredecessorAsync(
        ManufacturingToleranceProfileVersionRow version, CancellationToken cancellationToken)
    {
        var from = version.EffectiveFrom;
        var workCenterId = version.WorkCenterId;
        return database.ManufacturingTolerancePolicies
            .Where(row => row.Process == version.Process && row.WorkCenterId == workCenterId
                          && row.ProfileId != version.ProfileId
                          && row.EffectiveThrough == null && row.EffectiveFrom < from)
            .OrderByDescending(row => row.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The registry window of each published version (a successor may have closed an open-ended one).</summary>
    private async Task<IReadOnlyDictionary<Guid, DateOnly?>> RegistryWindowsAsync(CancellationToken cancellationToken) =>
        (await database.ManufacturingTolerancePolicies.AsNoTracking()
            .Select(row => new { row.ProfileId, row.EffectiveThrough })
            .ToListAsync(cancellationToken))
        .ToDictionary(row => row.ProfileId, row => row.EffectiveThrough);

    private static ManufacturingToleranceProfileLifecycleV1 ToleranceLifecycle(ManufacturingToleranceProfileVersionRow row) => new(
        row.CompanyId, row.ProfileId, row.Process, row.ProfileVersion, row.StateVersion,
        Enum.Parse<ManufacturingToleranceProfileStatusV1>(row.Status), row.PreparedBy, row.PreparedAtUtc, row.PreparationReason,
        row.ApprovedBy, row.ApprovedAtUtc, row.ApprovalReason, row.PublishedBy, row.PublishedAtUtc, row.PublicationReason);

    private static void ApplyLifecycle(ManufacturingToleranceProfileVersionRow row, ManufacturingToleranceProfileLifecycleV1 state)
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

    private static ManufacturingToleranceProfileViewV1 ToleranceView(ManufacturingToleranceProfileVersionRow row,
        IReadOnlyDictionary<Guid, DateOnly?> registryWindows) => new(
        row.ProfileId,
        ManufacturingProfileSourcesV1.Settings,
        row.Process,
        Text(row.ProfileVersion),
        Text(row.StateVersion),
        row.Status,
        ManufacturingProfileValueRulesV1.Format(row.MaximumFirePercent),
        ManufacturingProfileValueRulesV1.Format(row.MassBalanceDifferenceTolerancePercent),
        row.EffectiveFrom,
        row.Status == ManufacturingProfileStatusesV1.Published && registryWindows.TryGetValue(row.ProfileId, out var through)
            ? through
            : row.EffectiveThrough,
        row.PreparedBy,
        row.PreparedAtUtc,
        row.PreparationReason,
        row.ApprovedBy,
        row.ApprovedAtUtc,
        row.ApprovalReason,
        row.PublishedBy,
        row.PublishedAtUtc,
        row.PublicationReason,
        row.WorkCenterId);

    private static ManufacturingToleranceProfileViewV1 StartingValueView(ApprovedManufacturingToleranceStartingValue entry) => new(
        entry.ProfileId,
        ManufacturingProfileSourcesV1.Catalog,
        entry.Process,
        Text(ApprovedManufacturingProfileCatalog.CatalogProfileVersion),
        Text(ApprovedManufacturingProfileCatalog.CatalogProfileVersion),
        ManufacturingProfileStatusesV1.StartingValue,
        ManufacturingProfileValueRulesV1.Format(entry.MaximumFirePercent),
        ManufacturingProfileValueRulesV1.Format(entry.MassBalanceDifferenceTolerancePercent),
        entry.EffectiveFrom,
        null,
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
