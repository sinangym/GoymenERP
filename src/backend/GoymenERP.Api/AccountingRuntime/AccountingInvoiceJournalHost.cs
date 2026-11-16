using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Application.Inbox;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.AccountingRuntime;

/// <summary>
/// FIN-S13 (B-04): the host composition of Accounting's invoice journal consumer. It registers the consumer (scoped, not with the
/// dispatcher: Sales has no Work outbox source yet, and AddWorkInboxConsumer refuses a consumer without its source) and the
/// host-owned admission authority for the inbox caller. The authority is fail-closed: unless
/// <c>Accounting:InvoiceJournal:InboxAdmissionEnabled</c> is explicitly true it answers Unavailable for every caller, exactly like the
/// module default it replaces. Which permission measures a system admission is not decided (NEEDS-SINAN); turning the switch on is a
/// human decision, as is enabling the dispatcher (<c>Work:OutboxDispatcher:Enabled</c>). No permission, role or grant is added.
/// </summary>
internal static class AccountingInvoiceJournalHost
{
    public static IServiceCollection AddAccountingInvoiceJournal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<AccountingInvoiceJournalHostOptions>()
            .Bind(configuration.GetSection(AccountingInvoiceJournalHostOptions.SectionName));
        services.AddScoped<AccountingSalesInvoiceFinalizedInboxConsumer>();
        // Replaces the module's fail-closed default (registered with TryAdd); with the switch off it answers the same.
        services.Replace(ServiceDescriptor.Scoped<IAccountingSourceDocumentAdmissionAuthorityV1, AccountingInvoiceJournalInboxAuthorityV1>());
        return services;
    }
}

/// <summary>The host switch of the inbox caller's admission authority (off unless configured).</summary>
internal sealed class AccountingInvoiceJournalHostOptions
{
    public const string SectionName = "Accounting:InvoiceJournal";

    /// <summary>When false (the default) no caller is admitted: the port answers Unavailable and the consumer retries.</summary>
    public bool InboxAdmissionEnabled { get; set; }
}

/// <summary>
/// The admission authority of the invoice journal consumer. It allows ONLY the consumer's own caller identity, only for a sales
/// invoice, only for a resolved company and only while the host switch is on; every other caller stays Unavailable (no user
/// measurement is composed for the port yet). The audit permission field names the consumer, outside the grantable names.
/// </summary>
internal sealed class AccountingInvoiceJournalInboxAuthorityV1(IOptionsMonitor<AccountingInvoiceJournalHostOptions> options)
    : IAccountingSourceDocumentAdmissionAuthorityV1
{
    public Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(
        Guid companyId,
        string actorId,
        string documentKind,
        Guid sourceDocumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.CurrentValue.InboxAdmissionEnabled || companyId == Guid.Empty || sourceDocumentId == Guid.Empty
            || !string.Equals(actorId, AccountingSalesInvoiceFinalizedInboxV1.ActorId, StringComparison.Ordinal)
            || !string.Equals(documentKind, AccountingSourceDocumentKindsV1.SalesInvoice, StringComparison.Ordinal))
        {
            return Task.FromResult(AccountingSourceDocumentAuthorityAnswerV1.Unavailable);
        }

        return Task.FromResult(new AccountingSourceDocumentAuthorityAnswerV1(
            AccountingSourceDocumentAuthorityDecisionV1.Allowed,
            AccountingSalesInvoiceFinalizedInboxV1.AuditPermissionCode,
            []));
    }
}
