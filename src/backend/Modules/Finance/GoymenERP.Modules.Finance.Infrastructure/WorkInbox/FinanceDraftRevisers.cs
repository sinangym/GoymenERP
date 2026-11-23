using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// FNS-D118 (P6 with No 103): the expense payment and the manual bank payment have no separate "submit" step, so everyone
/// whose draft save succeeded for a payment is a maker and the owner's approve command refuses them
/// (<c>RevisedByAsync</c> in each service). This read answers that set from the same command rows, per payment, without
/// the first preparer (who is the item's <see cref="WorkInboxItemV1.PreparedBy"/>), ordinal and sorted. Null when a stored
/// result cannot be read as the owner's own: the source then answers Unavailable rather than a partial set.
/// </summary>
internal static class FinanceDraftRevisers
{
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>?> ExpensePaymentsAsync(
        FinanceDbContext db, Guid companyId, IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken)
    {
        var rows = await db.ExpensePaymentCommands.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.CommandName == ExpensePaymentService.DraftCommand
                && row.PaymentId != null && paymentIds.Contains(row.PaymentId!.Value))
            .Select(row => new { PaymentId = row.PaymentId!.Value, row.ActorId, row.ResultJson })
            .ToListAsync(cancellationToken);
        return Group(rows.Select(row => (row.PaymentId, row.ActorId,
            Succeeded<ExpensePaymentCommandResult>(row.ResultJson, result => result.Outcome))));
    }

    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>?> ManualBankPaymentsAsync(
        FinanceDbContext db, Guid companyId, IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken)
    {
        var rows = await db.Commands.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.CommandName == ManualBankPaymentService.DraftCommand
                && row.ManualBankPaymentId != null && paymentIds.Contains(row.ManualBankPaymentId!.Value))
            .Select(row => new { PaymentId = row.ManualBankPaymentId!.Value, row.ActorId, row.ResultJson })
            .ToListAsync(cancellationToken);
        return Group(rows.Select(row => (row.PaymentId, row.ActorId,
            Succeeded<ManualBankPaymentCommandResult>(row.ResultJson, result => result.Outcome))));
    }

    /// <summary>The revisers of one payment besides its first preparer, or null when there are more than the contract carries.</summary>
    public static IReadOnlyList<string>? ExcludedBesides(
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> revisers, Guid paymentId, string preparedBy)
    {
        var others = revisers.TryGetValue(paymentId, out var all)
            ? all.Where(actor => !WorkInboxRulesV1.SameUser(actor, preparedBy)).ToArray()
            : [];
        return others.Length > WorkInboxRulesV1.MaxExcludedDecidersPerItem ? null : others;
    }

    private static bool? Succeeded<TResult>(string json, Func<TResult, CommandOutcome> outcome) where TResult : class
    {
        try
        {
            return JsonSerializer.Deserialize<TResult>(json, Json) is { } result ? outcome(result) == CommandOutcome.Succeeded : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<string>>? Group(IEnumerable<(Guid PaymentId, string ActorId, bool? Succeeded)> rows)
    {
        var map = new Dictionary<Guid, SortedSet<string>>();
        foreach (var (paymentId, actorId, succeeded) in rows)
        {
            if (succeeded is null || string.IsNullOrWhiteSpace(actorId)) return null;
            if (succeeded is false) continue;
            if (!map.TryGetValue(paymentId, out var actors)) map[paymentId] = actors = new SortedSet<string>(StringComparer.Ordinal);
            actors.Add(actorId);
        }

        return map.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray());
    }
}
