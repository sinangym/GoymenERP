using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Modules.Logistics.Application;

public sealed partial class LogisticsCustomerReturnService
{
    public async Task<CustomerReturnResponseV1> RecordWeighingAsync(Guid customerReturnId, RecordCustomerReturnWeighingV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        RequireId(customerReturnId); RequireId(request.WeighingId);
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        const string permission = LogisticsPermissionManifestV1.WeighingsRecord;
        const string command = LogisticsCustomerReturnCommandsV1.RecordWeighing;
        var scope = await scopes.RequireAsync(permission, cancellationToken);
        var envelope = Envelope(scope, permission, metadata);
        var hash = LogisticsRequestHash.Compute(command, customerReturnId, request);
        try
        {
            return await ProvenAsync(FaultFencedAsync(() => transactions.ExecuteLogisticsAsync(scope.CompanyId, async token =>
            {
                var replay = await GuardedAsync(scope, permission, () => store.FindReplayAsync(envelope, hash, command, token), token);
                if (replay is not null) return Replayed(replay);
                var pending = await RequirePendingAsync(scope, permission, customerReturnId, token);
                if (!Enum.TryParse<WeighingDirection>(request.Direction, out var direction) || direction.ToString() != request.Direction)
                    throw new LogisticsCustomerReturnServiceException(422, "validation_failed");
                pending.RecordWeighing(expectedVersion, request.WeighingId, direction,
                    ParseQuantity(request.FirstWeighingKg, QuantityUnit.Kilogram), ParseQuantity(request.SecondWeighingKg, QuantityUnit.Kilogram),
                    scope.ActorId, timeProvider.GetUtcNow());
                var saved = await store.StoreWeighingAsync(envelope, hash, pending, token);
                return Response(saved.Return, saved.Outcome == LogisticsWriteOutcome.Replayed, saved.OriginalCorrelationId);
            }, token => scopes.RequireStillAsync(scope, permission, token), cancellationToken),
                () => scopes.RequireStillAsync(scope, permission, cancellationToken)));
        }
        catch (CustomerReturnVersionConflictException error)
        { return await TerminalAsync(scope, permission, envelope, hash, command, VersionConflict(error), cancellationToken); }
        catch (CustomerReturnRuleException error) { throw Rule(error); }
        catch (GoymenERP.Modules.Logistics.Domain.LogisticsRuleException)
        { throw new LogisticsCustomerReturnServiceException(422, "validation_failed"); }
        catch (ArgumentException) { throw new LogisticsCustomerReturnServiceException(422, "validation_failed"); }
        catch (LogisticsCustomerReturnServiceException error) when (IsRecordableTerminal(error))
        { return await TerminalAsync(scope, permission, envelope, hash, command, error, cancellationToken); }
        catch (LogisticsStoreException error) when (error.Code == LogisticsStoreCodes.ConcurrentChange)
        { return await RecoverWinnerAsync(scope, permission, envelope, hash, command, error, cancellationToken); }
    }

    private static CustomerReturnWeighingViewV1? WeighingView(CustomerReturnWeighing? value) => value is null ? null : new(
        value.WeighingId, value.Direction.ToString(), Weight(value.FirstWeighing.Value), Weight(value.SecondWeighing.Value), Weight(value.Net.Value),
        value.RecordedBy, value.RecordedAtUtc);
    private static string Weight(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
