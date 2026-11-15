using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>Where a weighing correction stands (URT-D53). A decided correction is final.</summary>
public enum WeighingCorrectionStatus
{
    Proposed = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// What an approved weighing correction recorded on the order: the reversal of the wrong weighing (its full net mass)
/// and the declaration of the correct weighing, both new facts (R6: nothing is deleted).
/// </summary>
public sealed record WeighingCorrectionApplication(OutputReversal Reversal, OutputDeclaration CorrectedOutput);

/// <summary>
/// URT-D53 (SINAN_KARARLARI_2026-11-14_URETIM_EK4, 9.4 and 9.16): the correction of an over-weighed output. "Yanlış tartım
/// silinmez; ters kayıt ve doğru tartım girilir; ikinci kişi onaylar; iz kalır." The chosen path, in the domain:
/// <list type="bullet">
/// <item>One person proposes it: the output, the correct weighing below the output's net recorded mass and a reason
/// (<see cref="Propose"/>). Nothing on the order moves while it is proposed.</item>
/// <item>A second person approves it (<see cref="Approve"/>): the proposer can neither approve nor reject their own
/// correction (<see cref="ManufacturingRuleCodes.WeighingCorrectionSamePerson"/>), whatever permissions they hold; "the
/// existing approval is enough" was not chosen (URT-D53 (4)). Approval records the reversal of the wrong weighing's full
/// net mass (<see cref="ProductionOrder.ReverseOutput"/>) and the declaration of the correct weighing as a new output on
/// the same parent roll, class and plan output line (<see cref="ProductionOrder.DeclareOutput"/>). The wrong declaration
/// stays as it was recorded.</item>
/// <item>The trace stays: the correction keeps who proposed and who decided, when, why, the recorded and the correct kg,
/// and the ids of the reversal and of the corrected output it caused.</item>
/// </list>
/// Only an over-weighed output is corrected (<see cref="ManufacturingRuleCodes.WeighingCorrectionNotLower"/>): the
/// decision is about an output weighed too heavy, and a correction upwards is not decided, so it is refused rather than
/// applied. A declared process loss has no weighing (<see cref="ManufacturingRuleCodes.WeighingCorrectionNotWeighed"/>).
/// A closed order is refused with <see cref="ManufacturingRuleCodes.OrderClosed"/> as <see cref="ProductionOrder.ReverseOutput"/>
/// refuses it, at the proposal and again at the approval (URT-D81, SINAN_KARARLARI_2026-11-15_DALGA2_EK10: a weighing on
/// a closed order is corrected only after the order is reopened by the reopen flow, URT-D32 with its URT-D77
/// permission; a direct correction on a closed order is refused). Nothing here reopens or changes a closed order; the
/// reopen is its own command. The stock
/// side of an approval (the wrong label's reversal movement and the correct label's receipt) is Inventory's and is not
/// performed here; the command that persists a correction stages both with the order's facts in one transaction. The
/// permission names of the proposal and the approval are that command's, registered without any grant (URT-D53 (3)).
/// </summary>
public sealed class WeighingCorrection
{
    private WeighingCorrection(
        Guid correctionId, Guid companyId, Guid orderId, OutputDeclaration output, Kilograms recordedMass,
        Kilograms correctMass, string reason, Guid proposedByUserId, DateTimeOffset proposedAtUtc)
    {
        CorrectionId = correctionId;
        CompanyId = companyId;
        OrderId = orderId;
        OutputId = output.OutputId;
        ParentRollId = output.ParentRollId;
        OutputClass = output.Class;
        OutputLine = output.OutputLine!;
        RecordedMass = recordedMass;
        CorrectMass = correctMass;
        Reason = reason;
        ProposedByUserId = proposedByUserId;
        ProposedAtUtc = proposedAtUtc;
        Status = WeighingCorrectionStatus.Proposed;
    }

    public Guid CorrectionId { get; }

    public Guid CompanyId { get; }

    public Guid OrderId { get; }

    /// <summary>The output declaration weighed wrong.</summary>
    public Guid OutputId { get; }

    public Guid ParentRollId { get; }

    public OutputClass OutputClass { get; }

    public PlanOutputLineReference OutputLine { get; }

    /// <summary>The output's net mass (declared minus its reversals) when the correction was proposed.</summary>
    public Kilograms RecordedMass { get; }

    /// <summary>The correct weighing, above zero and below <see cref="RecordedMass"/>.</summary>
    public Kilograms CorrectMass { get; }

    public string Reason { get; }

    public Guid ProposedByUserId { get; }

    public DateTimeOffset ProposedAtUtc { get; }

    public WeighingCorrectionStatus Status { get; private set; }

    /// <summary>The second person who approved or rejected it; null while proposed.</summary>
    public Guid? DecidedByUserId { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>The reversal the approval recorded on the order; null unless approved.</summary>
    public Guid? ReversalId { get; private set; }

    /// <summary>The output declaration of the correct weighing; null unless approved.</summary>
    public Guid? CorrectedOutputId { get; private set; }

    /// <summary>
    /// Proposes the correction of <paramref name="outputId"/> to <paramref name="correctMass"/> on an open order, at the
    /// order version the proposer saw. Refused on a closed order, for an output not declared here, without a plan output
    /// line (as its reversal would be), for a declared process loss, and when the correct weighing is not below the net
    /// recorded mass. The order is not changed.
    /// </summary>
    public static WeighingCorrection Propose(
        ProductionOrder order, long expectedOrderVersion, Guid correctionId, Guid outputId, Kilograms correctMass,
        string reason, Guid proposedByUserId, DateTimeOffset proposedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        RequireVersion(order, expectedOrderVersion);
        RequireOpen(order);
        RequireId(correctionId, nameof(correctionId));
        RequireId(outputId, nameof(outputId));
        RequireId(proposedByUserId, nameof(proposedByUserId));
        if (correctMass.Value == 0m)
        {
            throw new ArgumentException("Doğru tartım sıfırdan büyük olmalıdır.", nameof(correctMass));
        }

        var text = RequireReason(reason);
        var output = DeclarationOf(order, outputId);
        if (output.Class == OutputClass.ProcessLoss)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WeighingCorrectionNotWeighed, "Proses kaybı tartılmaz; düzeltilecek tartım yok.");
        }

        if (output.OutputLine is null)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputLineMissing,
                "Plan çıktı satırına bağlı olmayan eski beyan düzeltilemez.");
        }

        var recorded = order.NetMassOf(outputId);
        if (correctMass.Value >= recorded.Value)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WeighingCorrectionNotLower,
                "Doğru tartım, çıktının kayıtlı net kütlesinden küçük olmalıdır.");
        }

        return new WeighingCorrection(correctionId, order.CompanyId, order.Id, output, recorded, correctMass, text,
            proposedByUserId, proposedAt.ToUniversalTime());
    }

    /// <summary>
    /// The second person's approval: records on <paramref name="order"/> the reversal <paramref name="reversalId"/> of the
    /// wrong weighing's full net mass and the declaration <paramref name="correctedOutputId"/> of the correct weighing,
    /// in that order, at the order version the approver saw. Every refusal is checked before the order is touched, so a
    /// refused approval changes neither the order nor the correction.
    /// </summary>
    public WeighingCorrectionApplication Approve(
        ProductionOrder order, long expectedOrderVersion, Guid approverUserId, Guid reversalId, Guid correctedOutputId,
        DateTimeOffset approvedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        RequireDecidable(approverUserId);
        RequireId(reversalId, nameof(reversalId));
        RequireId(correctedOutputId, nameof(correctedOutputId));
        RequireSameOrder(order);
        RequireVersion(order, expectedOrderVersion);
        RequireOpen(order);
        if (order.NetMassOf(OutputId).Value != RecordedMass.Value)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WeighingCorrectionOutputChanged,
                "Çıktının net kütlesi düzeltme önerildikten sonra değişti.");
        }

        if (order.OutputReversals.Any(entry => entry.ReversalId == reversalId))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputReversalAlreadyRecorded, "Bu çıktı düzeltmesi zaten kaydedildi.");
        }

        if (correctedOutputId == OutputId || order.Outputs.Any(output => output.OutputId == correctedOutputId))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputAlreadyDeclared, "Bu çıktı zaten beyan edildi.");
        }

        var at = approvedAt.ToUniversalTime();
        var reversal = order.ReverseOutput(order.Version, reversalId, OutputId, RecordedMass, Reason, at);
        var corrected = order.DeclareOutput(
            order.Version, correctedOutputId, ParentRollId, OutputClass, CorrectMass, at, OutputLine);
        Status = WeighingCorrectionStatus.Approved;
        DecidedByUserId = approverUserId;
        DecidedAtUtc = at;
        ReversalId = reversalId;
        CorrectedOutputId = correctedOutputId;
        return new WeighingCorrectionApplication(reversal, corrected);
    }

    /// <summary>
    /// The second person's rejection: the correction is final and the order is not touched. The proposer cannot reject
    /// their own correction; withdrawing one is not decided and is not offered here.
    /// </summary>
    public void Reject(Guid rejecterUserId, DateTimeOffset rejectedAt)
    {
        RequireDecidable(rejecterUserId);
        Status = WeighingCorrectionStatus.Rejected;
        DecidedByUserId = rejecterUserId;
        DecidedAtUtc = rejectedAt.ToUniversalTime();
    }

    private void RequireDecidable(Guid deciderUserId)
    {
        if (Status != WeighingCorrectionStatus.Proposed)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WeighingCorrectionNotProposed, "Tartım düzeltmesi zaten karara bağlandı.");
        }

        RequireId(deciderUserId, nameof(deciderUserId));
        if (deciderUserId == ProposedByUserId)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WeighingCorrectionSamePerson,
                "Tartım düzeltmesini öneren kişi onaylayamaz veya reddedemez; ikinci kişi karar verir.");
        }
    }

    private void RequireSameOrder(ProductionOrder order)
    {
        if (order.Id != OrderId || order.CompanyId != CompanyId)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WeighingCorrectionOutputChanged, "Tartım düzeltmesi bu emre ait değil.");
        }
    }

    private static OutputDeclaration DeclarationOf(ProductionOrder order, Guid outputId) =>
        order.Outputs.SingleOrDefault(output => output.OutputId == outputId)
        ?? throw new ManufacturingRuleException(
            ManufacturingRuleCodes.OutputNotDeclared, "Düzeltilen çıktı bu emirde beyan edilmedi.");

    private static void RequireVersion(ProductionOrder order, long expectedOrderVersion)
    {
        if (expectedOrderVersion != order.Version)
        {
            throw new ManufacturingVersionConflictException(expectedOrderVersion, order.Version);
        }
    }

    private static void RequireOpen(ProductionOrder order)
    {
        if (order.Status != OrderStatus.Open)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderClosed, "Kapanmış emirde düzeltme yapılamaz.");
        }
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Kimlik boş olamaz.", parameterName);
        }
    }

    private static string RequireReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > OutputReversal.ReasonMaximumLength || text.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Düzeltme gerekçesi 1–{OutputReversal.ReasonMaximumLength} karakter olmalı ve kontrol karakteri taşımamalıdır.",
                nameof(reason));
        }

        return text;
    }
}
