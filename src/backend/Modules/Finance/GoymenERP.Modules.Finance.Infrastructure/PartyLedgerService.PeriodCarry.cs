using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class PartyLedgerService
{
    private sealed record PeriodCarryOpening(PartyLedgerPeriodCarryV1 Proof, IReadOnlyList<PartyLedgerOpeningBalance> Balances);

    private async Task<PeriodCarryOpening?> ReadPeriodCarryOpeningAsync(FinanceContext context,
        PartyLedgerRequest request, CancellationToken cancellationToken)
    {
        var voucher = await dbContext.PeriodCarryVouchers.AsNoTracking()
            .Where(x => x.TargetStartsOn <= request.From && !dbContext.PeriodCarryReversals.Any(r => r.VoucherId == x.Id))
            .OrderByDescending(x => x.TargetStartsOn).ThenByDescending(x => x.ClosedPeriodVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (voucher is null) return null;
        if (voucher.CompanyId != context.CompanyId || voucher.Id == Guid.Empty || voucher.ClosureId == Guid.Empty
            || voucher.RecordedBy == Guid.Empty || voucher.RecordedAtUtc == default || voucher.RecordedAtUtc.Offset != TimeSpan.Zero
            || voucher.SourceEndsOn >= voucher.TargetStartsOn
            || voucher.SourcePeriodId == Guid.Empty || voucher.TargetPeriodId == Guid.Empty
            || voucher.SourcePeriodId == voucher.TargetPeriodId || voucher.ClosedPeriodVersion < 2
            || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(voucher.SourceJson + "|" + voucher.LinesJson))) != voucher.SourceHash)
            throw new InvalidOperationException("Devir kaynak kanıtı doğrulanamadı.");
        FinancePeriodCarrySourceV1 source;
        FinancePeriodCarryLineV1[] lines;
        try
        {
            var options = FinanceJson.CreateOptions();
            source = JsonSerializer.Deserialize<FinancePeriodCarrySourceV1>(voucher.SourceJson, options)
                ?? throw new InvalidOperationException("Devir kaynağı eksik.");
            lines = JsonSerializer.Deserialize<FinancePeriodCarryLineV1[]>(voucher.LinesJson, options)
                ?? throw new InvalidOperationException("Devir satırları eksik.");
        }
        catch (JsonException exception) { throw new InvalidOperationException("Devir kaynağı okunamadı.", exception); }
        if (source.CompanyId != context.CompanyId || source.ClosureId != voucher.ClosureId
            || source.SourcePeriodId != voucher.SourcePeriodId || source.ClosedPeriodVersion != voucher.ClosedPeriodVersion
            || source.SourceEndsOn != voucher.SourceEndsOn || source.TargetPeriodId != voucher.TargetPeriodId
            || source.TargetStartsOn != voucher.TargetStartsOn || source.ActorId != voucher.RecordedBy
            || source.RecordedAtUtc != voucher.RecordedAtUtc || source.SourceStartsOn > source.SourceEndsOn
            || source.TargetEndsOn < source.TargetStartsOn)
            throw new InvalidOperationException("Devir başlık ve satır kanıtları uyuşmuyor.");
        var balances = new List<PartyLedgerOpeningBalance>();
        var keys = new HashSet<(string, Guid, Guid?, Guid)>();
        foreach (var line in lines)
        {
            if (line is null || line.AccountId == Guid.Empty || line.CurrencyId == Guid.Empty
                || line.AccountKind is not ("Party" or "Bank" or "Cash")
                || (line.AccountKind == "Party" ? line.PartyId is null || line.PartyId == Guid.Empty : line.PartyId is not null)
                || !keys.Add((line.AccountKind, line.AccountId, line.PartyId, line.CurrencyId))
                || !FinanceExactDecimalText.TryParseNonNegative(line.Debit, out var debit)
                || !FinanceExactDecimalText.TryParseNonNegative(line.Credit, out var credit)
                || debit.IsZero == credit.IsZero)
                throw new InvalidOperationException("Devir satırının tam bakiye kaynağı doğrulanamadı.");
            if (line.AccountKind == "Party" && line.PartyId == request.PartyId && line.AccountId == request.CompanyAccountId)
                balances.Add(new(line.CurrencyId, debit.IsZero ? PartyLedgerBalanceSide.Credit : PartyLedgerBalanceSide.Debit,
                    debit.IsZero ? credit.Text : debit.Text));
        }
        // The requested range may start after the carry's target day. Only actual intervening facts
        // advance that starting balance; neither a synthetic movement nor an all-time balance is added.
        var bridgeFrom = voucher.SourceEndsOn.AddDays(1);
        if (bridgeFrom < request.From)
        {
            var bridge = await ReadCoreAsync(context, request with { From = bridgeFrom, To = request.From.AddDays(-1) },
                PartyBalanceRules.MaximumFacts, includePeriodCarry: false, cancellationToken, projectInternalAmounts: true);
            if (bridge.Outcome != PartyLedgerOutcome.Found || bridge.Ledger is not { } ledger)
                throw new InvalidOperationException("Devir ile seçilen aralık arasındaki tam kaynak okunamadı.");
            foreach (var currency in ledger.Currencies)
            {
                if (currency.ClosingBalance.Amount is not { Disclosure: AmountDisclosure.Visible, Money: { } money }
                    || money.CurrencyId != currency.CurrencyId || currency.ClosingBalance.Side is not { } side)
                    throw new InvalidOperationException("Aralık öncesi bakiye açıklanabilir değil.");
                balances.Add(new(currency.CurrencyId, side, money.Amount));
            }
        }
        return new(new(voucher.Id, voucher.SourcePeriodId, voucher.SourceEndsOn, voucher.TargetPeriodId,
            voucher.TargetStartsOn, voucher.SourceHash), balances);
    }

    private Task<bool> PeriodCarryStillActiveAsync(PartyLedgerPeriodCarryV1 proof, CancellationToken token) =>
        dbContext.PeriodCarryVouchers.AsNoTracking().AnyAsync(x => x.Id == proof.VoucherId && x.SourceHash == proof.SourceHash
            && !dbContext.PeriodCarryReversals.Any(r => r.VoucherId == x.Id), token);
}
