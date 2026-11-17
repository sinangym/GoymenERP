using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.LogisticsRuntime;

public static class InventoryTransferPolicyHostComposition
{
    public static IServiceCollection AddInventoryTransferPolicyHostPorts(this IServiceCollection services)
    {
        services.AddScoped<ISettingsTransferWarehouseSourceV1, OrganizationSettingsTransferWarehouseSource>();
        services.AddScoped<ISettingsTransferDepartureCapabilityV1, LogisticsSettingsTransferDepartureCapability>();
        services.AddScoped<ILogisticsTransferReferenceSourceV1, InventoryLogisticsTransferReferenceSource>();
        services.AddScoped<IInventoryTransferSourceScaleSettingPortV1, SettingsInventoryTransferScaleSource>();
        services.AddScoped<LogisticsInventoryTransferCancelGate>();
        services.AddScoped<IInventoryTransferDispatchCancelGatePortV1>(sp => sp.GetRequiredService<LogisticsInventoryTransferCancelGate>());
        services.AddScoped<IInventoryTransferDispatchCancelGatePortV2>(sp => sp.GetRequiredService<LogisticsInventoryTransferCancelGate>());
        return services;
    }
}

internal sealed class OrganizationSettingsTransferWarehouseSource(IOrganizationTransferDestinationSourceV1 source,
    ICompanyContext company, ICurrentUser user) : ISettingsTransferWarehouseSourceV1
{
    public async Task<SettingsTransferWarehouseV1?> ReadAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var actor = TransferHostFence.Member(company, user, companyId, cancellationToken);
        var value = await source.ReadAsync(companyId, locationId, cancellationToken);
        TransferHostFence.Still(company, user, companyId, actor, cancellationToken);
        return value is null || value.LocationId != locationId || value.LocationVersion < 1 ? null
            : new(companyId, locationId, value.IsActive, value.IsExternal, value.LocationVersion);
    }
}

internal sealed class LogisticsSettingsTransferDepartureCapability(ILogisticsTransferDepartureOwnerV1 departure,
    ISettingsTransferWarehouseSourceV1 warehouses, ICompanyContext company, ICurrentUser user)
    : ISettingsTransferDepartureCapabilityV1
{
    public async Task<bool> IsSupportedAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var actor = TransferHostFence.Member(company, user, companyId, cancellationToken);
        var location = await warehouses.ReadAsync(companyId, locationId, cancellationToken);
        TransferHostFence.Still(company, user, companyId, actor, cancellationToken);
        if (location is not { IsActive: true, IsExternal: false, LocationVersion: > 0 }
            || location.CompanyId != companyId || location.LocationId != locationId) return false;
        var supported = await departure.SupportsWarehouseAsync(companyId, locationId, cancellationToken);
        TransferHostFence.Still(company, user, companyId, actor, cancellationToken);
        return supported;
    }
}

internal sealed class SettingsInventoryTransferScaleSource(ISettingsWarehouseTransferScaleProviderV1 source,
    ICompanyContext company, ICurrentUser user) : IInventoryTransferSourceScaleSettingPortV1
{
    public async Task<InventoryTransferScaleSettingV1> ReadAsync(Guid companyId, Guid sourceLocationId, CancellationToken cancellationToken = default)
    {
        var actor = TransferHostFence.Member(company, user, companyId, cancellationToken);
        SettingsWarehouseTransferScalePolicyV1 answer;
        try { answer = await source.GetAsync(companyId, sourceLocationId, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { TransferHostFence.Still(company, user, companyId, actor, cancellationToken); return InventoryTransferScaleSettingV1.Unreadable; }
        TransferHostFence.Still(company, user, companyId, actor, cancellationToken);
        if (answer.CompanyId != companyId || answer.LocationId != sourceLocationId) return InventoryTransferScaleSettingV1.Unreadable;
        return answer switch
        {
            { Status: SettingsWarehouseTransferScaleStatusV1.NotConfigured, RequireExitWeighing: false, RecordedVersion: null }
                when answer.SourceVersion == SettingsWarehouseTransferScaleV1.SourceVersion + "/not-configured" => InventoryTransferScaleSettingV1.Off,
            { Status: SettingsWarehouseTransferScaleStatusV1.Configured, RequireExitWeighing: { } enabled, RecordedVersion: > 0 }
                when answer.SourceVersion == SettingsWarehouseTransferScaleV1.SourceVersion + "/" + answer.RecordedVersion.Value.ToString(CultureInfo.InvariantCulture)
                => enabled ? InventoryTransferScaleSettingV1.On : InventoryTransferScaleSettingV1.Off,
            _ => InventoryTransferScaleSettingV1.Unreadable,
        };
    }
}

internal sealed class InventoryLogisticsTransferReferenceSource(IInventoryTransferReaderV1 source,
    ICompanyContext company, ICurrentUser user) : ILogisticsTransferReferenceSourceV1
{
    public async Task<LogisticsTransferReferenceV1?> ReadAsync(Guid companyId, Guid transferId, CancellationToken cancellationToken = default)
    {
        var actor = TransferHostFence.Member(company, user, companyId, cancellationToken);
        var value = await source.ReadAsync(transferId, cancellationToken);
        TransferHostFence.Still(company, user, companyId, actor, cancellationToken);
        if (value is null) return null;
        if (value.CompanyId != companyId || value.TransferId != transferId || value.SourceLocationId == Guid.Empty
            || !long.TryParse(value.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
            || value.Version != version.ToString(CultureInfo.InvariantCulture) || value.Lines.Count is < 1 or > 200
            || value.Lines.Any(x => x.LineId == Guid.Empty) || value.Lines.Select(x => x.LineId).Distinct().Count() != value.Lines.Count)
            throw new LogisticsTransferDepartureException(503, "logistics_transfer_departure_unavailable");
        return new(companyId, transferId, value.SourceLocationId, value.Status, version, value.DispatchedAtUtc,
            value.Lines.Select(x => x.LineId).ToArray());
    }
}

internal sealed class LogisticsInventoryTransferCancelGate(ILogisticsTransferDepartureOwnerV1 owner,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority) : IInventoryTransferDispatchCancelGatePortV2
{
    public async Task<InventoryTransferDispatchCancelFactsV1> ReadAsync(InventoryTransferDispatchCancelGateQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var actor = TransferHostFence.Member(company, user, query.CompanyId, cancellationToken);
        await PermissionAsync(query.CompanyId, actor, cancellationToken);
        var facts = await owner.ReadAsync(Query(query), cancellationToken);
        await PermissionAsync(query.CompanyId, actor, cancellationToken);
        return Map(query, facts);
    }

    public Task<InventoryTransferDispatchCancelConfirmationV1> ConfirmAsync(InventoryTransferDispatchCancelGateQueryV1 query,
        bool requireLogisticsNoExit, bool requireEDispatchCancel, CancellationToken cancellationToken = default) =>
        throw new LogisticsTransferDepartureException(503, "logistics_transfer_departure_metadata_required");

    public async Task<InventoryTransferDispatchCancelConfirmationV1> ConfirmAsync(InventoryTransferDispatchCancelGateQueryV1 query,
        CancelInventoryTransferDispatchV1 request, bool requireLogisticsNoExit, bool requireEDispatchCancel,
        InventoryCommandMetadata metadata, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
    {
        var actor = TransferHostFence.Member(company, user, query.CompanyId, cancellationToken);
        if (metadata.ActorId != actor || metadata.PermissionCode != ErpPermissions.InventoryStockTransferCancel)
            throw new UnauthorizedAccessException();
        if (request.CancelledAtUtc.Offset != TimeSpan.Zero || request.CancelledAtUtc < query.DispatchedAtUtc
            || CancelInventoryTransferDispatchV1.NormalizeReason(request.Reason) is null)
            throw new LogisticsTransferDepartureException(422, "logistics_transfer_departure_request_invalid");
        async Task Fence(CancellationToken token)
        {
            await freshAuthority(token);
            await PermissionAsync(query.CompanyId, actor, token);
        }
        await Fence(cancellationToken);
        var envelope = new LogisticsWriteEnvelope(actor, metadata.PermissionCode, metadata.RoleCodes, metadata.CorrelationId,
            metadata.CausationId, metadata.IdempotencyKey, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent);
        var result = await owner.ConfirmAsync(new(Query(query), request.ExpectedTransferVersion, request.CancelledAtUtc,
            request.Reason, requireLogisticsNoExit, requireEDispatchCancel), envelope, Fence, cancellationToken);
        await Fence(cancellationToken);
        return new(Map(query, result.Facts), result.NoExitConfirmationVersion, result.EDispatchCancellationVersion);
    }

    internal static InventoryTransferDispatchCancelFactsV1 Map(InventoryTransferDispatchCancelGateQueryV1 query,
        LogisticsTransferDepartureFactsV1 facts)
    {
        var unreadable = new InventoryTransferDepartureFactV1(InventoryTransferDepartureFactStatusV1.Unreadable, null);
        if (facts.CompanyId != query.CompanyId || facts.TransferId != query.TransferId || facts.SourceLocationId != query.SourceLocationId
            || facts.Version is not > 0 || facts.SourceVersion != LogisticsTransferDepartureV1.SourceVersion + "/" + facts.Version.Value.ToString(CultureInfo.InvariantCulture)
            || facts.Association is not (LogisticsTransferEDispatchAssociationV1.NotLinked or LogisticsTransferEDispatchAssociationV1.Linked))
            return new(InventoryTransferEDispatchLinkV1.Unreadable, null, unreadable, unreadable, unreadable);
        InventoryTransferDepartureFactV1 Fact(DateTimeOffset? at) => at is null
            ? new(InventoryTransferDepartureFactStatusV1.NotOccurred, null)
            : at.Value.Offset == TimeSpan.Zero && at >= query.DispatchedAtUtc
                ? new(InventoryTransferDepartureFactStatusV1.Occurred, at) : unreadable;
        if (facts.Association == LogisticsTransferEDispatchAssociationV1.NotLinked)
        {
            if (facts.OfficialDocument is not null) return new(InventoryTransferEDispatchLinkV1.Unreadable, null, unreadable, unreadable, unreadable);
            return new(InventoryTransferEDispatchLinkV1.NotLinked, null, new(InventoryTransferDepartureFactStatusV1.NotOccurred, null),
                Fact(facts.ExitWeighingAtUtc), Fact(facts.VehicleExitAtUtc));
        }
        var proof = facts.OfficialDocument;
        if (proof is not { IsLinked: true, OfficialDocumentId: var officialId, Version: > 0, IsSentToGib: { } sent, LinkedAtUtc: { } linked }
            || officialId == Guid.Empty || proof.CompanyId != query.CompanyId || proof.TransferId != query.TransferId
            || linked == default || linked.Offset != TimeSpan.Zero
            || sent && (proof.SentAtUtc is not { } at || at.Offset != TimeSpan.Zero || at < linked)
            || !sent && proof.SentAtUtc is not null)
            return new(InventoryTransferEDispatchLinkV1.Unreadable, null, unreadable, Fact(facts.ExitWeighingAtUtc), Fact(facts.VehicleExitAtUtc));
        return new(InventoryTransferEDispatchLinkV1.Linked, linked,
            new(sent ? InventoryTransferDepartureFactStatusV1.Occurred : InventoryTransferDepartureFactStatusV1.NotOccurred, proof.SentAtUtc),
            Fact(facts.ExitWeighingAtUtc), Fact(facts.VehicleExitAtUtc));
    }
    private async Task PermissionAsync(Guid companyId, string actor, CancellationToken token)
    {
        TransferHostFence.Still(company, user, companyId, actor, token);
        var permission = await authority.RevalidatePermissionScopeAsync(ErpPermissions.InventoryStockTransferCancel, companyId, token);
        TransferHostFence.Still(company, user, companyId, actor, token);
        if (!permission.IsGranted || !permission.IsCompanyWide) throw new UnauthorizedAccessException();
    }
    private static LogisticsTransferDepartureQueryV1 Query(InventoryTransferDispatchCancelGateQueryV1 query) =>
        new(query.CompanyId, query.TransferId, query.SourceLocationId, query.DispatchedAtUtc);
}

internal static class TransferHostFence
{
    internal static string Member(ICompanyContext company, ICurrentUser user, Guid companyId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(user.UserId) || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
        return user.UserId;
    }
    internal static void Still(ICompanyContext company, ICurrentUser user, Guid companyId, string actor, CancellationToken token)
    { if (Member(company, user, companyId, token) != actor) throw new UnauthorizedAccessException(); }
}
