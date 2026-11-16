using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// ALC-SC33 (FNS-D73 / FNS-D79 / FNS-D80 / FNS-D83 / FNS-D91): the exchange rate a foreign-currency order records as a NUMBER.
/// <list type="bullet">
/// <item>The order opens with the automatic default — the SAT-D54/D170 TCMB forex selling rate the Settings resolver answers for the
/// order's creation business day (previous business day, arife rule; Settings decides, never Purchasing). Without an answer the order
/// opens without a rate; it is never blocked at creation and never given a guessed number.</item>
/// <item>The preparer may change the rate while the order is a Draft (on the order preparer's permission), with an optional reason;
/// the old rate, the new rate and the reason go to the audit record's before/after snapshots (FNS-D91).</item>
/// <item>The rate is required and locked at approval (FNS-D80): an order without one is not approved, and a change after approval
/// is refused — a revision with a new approval is not built (NEEDS-SINAN, never chosen here).</item>
/// </list>
/// The same fences as every Purchasing command (#276), ADR-011 replay first, and the terminal refusals recorded under the key.
/// </summary>
public sealed partial class PurchasingService
{
    /// <summary>A technical bound on the rate text read from the wire or the resolver; the domain enforces the number's own bounds.</summary>
    private const int MaximumExchangeRateTextLength = 64;

    public async Task<PurchaseOrderResponse> ChangeOrderExchangeRateAsync(Guid orderId, ChangePurchaseOrderExchangeRateRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.CreatePermission;
        const string commandName = PurchasingCommands.OrderExchangeRateChangeV1;
        var scope = await RequireAsync(permission, cancellationToken);
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        var requestHash = PurchasingRequestHash.Compute(commandName, orderId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return ReplayedOrder(replay, disclosure);
        }

        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        if (!TryParseExchangeRate(request.Rate, out var rate))
        {
            throw new PurchasingServiceException(422, PurchasingProblemCodesV1.OrderExchangeRateInvalid,
                [new("rate", PurchasingRuleCodes.OrderExchangeRateInvalid)]);
        }

        // FNS-D83: without a named rate day the order's creation business day applies; the zone is required (503, never UTC).
        var zone = BusinessZone.Value ?? throw new PurchasingServiceException(503, "service_unavailable");

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        var actor = scope.ActorId.Trim();
        PurchasingOrderStoreResult? result;
        try
        {
            result = await store.MutateOrderAsync(envelope, requestHash, commandName, orderId,
                (order, now) => order.SetExchangeRate(expectedVersion,
                    new PurchaseOrderExchangeRate(rate, SettingsCurrencyIdentitiesV1.TurkishLira, PurchaseOrderExchangeRateKind.TcmbForexSelling,
                        request.RateDate ?? BusinessDateOf(order.CreatedAtUtc, zone), PurchaseOrderExchangeRateSource.Manual, null),
                    actor, request.Reason, now),
                new PurchasingMutationAudit("purchasing.purchase-order.exchange-rate-changed.v1", "ChangeExchangeRate",
                    "Satınalma siparişinin kuru değiştirildi"), cancellationToken);
        }
        catch (PurchasingVersionConflictException)
        {
            return await ExchangeRateTerminalAsync(scope, permission, envelope, requestHash, PurchasingProblemCodesV1.VersionConflict,
                "expectedVersion", "version_conflict", disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.OrderExchangeRateLocked)
        {
            return await ExchangeRateTerminalAsync(scope, permission, envelope, requestHash, PurchasingProblemCodesV1.OrderExchangeRateLocked,
                "status", exception.Code, disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchasingRuleCodes.OrderExchangeRateInvalid
                                                             or PurchasingRuleCodes.OrderExchangeRateNotApplicable)
        {
            var code = exception.Code == PurchasingRuleCodes.OrderExchangeRateInvalid
                ? PurchasingProblemCodesV1.OrderExchangeRateInvalid
                : PurchasingProblemCodesV1.OrderExchangeRateNotApplicable;
            throw new PurchasingServiceException(422, code, [new("rate", exception.Code)]);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.OrderExchangeRateReasonTooLong)
        {
            throw new PurchasingServiceException(422, PurchasingProblemCodesV1.OrderExchangeRateReasonTooLong, [new("reason", exception.Code)]);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("order", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedOrder(won, disclosure);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new PurchasingServiceException(404, "not_found")
            : OrderResponse(result.Order, result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId, disclosure);
    }

    public async Task<PurchaseOrderExchangeRateView> GetOrderExchangeRateAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        StoredPurchaseOrder? stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return stored is null ? throw new PurchasingServiceException(404, "not_found") : ExchangeRateView(stored.Order);
    }

    /// <summary>The recorded rate as stored; the screen shows these values and never recomputes them.</summary>
    private static PurchaseOrderExchangeRateView ExchangeRateView(PurchaseOrder order)
    {
        var functional = SettingsCurrencyIdentitiesV1.TurkishLira;
        var applicable = order.Money is { } money && money.CurrencyId != functional;
        var draft = order.Status == PurchaseOrderStatus.Draft;
        var current = order.ExchangeRate is { } rate && order.LastExchangeRateChange is { } change
            ? new PurchaseOrderExchangeRateEntryView(rate.Rate.ToString(CultureInfo.InvariantCulture), rate.Kind.ToString(), rate.RateDay,
                rate.Source.ToString(), rate.BulletinDate, change.ChangedBy, change.ChangedAtUtc, change.Reason)
            : null;
        return new PurchaseOrderExchangeRateView(order.Id, order.Status.ToString(), order.Version.ToString(CultureInfo.InvariantCulture),
            order.Money?.CurrencyId, functional, applicable,
            draft && applicable && (order.ExchangeRate is null || order.ExchangeRate.TargetCurrencyId != functional),
            draft && applicable, current);
    }

    /// <summary>
    /// FNS-D79: the automatic default for a new order — the Settings resolver's TCMB forex selling rate for <paramref name="rateDay"/>
    /// into the functional currency (the Sales mapping). A denied answer is 403; a resolved answer for another target breaks the owner's
    /// contract (503); every other missing, unresolved or malformed answer leaves the order without a default (null), never a guess.
    /// </summary>
    private async Task<PurchaseOrderExchangeRate?> ReadDefaultExchangeRateAsync(Scope scope, string? permission, Guid currencyId,
        DateOnly rateDay, CancellationToken cancellationToken)
    {
        if (exchangeRates is not { } resolver)
        {
            return null;
        }

        ExchangeRateResolutionV1? answer;
        try
        {
            answer = await resolver.ResolveAsync(rateDay, currencyId, SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == ExchangeRateProblemCodesV1.AccessDenied)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw new PurchasingServiceException(403, "forbidden");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            answer = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, cancellationToken);
        if (answer is { State: ExchangeRateResolutionStatesV1.Resolved } && answer.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira)
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
        }

        if (answer is not { State: ExchangeRateResolutionStatesV1.Resolved, BulletinDate: { } bulletin, SellingRate: { } text }
            || answer.SourceCurrencyId != currencyId || answer.RateDay != rateDay || !TryParseExchangeRate(text, out var value))
        {
            return null;
        }

        try
        {
            return new PurchaseOrderExchangeRate(value, SettingsCurrencyIdentitiesV1.TurkishLira, PurchaseOrderExchangeRateKind.TcmbForexSelling,
                rateDay, PurchaseOrderExchangeRateSource.Automatic, bulletin);
        }
        catch (PurchasingRuleException)
        {
            // A number outside the order's bounds is not taken as the default; the preparer enters the rate before approval.
            return null;
        }
    }

    /// <summary>The exact decimal text (ASCII digits with an optional single point); signs, exponents, spaces and separators are refused.</summary>
    private static bool TryParseExchangeRate(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumExchangeRateTextLength || text[0] == '.' || text[^1] == '.')
        {
            return false;
        }

        var points = 0;
        foreach (var character in text)
        {
            if (character == '.')
            {
                points++;
            }
            else if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return points <= 1 && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value > 0m;
    }

    /// <summary>SAT-D10: the Europe/Istanbul calendar day of an instant; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateOnly BusinessDateOf(DateTimeOffset instant) =>
        BusinessDateOf(instant, BusinessZone.Value ?? throw new PurchasingServiceException(503, "service_unavailable"));

    private static DateOnly BusinessDateOf(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private const string BusinessTimeZoneId = "Europe/Istanbul";

    private static readonly Lazy<TimeZoneInfo?> BusinessZone = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private Task<PurchaseOrderResponse> ExchangeRateTerminalAsync(Scope scope, string? permission, PurchasingWriteEnvelope envelope,
        string requestHash, string code, string field, string fieldCode, string disclosure, CancellationToken cancellationToken) =>
        TerminalAsync(scope, permission, envelope, requestHash, PurchasingCommands.OrderExchangeRateChangeV1,
            new PurchasingTerminalRefusal(409, code, [new(field, fieldCode)]), replayed => ReplayedOrder(replayed, disclosure), cancellationToken);
}
