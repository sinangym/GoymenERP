using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>What a Finance authorized detail read answered for one owner id: the outcome and, when found, the record's identity.</summary>
public readonly record struct FinanceOwnerReadV1(ReadOutcome Outcome, Guid? Id, Guid? CompanyId)
{
    public static FinanceOwnerReadV1 Of(ReadOutcome outcome) => new(outcome, null, null);
}

/// <summary>
/// B-26 (FNS-D35/D36): adapts a Finance record's authorized detail read to the Document Vault owner-link contract. The vault
/// never reads Finance persistence. The read runs with the exact context company, actor and correlation; it is re-checked
/// after the await. Found is readable only for the very record of the context company; unknown and other-company ids answer
/// NotFound, a missing read authority Denied (both non-disclosing to the client); a provider failure or an inconsistent
/// answer is Unavailable. These records accept an EXISTING document (a receipt, a bank slip, a scanned instrument): the
/// owner is a separate record, unlike the official sales invoice whose PDF belongs to that invoice alone (FNS-D36/D38).
/// </summary>
public abstract class FinanceRecordDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation) : IDocumentOwnerLinkPortV1
{
    public abstract string Kind { get; }

    public bool AcceptsExistingDocumentLinks => true;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerId == Guid.Empty || !IsCurrent())
        {
            return DocumentOwnerAccessV1.Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            var read = await ReadAsync(
                new FinanceContext(companyId, actorId, correlation.CorrelationId, correlation.CausationId), ownerId,
                cancellationToken);
            if (!IsCurrent() || companyContext.CompanyId != companyId
                || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
            {
                return DocumentOwnerAccessV1.Denied;
            }

            return read.Outcome switch
            {
                ReadOutcome.Found when read.Id == ownerId && read.CompanyId == companyId => DocumentOwnerAccessV1.Readable,
                ReadOutcome.NotFound => DocumentOwnerAccessV1.NotFound,
                ReadOutcome.Denied => DocumentOwnerAccessV1.Denied,
                _ => DocumentOwnerAccessV1.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return DocumentOwnerAccessV1.Unavailable;
        }
    }

    /// <summary>The Finance authorized detail read of one record, with the context it is given.</summary>
    protected abstract Task<FinanceOwnerReadV1> ReadAsync(FinanceContext context, Guid ownerId, CancellationToken cancellationToken);

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}

/// <summary>B-26: <c>finance.manual-receipt</c> through <see cref="IManualReceiptService.DetailAuthorizedAsync"/>.</summary>
public sealed class FinanceManualReceiptDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IManualReceiptService receipts) : FinanceRecordDocumentOwnerLinkPort(companyContext, currentUser, correlation)
{
    public override string Kind => DocumentOwnerLinkKindsV1.FinanceManualReceipt;

    protected override async Task<FinanceOwnerReadV1> ReadAsync(FinanceContext context, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var result = await receipts.DetailAuthorizedAsync(context, new SourceDetailRequest(ownerId), cancellationToken);
        return result.Resource?.Source is { } source
            ? new FinanceOwnerReadV1(result.Outcome, source.Id, source.CompanyId)
            : FinanceOwnerReadV1.Of(result.Outcome);
    }
}

/// <summary>B-26: <c>finance.manual-bank-payment</c> through <see cref="IManualBankPaymentService.DetailAuthorizedAsync"/>.</summary>
public sealed class FinanceManualBankPaymentDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IManualBankPaymentService payments) : FinanceRecordDocumentOwnerLinkPort(companyContext, currentUser, correlation)
{
    public override string Kind => DocumentOwnerLinkKindsV1.FinanceManualBankPayment;

    protected override async Task<FinanceOwnerReadV1> ReadAsync(FinanceContext context, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var result = await payments.DetailAuthorizedAsync(context, ownerId, cancellationToken);
        return result.Resource is { } payment
            ? new FinanceOwnerReadV1(result.Outcome, payment.Id, payment.CompanyId)
            : FinanceOwnerReadV1.Of(result.Outcome);
    }
}

/// <summary>B-26: <c>finance.customer-instrument</c> through <see cref="ICustomerInstrumentService.DetailAuthorizedAsync"/>.</summary>
public sealed class FinanceCustomerInstrumentDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    ICustomerInstrumentService instruments) : FinanceRecordDocumentOwnerLinkPort(companyContext, currentUser, correlation)
{
    public override string Kind => DocumentOwnerLinkKindsV1.FinanceCustomerInstrument;

    protected override async Task<FinanceOwnerReadV1> ReadAsync(FinanceContext context, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var result = await instruments.DetailAuthorizedAsync(context, ownerId, cancellationToken);
        return result.Resource?.Instrument is { } instrument
            ? new FinanceOwnerReadV1(result.Outcome, instrument.Id, instrument.CompanyId)
            : FinanceOwnerReadV1.Of(result.Outcome);
    }
}

/// <summary>
/// WP12 / FNS-D52: <c>finance.bank-statement-import</c> through
/// <see cref="IFinanceBankStatementImportServiceV1.CheckDocumentOwnerAsync"/>. The only owner that accepts the bank
/// statement kinds (XLSX, MT940, CSV); a statement file belongs to its import alone, so no existing document is linked.
/// </summary>
public sealed class FinanceBankStatementImportDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IServiceProvider services) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.FinanceBankStatementImport;

    public IReadOnlyCollection<GoymenERP.Modules.Documents.Domain.DocumentFileKind> AcceptedFileKinds =>
        GoymenERP.Modules.Documents.Domain.DocumentContentPolicy.BankStatementImportKinds;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerId == Guid.Empty || !currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return DocumentOwnerAccessV1.Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            // Resolve only on use: the import service's statement read adapter itself consumes IDocumentService, so
            // capturing it in this constructor would make the vault's owner-port collection cyclic (a resolve deadlock).
            var imports = services.GetRequiredService<IFinanceBankStatementImportServiceV1>();
            var outcome = await imports.CheckDocumentOwnerAsync(
                new FinanceContext(companyId, actorId, correlation.CorrelationId, correlation.CausationId), ownerId,
                cancellationToken);
            if (companyContext.CompanyId != companyId || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
            {
                return DocumentOwnerAccessV1.Denied;
            }

            return outcome switch
            {
                ReadOutcome.Found => DocumentOwnerAccessV1.Readable,
                ReadOutcome.NotFound => DocumentOwnerAccessV1.NotFound,
                ReadOutcome.Denied => DocumentOwnerAccessV1.Denied,
                _ => DocumentOwnerAccessV1.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return DocumentOwnerAccessV1.Unavailable;
        }
    }
}
