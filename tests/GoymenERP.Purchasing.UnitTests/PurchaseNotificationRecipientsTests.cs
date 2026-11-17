using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseNotificationRecipientsTests
{
    private readonly Guid companyId = Guid.NewGuid();
    private readonly Guid orderId = Guid.NewGuid();
    private readonly Guid lineId = Guid.NewGuid();
    private readonly Guid preparer = Guid.NewGuid();
    private readonly Guid approver = Guid.NewGuid();

    [Fact]
    public async Task Unresolved_or_changed_company_is_denied_before_any_database_connection()
    {
        var company = new CompanyContext();
        await using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=SENTETIK;Integrated Security=True;Connect Timeout=1").Options, company);
        var port = new PurchaseNotificationRecipients(database, company);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadAsync(companyId, orderId, lineId));
        company.Set(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadAsync(companyId, orderId, lineId));
        company.Set(companyId);
        var missing = await port.ReadAsync(companyId, Guid.Empty, lineId);
        Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.NotFound, missing.Outcome);
        Assert.Empty(missing.UserIds);
    }

    [Fact]
    public void Reopened_supply_answers_only_the_recorded_preparer_and_approver()
    {
        var answer = Compose(Facts());
        Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.Found, answer.Outcome);
        Assert.Equal(new[] { preparer, approver }.Order(), answer.UserIds);
        Assert.Equal(7L, answer.ObservedOrderVersion);
        Assert.Equal((companyId, orderId, lineId), (answer.CompanyId, answer.PurchaseOrderId, answer.GeneralLineId));
    }

    [Fact]
    public void Legacy_missing_approver_does_not_fabricate_the_event_actor_or_complete_a_partial_delivery()
    {
        var answer = Compose(Facts() with { ApprovedBy = null, ApprovedAtUtc = null });
        Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.Unassigned, answer.Outcome);
        Assert.Empty(answer.UserIds);
    }

    [Fact]
    public void Opaque_actor_text_and_empty_identity_are_unavailable_not_new_users()
    {
        foreach (var actor in new[] { "SENTETIK-non-user-actor", Guid.Empty.ToString("D") })
        {
            var answer = Compose(Facts() with { ApprovedBy = actor });
            Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.Unavailable, answer.Outcome);
            Assert.Empty(answer.UserIds);
        }
    }

    [Fact]
    public void Foreign_or_unversioned_or_draft_source_cannot_become_a_recipient_set()
    {
        foreach (var facts in new[] { Facts() with { CompanyId = Guid.NewGuid() }, Facts() with { OrderId = Guid.NewGuid() },
                     Facts() with { GeneralLineId = Guid.NewGuid() }, Facts() with { Version = 0 },
                     Facts() with { Status = (int)PurchaseOrderStatus.Draft } })
        {
            var answer = Compose(facts);
            Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.Unavailable, answer.Outcome);
            Assert.Null(answer.ObservedOrderVersion);
            Assert.Empty(answer.UserIds);
        }
    }

    private PurchaseNotificationRecipientsV1 Compose(PurchaseNotificationRecipientFacts facts) =>
        PurchaseNotificationRecipients.Compose(companyId, orderId, lineId, facts);
    private PurchaseNotificationRecipientFacts Facts() => new(companyId, orderId, lineId, 7,
        (int)PurchaseOrderStatus.Approved, preparer.ToString("D"), approver.ToString("D"), DateTimeOffset.UtcNow);
}
