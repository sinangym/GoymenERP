using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Subcontracts;

namespace GoymenERP.Modules.Purchasing.Application.Subcontracts;

/// <summary>
/// Composes fresh owner answers for an already validated subcontract draft. It creates no
/// stock, receipt, transfer, invoice, price, tax, or payment effect. In particular, the
/// commercial service quantity is not converted into a physical stock quantity.
/// </summary>
public sealed class PurchasingSubcontractAdmissionV1(
    IPurchasingSubcontractSubjectSourceV1 subjects,
    IPurchasingPartyAccountPortV1 partyAccounts)
{
    public async Task<PurchasingSubcontractAdmissionSnapshotV1> AdmitAsync(
        PurchasingSubcontractOrderV1 order,
        Guid lineId,
        SubcontractPhysicalContextV1 physicalContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(physicalContext);
        cancellationToken.ThrowIfCancellationRequested();

        if (lineId == Guid.Empty)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.InvalidRequest);
        }

        var line = order.Lines.SingleOrDefault(candidate => candidate.LineId == lineId)
            ?? throw Failure(PurchasingSubcontractAdmissionProblemV1.InvalidRequest);

        RequirePhysicalContext(order, line, physicalContext);

        var subjectReference = new PurchasingCommercialLineSubjectReferenceV1(
            PurchasingCommercialLineSubjectKindV1.Service,
            line.ServiceId,
            line.ExpectedServiceVersion);

        PurchasingPartyAccountAnswerV1 partyAnswer;
        try
        {
            partyAnswer = await partyAccounts.GetAuthorizedAsync(
                order.SupplierPartyId,
                order.CompanyAccountId,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PartyUnavailable);
        }

        RequirePartyAnswer(order, partyAnswer);

        PurchasingSubcontractSubjectAnswerV1 subjectAnswer;
        try
        {
            subjectAnswer = await subjects.GetAuthorizedAsync(
                order.CompanyId,
                subjectReference,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.SubjectUnavailable);
        }

        var subject = RequireSubjectAnswer(subjectReference, subjectAnswer);
        return new PurchasingSubcontractAdmissionSnapshotV1(
            order.OrderId,
            order.CompanyId,
            line.LineId,
            subject,
            order.SupplierPartyId,
            order.ExpectedSupplierPartyVersion,
            order.CompanyAccountId,
            order.ExpectedCompanyAccountVersion,
            physicalContext);
    }

    private static void RequirePhysicalContext(
        PurchasingSubcontractOrderV1 order,
        PurchasingSubcontractOrderLineV1 line,
        SubcontractPhysicalContextV1 context)
    {
        if (context.CompanyId != order.CompanyId
            || context.SourcePhysicalRequestId != line.SourcePhysicalRequestId
            || !Exact(context.SourcePhysicalRequestVersion, line.ExpectedSourcePhysicalRequestVersion)
            || context.OperatorPartyId == Guid.Empty
            || context.CustodianPartyId != context.OperatorPartyId)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PhysicalContextConflict);
        }
    }

    private static void RequirePartyAnswer(
        PurchasingSubcontractOrderV1 order,
        PurchasingPartyAccountAnswerV1 answer)
    {
        if (answer is null)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PartyUnavailable);
        }

        if (answer.Outcome == PurchasingPartyAccountOutcomeV1.Unavailable)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PartyUnavailable);
        }

        if (answer.Outcome == PurchasingPartyAccountOutcomeV1.Denied)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PartyDenied);
        }

        if (answer.Outcome == PurchasingPartyAccountOutcomeV1.NotFound)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PartyNotFound);
        }

        var account = answer.Account;
        if (answer.Outcome != PurchasingPartyAccountOutcomeV1.Found
            || account is null
            || account.PartyId != order.SupplierPartyId
            || account.CompanyAccountId != order.CompanyAccountId
            || account.CompanyId != order.CompanyId
            || !account.PartyIsActive
            || !account.AccountIsActive
            || !account.AccountHasSupplierRole)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PartyInvalid);
        }

        if (!Exact(account.PartyVersion, order.ExpectedSupplierPartyVersion)
            || !Exact(account.AccountVersion, order.ExpectedCompanyAccountVersion))
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.PartyVersionConflict);
        }
    }

    private static PurchasingCommercialLineSubjectSnapshotV1 RequireSubjectAnswer(
        PurchasingCommercialLineSubjectReferenceV1 expected,
        PurchasingSubcontractSubjectAnswerV1 answer)
    {
        if (answer is null)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.SubjectUnavailable);
        }

        if (answer.Outcome == PurchasingSubcontractSubjectOutcomeV1.Unavailable)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.SubjectUnavailable);
        }

        if (answer.Outcome == PurchasingSubcontractSubjectOutcomeV1.Denied)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.SubjectDenied);
        }

        if (answer.Outcome == PurchasingSubcontractSubjectOutcomeV1.NotFound)
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.SubjectNotFound);
        }

        var subject = answer.Subject;
        if (answer.Outcome != PurchasingSubcontractSubjectOutcomeV1.Found
            || subject is null
            || subject.Kind != PurchasingCommercialLineSubjectKindV1.Service
            || subject.Id != expected.Id
            || string.IsNullOrWhiteSpace(subject.Code)
            || string.IsNullOrWhiteSpace(subject.Name))
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.SubjectInvalid);
        }

        if (!Exact(subject.Version, expected.ExpectedVersion))
        {
            throw Failure(PurchasingSubcontractAdmissionProblemV1.SubjectVersionConflict);
        }

        return subject;
    }

    private static bool Exact(string actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual)
        && !string.IsNullOrWhiteSpace(expected)
        && string.Equals(actual, expected, StringComparison.Ordinal);

    private static PurchasingSubcontractAdmissionExceptionV1 Failure(
        PurchasingSubcontractAdmissionProblemV1 problem) => new(problem);
}

public sealed record PurchasingSubcontractAdmissionSnapshotV1(
    Guid OrderId,
    Guid CompanyId,
    Guid LineId,
    PurchasingCommercialLineSubjectSnapshotV1 Service,
    Guid SupplierPartyId,
    string SupplierPartyVersion,
    Guid CompanyAccountId,
    string CompanyAccountVersion,
    SubcontractPhysicalContextV1 PhysicalContext);

public enum PurchasingSubcontractAdmissionProblemV1
{
    InvalidRequest = 0,
    PartyUnavailable = 1,
    PartyDenied = 2,
    PartyNotFound = 3,
    PartyInvalid = 4,
    PartyVersionConflict = 5,
    SubjectUnavailable = 6,
    SubjectDenied = 7,
    SubjectNotFound = 8,
    SubjectInvalid = 9,
    SubjectVersionConflict = 10,
    PhysicalContextConflict = 11,
}

public sealed class PurchasingSubcontractAdmissionExceptionV1(
    PurchasingSubcontractAdmissionProblemV1 problem)
    : InvalidOperationException($"Subcontract admission refused: {problem}.")
{
    public PurchasingSubcontractAdmissionProblemV1 Problem { get; } = problem;
}
