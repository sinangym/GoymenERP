using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>One immutable native due-plan entry; no date or amount is supplied by the invoice screen.</summary>
public sealed record SalesSubcontractDuePlanEntry(int SequenceNo, DateOnly DueDate, string SharePercent, decimal Amount);

/// <summary>The functional amounts calculated once from the frozen service line and the official-date rate.</summary>
public sealed record SalesSubcontractFunctionalLine(Guid LineId, decimal Net, decimal Vat, decimal Withholding,
    IReadOnlyList<SalesInvoiceAmountType> Adjusted, bool NeedsReview);

/// <summary>Persistence envelope for the service invoice's lifecycle; older drafts have no envelope.</summary>
public sealed record SalesSubcontractInvoiceLifecycleFacts(
    SalesInvoiceDecisionFacts Decision,
    IReadOnlyList<SalesSubcontractDuePlanEntry> DuePlan,
    SalesInvoiceRateSnapshot? Rate,
    IReadOnlyList<SalesSubcontractFunctionalLine> FunctionalLines,
    SalesInvoiceMatchFacts? Match,
    SalesInvoiceFinalization? Finalization,
    Guid? FunctionalCurrencyId = null);

public sealed partial class SalesSubcontractServiceInvoice
{
    private readonly List<SalesSubcontractDuePlanEntry> duePlan = [];
    private readonly List<SalesSubcontractFunctionalLine> functionalLines = [];
    public SalesInvoiceDecisionFacts Decision { get; private set; } = SalesInvoiceDecisionFacts.None;
    public IReadOnlyList<SalesSubcontractDuePlanEntry> DuePlan => duePlan.AsReadOnly();
    public IReadOnlyList<SalesSubcontractFunctionalLine> FunctionalLines => functionalLines.AsReadOnly();
    public SalesInvoiceRateSnapshot? Rate { get; private set; }
    public SalesInvoiceMatchFacts? Match { get; private set; }
    public SalesInvoiceFinalization? Finalization { get; private set; }
    public Guid? FunctionalCurrencyId { get; private set; }

    public SalesSubcontractInvoiceLifecycleFacts LifecycleFacts => new(Decision, DuePlan, Rate, FunctionalLines, Match, Finalization, FunctionalCurrencyId);

    public void SetDuePlan(SalesDueTerms terms, IReadOnlyList<SalesDueDate> dates)
    {
        RequireLifecycleState(SalesSubcontractServiceInvoiceState.Draft);
        if (duePlan.Count != 0 || dates.Count != terms.Instalments.Count || dates.Any(item => !item.IsResolved))
            throw new SalesRuleException(SalesRuleCodes.InvoiceDueDateMissing, "Fason faturanın kaynak vade planı çözümlenemedi.");
        var split = SalesDueDateCalculator.SplitAmount(terms, Totals.Payable);
        var prepared = new List<SalesSubcontractDuePlanEntry>(split.Count);
        for (var index = 0; index < split.Count; index++)
        {
            if (split[index].SequenceNo != dates[index].SequenceNo || dates[index].DueDate is not { } date || date == default)
                throw new ArgumentException("Vade planı sırası ve tarihleri uyuşmuyor.", nameof(dates));
            prepared.Add(new(split[index].SequenceNo, date, dates[index].SharePercent, split[index].Amount.Amount));
        }
        duePlan.AddRange(prepared);
    }

    public void Submit(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireLifecycleVersion(expectedVersion);
        RequireLifecycleState(SalesSubcontractServiceInvoiceState.Draft);
        if (duePlan.Count == 0)
            throw new SalesRuleException(SalesRuleCodes.InvoiceDueDateMissing, "Fason faturanın kaynak vade planı eksik.");
        Decision = Decision with { SubmittedBy = Actor(actorId), SubmittedAtUtc = now.ToUniversalTime() };
        State = SalesSubcontractServiceInvoiceState.PendingApproval;
        AdvanceLifecycle();
    }

    public void Approve(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireLifecycleVersion(expectedVersion);
        RequireLifecycleState(SalesSubcontractServiceInvoiceState.PendingApproval);
        var actor = Actor(actorId);
        RequireOtherPreparer(actor);
        Decision = Decision with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), DecisionReason = null };
        State = SalesSubcontractServiceInvoiceState.Approved;
        AdvanceLifecycle();
    }

    public void Reject(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireLifecycleVersion(expectedVersion);
        RequireLifecycleState(SalesSubcontractServiceInvoiceState.PendingApproval);
        var actor = Actor(actorId);
        RequireOtherPreparer(actor);
        Decision = Decision with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), DecisionReason = Reason(reason) };
        State = SalesSubcontractServiceInvoiceState.Rejected;
        AdvanceLifecycle();
    }

    public void Cancel(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireLifecycleVersion(expectedVersion);
        if (State is not (SalesSubcontractServiceInvoiceState.Draft or SalesSubcontractServiceInvoiceState.PendingApproval))
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Yalnız taslak veya onay bekleyen fatura iptal edilir.");
        Decision = Decision with { CancelledBy = Actor(actorId), CancelledAtUtc = now.ToUniversalTime(), CancellationReason = Reason(reason) };
        State = SalesSubcontractServiceInvoiceState.Cancelled;
        AdvanceLifecycle();
    }

    /// <summary>SAT-D380: an official-date rate is recorded only with the actual official match, never the draft day.</summary>
    public void RecordMatch(long expectedVersion, string actorId, SalesOfficialInvoiceFacts official,
        Guid functionalCurrencyId, SalesInvoiceRateSnapshot? rate, decimal tolerance, bool approveDifference,
        string? differenceReason, DateTimeOffset now)
    {
        RequireLifecycleVersion(expectedVersion);
        RequireLifecycleState(SalesSubcontractServiceInvoiceState.Approved);
        if (Match is not null) throw new SalesRuleException(SalesRuleCodes.StateConflict, "Fatura zaten eşleştirildi.");
        if (!official.HasCompleteSourceProof(CompanyId))
            throw new SalesRuleException(SalesRuleCodes.InvoiceMatchSourceProofRequired, "Resmî fatura tarihi ve kaynak kanıtı eksik.");
        if (functionalCurrencyId == Guid.Empty || (Header.CurrencyId == functionalCurrencyId ? rate is not null
            : rate is null || rate.FunctionalCurrencyId != functionalCurrencyId || rate.RateDay != official.InvoiceDate || rate.Rate <= 0m))
            throw new SalesRuleException(SalesRuleCodes.InvoiceMatchSourceProofRequired, "Resmî fatura tarihinin onaylı kur kaynağı eksik veya uyuşmuyor.");
        var converted = ConvertAtOfficialRate(rate);
        var functional = converted.FunctionalTotals ?? Totals;
        var evaluation = new SalesInvoiceMatchEvaluation(Header.PartyId == official.CustomerPartyId,
            Header.CurrencyId == official.CurrencyId, functional.Payable.Amount, official.PayableFunctionalAmount,
            Math.Abs(functional.Payable.Amount - official.PayableFunctionalAmount),
            new(functional.Vat.Amount, official.VatFunctionalAmount, Math.Abs(functional.Vat.Amount - official.VatFunctionalAmount),
                functional.Withholding.Amount, official.WithholdingFunctionalAmount,
                Math.Abs(functional.Withholding.Amount - official.WithholdingFunctionalAmount)));
        var actor = Actor(actorId);
        var match = SalesInvoice.BuildMatchFacts(CompanyId, official, evaluation, actor, tolerance,
            approveDifference, differenceReason, () => RequireOtherPreparer(actor), now);
        var frozen = converted.Lines.Where(item => item.Functional is not null).Select(item =>
            new SalesSubcontractFunctionalLine(item.LineId, item.Functional!.Amounts.Net.Amount,
                item.Functional.Amounts.Vat.Amount, item.Functional.Amounts.Withholding.Amount,
                item.Functional.Adjusted, item.Functional.NeedsReview)).ToArray();
        Match = match;
        Rate = rate;
        FunctionalCurrencyId = functionalCurrencyId;
        functionalLines.AddRange(frozen);
        AdvanceLifecycle();
    }

    public SalesInvoiceFinalization Finalize(long expectedVersion, string actorId, Func<Guid> newId, DateTimeOffset now)
    {
        RequireLifecycleVersion(expectedVersion);
        RequireLifecycleState(SalesSubcontractServiceInvoiceState.Approved);
        if (Match is null) throw new SalesRuleException(SalesRuleCodes.InvoiceMatchRequired, "Fatura resmî belgeyle eşleştirilmedi.");
        if (!Match.HasCompleteSourceProof(CompanyId))
            throw new SalesRuleException(SalesRuleCodes.InvoiceMatchSourceProofRequired, "Resmî fatura kaynak kanıtı eksik.");
        if (Match.HasOpenDifference)
            throw new SalesRuleException(SalesRuleCodes.InvoiceMatchDifferenceOpen, "Resmî belge farkı çözülmeden kesinleştirme yapılamaz.");
        var nativePlan = duePlan.Select(item => new SalesInvoiceInstalment(item.SequenceNo, item.DueDate,
            item.SharePercent, Money.Of(item.Amount, Header.CurrencyId, Header.AmountScale))).ToArray();
        var split = SalesInvoiceInstalmentCalculator.FunctionalAmounts(nativePlan, Rate, Header.CurrencyId, FunctionalTotals?.Payable);
        var plan = duePlan.Select((item, index) => new SalesInvoiceFinalInstalment(Identity.Require(newId(), "openItemId"),
            Identity.Require(newId(), "installmentId"), item.SequenceNo, item.DueDate, item.Amount, split[index])).ToArray();
        var facts = new SalesInvoiceFinalization(Identity.Require(newId(), "admissionId"), checked(Version + 1), Actor(actorId),
            now.ToUniversalTime(), SalesInvoiceFinanceStatus.Waiting, null, 0, null, null, plan);
        Finalization = facts;
        State = SalesSubcontractServiceInvoiceState.Finalized;
        AdvanceLifecycle();
        return facts;
    }

    public SalesInvoiceAmountSet? FunctionalTotals => Rate is { } rate
        ? SalesInvoiceCalculator.Sum(functionalLines.Select(item => SalesInvoiceCalculator.Set(
            Money.Of(item.Net, rate.FunctionalCurrencyId, rate.FunctionalScale),
            Money.Of(item.Vat, rate.FunctionalCurrencyId, rate.FunctionalScale),
            Money.Of(item.Withholding, rate.FunctionalCurrencyId, rate.FunctionalScale))).ToArray(), rate.FunctionalCurrencyId, rate.FunctionalScale)
        : null;

    public SalesInvoiceAmountSet? FunctionalAmounts(Guid lineId)
    {
        var line = functionalLines.SingleOrDefault(item => item.LineId == lineId);
        return line is not null && Rate is { } rate ? SalesInvoiceCalculator.Set(
            Money.Of(line.Net, rate.FunctionalCurrencyId, rate.FunctionalScale),
            Money.Of(line.Vat, rate.FunctionalCurrencyId, rate.FunctionalScale),
            Money.Of(line.Withholding, rate.FunctionalCurrencyId, rate.FunctionalScale)) : null;
    }

    public SalesInvoiceFinalization RecordFinanceAttempt(bool admitted, string outcome, DateTimeOffset now)
    {
        var current = Finalization ?? throw new SalesRuleException(SalesRuleCodes.StateConflict, "Fatura kesinleştirilmedi.");
        if (State != SalesSubcontractServiceInvoiceState.Finalized || current.FinanceStatus == SalesInvoiceFinanceStatus.Admitted)
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Finans kabulü bu durumda değiştirilemez.");
        var at = now.ToUniversalTime();
        Finalization = current with
        {
            FinanceStatus = admitted ? SalesInvoiceFinanceStatus.Admitted : SalesInvoiceFinanceStatus.Waiting,
            AdmittedAtUtc = admitted ? at : null,
            AttemptCount = checked(current.AttemptCount + 1), LastAttemptAtUtc = at,
            LastOutcome = RequireText(outcome, SalesInvoiceFinalization.MaximumOutcomeLength, nameof(outcome)),
        };
        return Finalization;
    }

    private void RestoreLifecycle(SalesSubcontractInvoiceLifecycleFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Decision is null || facts.DuePlan is null || facts.FunctionalLines is null
            || !SalesInvoice.DecisionIsConsistent((SalesInvoiceState)State, CreatedBy, facts.Decision)
            || facts.DuePlan.Count > SalesDueTerms.MaximumInstalments || facts.DuePlan.Any(item => item is null)
            || facts.FunctionalLines.Any(item => item is null || item.Adjusted is null)
            || facts.DuePlan.Select((item, index) => item.SequenceNo != index + 1 || item.DueDate == default
                || item.Amount < 0m || Money.Of(item.Amount, Header.CurrencyId, Header.AmountScale).Amount != item.Amount).Any(bad => bad)
            || (facts.DuePlan.Count > 0 && facts.DuePlan.Sum(item => item.Amount) != Totals.Payable.Amount)
            || (State is not (SalesSubcontractServiceInvoiceState.Draft or SalesSubcontractServiceInvoiceState.Cancelled)
                && facts.DuePlan.Count == 0)
            || (facts.Match is not null && State is not (SalesSubcontractServiceInvoiceState.Approved or SalesSubcontractServiceInvoiceState.Finalized))
            || (facts.Match is null && (facts.Rate is not null || facts.FunctionalLines.Count > 0 || facts.FunctionalCurrencyId is not null))
            || (facts.Match is not null && (facts.FunctionalCurrencyId is null || facts.FunctionalCurrencyId == Guid.Empty
                || (facts.FunctionalCurrencyId == Header.CurrencyId ? facts.Rate is not null
                    : facts.Rate is null || facts.Rate.FunctionalCurrencyId != facts.FunctionalCurrencyId)))
            || (facts.Rate is null ? facts.FunctionalLines.Count != 0 : facts.FunctionalLines.Count != lines.Count))
            throw new InvalidOperationException("Kayıtlı fason fatura karar/vade/kur kanıtı tutarsız.");
        if (facts.Rate is { } rate && (rate.Rate <= 0 || rate.RateDay != facts.Match?.InvoiceDate
            || rate.FunctionalCurrencyId == Header.CurrencyId || rate.FunctionalScale is < 0 or > Money.MaximumScale
            || facts.FunctionalLines.Select(item => item.LineId).Distinct().Count() != lines.Count
            || facts.FunctionalLines.Any(item => lines.All(line => line.Id != item.LineId)
                || item.Net < 0m || item.Vat < 0m || item.Withholding < 0m || item.Withholding > item.Net + item.Vat)))
            throw new InvalidOperationException("Kayıtlı fason fatura kur tutarları tutarsız.");
        Decision = facts.Decision;
        duePlan.AddRange(facts.DuePlan);
        Rate = facts.Rate;
        FunctionalCurrencyId = facts.FunctionalCurrencyId;
        functionalLines.AddRange(facts.FunctionalLines);
        Match = facts.Match;
        if (Match is { } match)
        {
            var amount = FunctionalTotals ?? Totals;
            var approval = match.Exceeded ? match.DifferenceApprovedBy is not null && !string.IsNullOrWhiteSpace(match.DifferenceReason)
                && match.DifferenceApprovedBy != CreatedBy && match.DifferenceApprovedBy != Decision.SubmittedBy
                : match.DifferenceApprovedBy is null && match.DifferenceReason is null;
            if (!match.HasCompleteSourceProof(CompanyId) || !approval || match.Tolerance < 0m
                || match.Taxes is not { IsConsistent: true } taxes || string.IsNullOrWhiteSpace(match.MatchedBy)
                || string.IsNullOrWhiteSpace(match.OfficialNo) || !Guid.TryParseExact(match.Ettn, "D", out var ettn) || ettn == Guid.Empty
                || match.InvoiceFunctionalAmount != amount.Payable.Amount
                || match.Difference != Math.Abs(match.InvoiceFunctionalAmount - match.OfficialFunctionalAmount)
                || taxes.InvoiceVatAmount != amount.Vat.Amount || taxes.InvoiceWithholdingAmount != amount.Withholding.Amount)
                throw new InvalidOperationException("Kayıtlı fason fatura resmî eşleme kanıtı tutarsız.");
        }
        if ((State == SalesSubcontractServiceInvoiceState.Finalized) != (facts.Finalization is not null))
            throw new InvalidOperationException("Kayıtlı fason fatura kesinleştirme durumu tutarsız.");
        if (facts.Finalization is { } final)
        {
            var admitted = final.FinanceStatus == SalesInvoiceFinanceStatus.Admitted;
            var expectedFunctional = SalesInvoiceInstalmentCalculator.FunctionalAmounts(duePlan.Select(item =>
                new SalesInvoiceInstalment(item.SequenceNo, item.DueDate, item.SharePercent,
                    Money.Of(item.Amount, Header.CurrencyId, Header.AmountScale))).ToArray(), Rate, Header.CurrencyId, FunctionalTotals?.Payable);
            if (Match is null || Match.HasOpenDifference || final.AdmissionId == Guid.Empty || final.FinalizedVersion != Version
                || string.IsNullOrWhiteSpace(final.FinalizedBy) || final.FinalizedAtUtc == default || final.AttemptCount < 0
                || !Enum.IsDefined(final.FinanceStatus) || admitted != final.AdmittedAtUtc.HasValue
                || (final.AttemptCount == 0) != (final.LastAttemptAtUtc is null)
                || (final.AttemptCount == 0 ? final.LastOutcome is not null : string.IsNullOrWhiteSpace(final.LastOutcome))
                || final.LastOutcome?.Length > SalesInvoiceFinalization.MaximumOutcomeLength || (admitted && final.AttemptCount == 0)
                || final.Plan is null || final.Plan.Count != duePlan.Count
                || final.Plan.Any(item => item is null || item.OpenItemId == Guid.Empty || item.InstallmentId == Guid.Empty)
                || final.Plan.Select(item => item.OpenItemId).Distinct().Count() != final.Plan.Count
                || final.Plan.Select(item => item.InstallmentId).Distinct().Count() != final.Plan.Count
                || final.Plan.Select((item, index) => item.SequenceNo != duePlan[index].SequenceNo
                    || item.Amount != duePlan[index].Amount || item.DueDate != duePlan[index].DueDate).Any(bad => bad)
                || final.Plan.Select((item, index) => item.FunctionalAmount != expectedFunctional[index]).Any(bad => bad))
                throw new InvalidOperationException("Kayıtlı fason fatura Finans planı tutarsız.");
            Finalization = final;
        }
    }

    private SalesInvoiceAmounts ConvertAtOfficialRate(SalesInvoiceRateSnapshot? rate)
    {
        var conversion = rate is null ? null : new SalesInvoiceFunctionalConversion(
            ExchangeRate.Of(Header.CurrencyId, rate.FunctionalCurrencyId, rate.Rate), rate.FunctionalScale);
        var input = lines.Select(line => new SalesInvoiceLineInput(line.Id, line.LineNo, line.Input.UnitId,
            line.Input.Quantity, line.Input.Commercial, line.Input.Tax)).ToArray();
        var converted = SalesInvoiceCalculator.Calculate(input, Header.CurrencyId, Header.AmountScale, conversion);
        if (!converted.IsComputed || converted.Totals != Totals || converted.Lines.Count != lines.Count
            || converted.Lines.Any(item => lines.Single(line => line.Id == item.LineId).Amounts != item.Amounts))
            throw new SalesRuleException(SalesRuleCodes.InvoiceAmountOutOfRange, "Kur bağlama dondurulmuş fatura tutarlarını korumadı.");
        return converted;
    }

    private static string Actor(string actorId) => RequireText(actorId, MaximumActorLength, nameof(actorId));
    private static string? Reason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null
        : RequireText(reason, SalesInvoice.MaximumReasonLength, nameof(reason));
    private void RequireOtherPreparer(string actor)
    {
        if (string.Equals(actor, CreatedBy, StringComparison.Ordinal) || string.Equals(actor, Decision.SubmittedBy, StringComparison.Ordinal))
            throw new SalesRuleException(SalesRuleCodes.InvoiceSelfApproval, "Hazırlayan veya onaya gönderen kendi faturasını onaylayamaz.");
    }
    private void RequireLifecycleVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new SalesVersionConflictException(expectedVersion, Version);
    }
    private void RequireLifecycleState(SalesSubcontractServiceInvoiceState state)
    {
        if (State != state) throw new SalesRuleException(SalesRuleCodes.StateConflict, "Fatura durumu bu işleme uygun değil.");
    }
    private void AdvanceLifecycle() => Version = checked(Version + 1);
}
