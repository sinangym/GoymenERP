using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Application.Inbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-S13 (B-04): the host's admission authority for Accounting's invoice journal consumer is fail-closed. With the host switch
/// off (the default) it answers Unavailable for every caller, exactly like the module default it replaces; with the switch on it
/// allows ONLY the consumer's own caller identity for a sales invoice, and its audit permission field names the consumer.
/// </summary>
public sealed class AccountingInvoiceJournalHostTests
{
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-0000000013e1");
    private static readonly Guid SourceDocumentId = Guid.Parse("20000000-0000-0000-0000-0000000013e2");

    [Fact]
    public async Task With_the_switch_off_the_inbox_caller_is_unavailable()
    {
        var authority = Authority(enabled: null);

        var answer = await authority.CheckAsync(CompanyA, AccountingSalesInvoiceFinalizedInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId, CancellationToken.None);

        Assert.Equal(AccountingSourceDocumentAuthorityAnswerV1.Unavailable, answer);
    }

    [Fact]
    public async Task With_the_switch_on_only_the_inbox_caller_is_allowed_with_the_consumer_as_audit_permission()
    {
        var authority = Authority(enabled: "true");

        var answer = await authority.CheckAsync(CompanyA, AccountingSalesInvoiceFinalizedInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId, CancellationToken.None);

        Assert.Equal(AccountingSourceDocumentAuthorityDecisionV1.Allowed, answer.Decision);
        Assert.Equal(AccountingSalesInvoiceFinalizedInboxV1.AuditPermissionCode, answer.PermissionCode);
        Assert.Empty(answer.RoleCodes);
    }

    [Fact]
    public async Task With_the_switch_on_another_caller_kind_company_or_document_stays_unavailable()
    {
        var authority = Authority(enabled: "true");

        var otherActor = await authority.CheckAsync(CompanyA, "synthetic-user",
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId, CancellationToken.None);
        var otherKind = await authority.CheckAsync(CompanyA, AccountingSalesInvoiceFinalizedInboxV1.ActorId,
            "purchase-invoice", SourceDocumentId, CancellationToken.None);
        var noCompany = await authority.CheckAsync(Guid.Empty, AccountingSalesInvoiceFinalizedInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId, CancellationToken.None);
        var noDocument = await authority.CheckAsync(CompanyA, AccountingSalesInvoiceFinalizedInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.SalesInvoice, Guid.Empty, CancellationToken.None);

        Assert.All([otherActor, otherKind, noCompany, noDocument],
            answer => Assert.Equal(AccountingSourceDocumentAuthorityAnswerV1.Unavailable, answer));
    }

    [Fact]
    public void The_host_replaces_the_module_default_authority_and_registers_the_consumer()
    {
        var services = new ServiceCollection();
        services.TryAddScoped<IAccountingSourceDocumentAdmissionAuthorityV1, SyntheticDefaultAuthority>();
        services.TryAddScoped<IAccountingSourceInvoiceCancellationAuthorityV1, UnavailableAccountingSourceInvoiceCancellationAuthorityV1>();

        services.AddAccountingInvoiceJournal(new ConfigurationBuilder().Build());

        var authority = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IAccountingSourceDocumentAdmissionAuthorityV1));
        Assert.Equal(typeof(AccountingInvoiceJournalInboxAuthorityV1), authority.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, authority.Lifetime);
        var consumer = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(AccountingSalesInvoiceFinalizedInboxConsumer));
        Assert.Equal(ServiceLifetime.Scoped, consumer.Lifetime);
        var cancellation = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IAccountingSourceInvoiceCancellationAuthorityV1));
        Assert.Equal(typeof(AccountingInvoiceJournalCancellationAuthorityV1), cancellation.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, cancellation.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services,
            descriptor => descriptor.ServiceType == typeof(AccountingSalesInvoiceCancelledInboxConsumerV1)).Lifetime);
    }

    [Fact]
    public async Task Purchase_inbox_identity_is_allowed_only_for_purchase_kind_and_only_when_the_existing_switch_is_enabled()
    {
        var allowed = await Authority("true").CheckAsync(CompanyA, AccountingPurchaseInvoiceFinalizedInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.PurchaseInvoice, SourceDocumentId, CancellationToken.None);
        Assert.Equal(AccountingSourceDocumentAuthorityDecisionV1.Allowed, allowed.Decision);
        Assert.Equal(AccountingPurchaseInvoiceFinalizedInboxV1.AuditPermissionCode, allowed.PermissionCode);
        var off = await Authority(null).CheckAsync(CompanyA, AccountingPurchaseInvoiceFinalizedInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.PurchaseInvoice, SourceDocumentId, CancellationToken.None);
        var swapped = await Authority("true").CheckAsync(CompanyA, AccountingPurchaseInvoiceFinalizedInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId, CancellationToken.None);
        Assert.Equal(AccountingSourceDocumentAuthorityAnswerV1.Unavailable, off);
        Assert.Equal(AccountingSourceDocumentAuthorityAnswerV1.Unavailable, swapped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    public async Task Cancellation_identity_is_separate_from_finalization_and_preserves_the_existing_host_switch(string? enabled)
    {
        var authority = new AccountingInvoiceJournalCancellationAuthorityV1(Options(enabled));
        var answer = await authority.CheckAsync(CompanyA, AccountingSalesInvoiceCancelledInboxV1.ActorId,
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId, CancellationToken.None);
        Assert.Equal(enabled == "true" ? AccountingSourceDocumentAuthorityDecisionV1.Allowed
            : AccountingSourceDocumentAuthorityAnswerV1.Unavailable.Decision, answer.Decision);
        if (enabled == "true")
        {
            Assert.Equal(AccountingSalesInvoiceCancelledInboxV1.ActorId, answer.PermissionCode);
            Assert.Empty(answer.RoleCodes);
        }
        foreach (var invalid in new[]
        {
            (CompanyA, AccountingSalesInvoiceFinalizedInboxV1.ActorId, AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId),
            (CompanyA, AccountingSalesInvoiceCancelledInboxV1.ActorId, AccountingSourceDocumentKindsV1.PurchaseInvoice, SourceDocumentId),
            (Guid.Empty, AccountingSalesInvoiceCancelledInboxV1.ActorId, AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId),
            (CompanyA, AccountingSalesInvoiceCancelledInboxV1.ActorId, AccountingSourceDocumentKindsV1.SalesInvoice, Guid.Empty),
        }) Assert.Equal(AccountingSourceDocumentAuthorityAnswerV1.Unavailable,
            await authority.CheckAsync(invalid.Item1, invalid.Item2, invalid.Item3, invalid.Item4, CancellationToken.None));
        Assert.Equal(AccountingSourceDocumentAuthorityAnswerV1.Unavailable,
            await Authority(enabled).CheckAsync(CompanyA, AccountingSalesInvoiceCancelledInboxV1.ActorId,
                AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId, CancellationToken.None));
    }

    private static AccountingInvoiceJournalInboxAuthorityV1 Authority(string? enabled) => new(Options(enabled));

    private static IOptionsMonitor<AccountingInvoiceJournalHostOptions> Options(string? enabled)
    {
        var values = new Dictionary<string, string?>();
        if (enabled is not null)
        {
            values[AccountingInvoiceJournalHostOptions.SectionName + ":" + nameof(AccountingInvoiceJournalHostOptions.InboxAdmissionEnabled)] = enabled;
        }

        var services = new ServiceCollection();
        services.AddAccountingInvoiceJournal(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<AccountingInvoiceJournalHostOptions>>();
    }

    private sealed class SyntheticDefaultAuthority : IAccountingSourceDocumentAdmissionAuthorityV1
    {
        public Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(
            Guid companyId,
            string actorId,
            string documentKind,
            Guid sourceDocumentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(AccountingSourceDocumentAuthorityAnswerV1.Unavailable);
    }
}
