using GoymenERP.Api.Ai.Email;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

// Source-only C7 port contract tests; no account, provider or durable delivery acceptance.
public sealed class AiApprovedEmailServiceTests
{
    [Fact]
    public async Task Unconfigured_owner_never_reads_contacts_or_enqueues()
    {
        var h = new Harness(); h.Owner.IsAvailable = false;
        Assert.Equal(AiEmailStateV1.Unavailable, (await h.Service.ExecuteApprovedAsync(h.Approval())).State);
        Assert.Equal(0, h.Contacts.Reads); Assert.Equal(0, h.Owner.Enqueues);
    }

    [Fact]
    public async Task Changed_content_cannot_reuse_human_digest()
    {
        var h = new Harness(); var approved = h.Approval();
        Assert.Equal(AiEmailStateV1.Refused, (await h.Service.ExecuteApprovedAsync(
            approved with { Content = new("different", "body") })).State);
        Assert.Equal(0, h.Owner.Enqueues);
    }

    [Fact]
    public async Task Changed_registered_address_invalidates_the_frozen_effect()
    {
        var h = new Harness(); h.Contacts.Address = "changed@example.invalid";
        Assert.Equal(AiEmailStateV1.Refused, (await h.Service.ExecuteApprovedAsync(h.Approval())).State);
        Assert.Equal(0, h.Owner.Enqueues);
    }

    [Fact]
    public async Task Foreign_company_contact_and_authority_loss_after_read_are_refused()
    {
        var h = new Harness(); h.Contacts.Company = Guid.NewGuid();
        Assert.Equal(AiEmailStateV1.Refused, (await h.Service.ExecuteApprovedAsync(h.Approval())).State);
        h.Contacts.Company = h.Scope.CompanyId; h.Contacts.AfterRead = () => h.Authority.Current = false;
        Assert.Equal(AiEmailStateV1.Refused, (await h.Service.ExecuteApprovedAsync(h.Approval())).State);
        Assert.Equal(0, h.Owner.Enqueues);
    }

    [Fact]
    public async Task Durable_unknown_is_only_looked_up_and_never_enqueued_again()
    {
        var h = new Harness(); var approval = h.Approval();
        h.Owner.Receipt = new(approval.OperationId, approval.Scope.CompanyId, approval.Digest, AiEmailStateV1.Unknown);
        Assert.Equal(AiEmailStateV1.Unknown, (await h.Service.ExecuteApprovedAsync(approval)).State);
        Assert.Equal(AiEmailStateV1.Unknown, (await h.Service.ReconcileAsync(approval)).State);
        h.Owner.Receipt = null;
        Assert.Equal(AiEmailStateV1.Unknown, (await h.Service.ReconcileAsync(approval)).State);
        Assert.Equal(0, h.Owner.Enqueues);
    }

    [Fact]
    public async Task Foreign_receipt_is_not_released_and_queued_does_not_claim_delivery()
    {
        var h = new Harness(); var approval = h.Approval();
        Assert.Equal(AiEmailStateV1.Queued, (await h.Service.ExecuteApprovedAsync(approval)).State);
        Assert.Equal(1, h.Owner.Enqueues);
        h.Owner.Receipt = h.Owner.Receipt! with { CompanyId = Guid.NewGuid() };
        Assert.Equal(AiEmailStateV1.Refused, (await h.Service.ExecuteApprovedAsync(approval)).State);
        Assert.Equal(1, h.Owner.Enqueues);
    }

    private sealed class Harness
    {
        public AiEmailScopeV1 Scope { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "synthetic-session");
        public Authority Authority { get; } = new();
        public Owner Owner { get; } = new();
        public Contacts Contacts { get; }
        public AiApprovedEmailServiceV1 Service { get; }
        public Harness()
        {
            Contacts = new() { Company = Scope.CompanyId };
            Service = new(Owner, Authority, new(Contacts, Authority));
        }
        public AiEmailApprovalV1 Approval()
        {
            AiEmailRecipientV1[] targets = [new(Contacts.Party, Contacts.Account, "1", "registered@example.invalid")];
            var content = new AiEmailContentV1("SENTETIK", "body");
            return new(Guid.NewGuid(), Scope, targets, content, AiApprovedEmailServiceV1.Digest(Scope, targets, content));
        }
    }
    private sealed class Authority : IAiEmailAuthorityV1
    {
        public bool Current { get; set; } = true;
        public Task<bool> IsCurrentAsync(AiEmailScopeV1 scope, CancellationToken cancellationToken) => Task.FromResult(Current);
    }
    private sealed class Owner : IAiApprovedEmailOwnerV1
    {
        public bool IsAvailable { get; set; } = true;
        public AiEmailReceiptV1? Receipt { get; set; }
        public int Enqueues { get; private set; }
        public Task<AiEmailReceiptV1?> FindAsync(AiEmailScopeV1 scope, Guid operationId, CancellationToken cancellationToken) => Task.FromResult(Receipt);
        public Task<AiEmailReceiptV1> EnqueueApprovedOnceAsync(AiEmailApprovalV1 approval, CancellationToken cancellationToken)
        {
            Enqueues++;
            var receipt = new AiEmailReceiptV1(approval.OperationId, approval.Scope.CompanyId, approval.Digest, AiEmailStateV1.Queued);
            Receipt = receipt;
            return Task.FromResult(receipt);
        }
    }
    private sealed class Contacts : ICounterpartyContactInformationService
    {
        public Guid Company { get; set; }
        public Guid Party { get; } = Guid.NewGuid();
        public Guid Account { get; } = Guid.NewGuid();
        public string Address { get; set; } = "registered@example.invalid";
        public int Reads { get; private set; }
        public Action? AfterRead { get; set; }
        public Task<CounterpartyContactInformationView?> GetContactInformationAsync(Guid partyId, CancellationToken cancellationToken)
        {
            Reads++; AfterRead?.Invoke();
            return Task.FromResult<CounterpartyContactInformationView?>(new(Party, Account, Company, "1", true,
                new(null, null, Address, null, [], [])));
        }
        public Task<CounterpartyCommandExecution> UpdateContactInformationAsync(Guid partyId, UpdateCounterpartyContactInformation command,
            CounterpartyCommandMetadata metadata, CancellationToken cancellationToken) => throw new InvalidOperationException("Read-only resolver");
    }
}
