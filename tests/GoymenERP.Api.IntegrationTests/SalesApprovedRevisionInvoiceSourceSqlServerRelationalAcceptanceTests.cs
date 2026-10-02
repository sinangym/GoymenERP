using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Accepted_commercial_revision_is_saved_closed_reopened_and_read_as_the_same_invoice_source()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesOrderDraft draft;
        Guid lineId;
        await using (var first = run.Store(CompanyA))
        {
            draft = (await first.Store.StoreDraftAsync(run.Envelope("invoice-create"), Hash("invoice-create"),
                documentNo => Draft(owners, documentNo, T0, customerAcceptanceRequired: true))).Draft;
            lineId = draft.Lines[0].Id;
            var priced = (await first.Store.ReviseDraftCommercialAsync(run.Envelope("invoice-price"), Hash("invoice-price"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id,
                (stored, now) => stored.ReviseCommercial(stored.Version,
                    [new LineCommercialInput(lineId, new CommercialDraftFields("12.50", KgUnit, "5"))], run.Actor, now),
                Revised, _ => Task.CompletedTask))!;
            var submitted = (await first.Store.UpdateDraftStateAsync(run.Envelope("invoice-submit"), Hash("invoice-submit"),
                SalesDraftCommands.SubmitV1, draft.Id, (stored, now) => stored.Submit(priced.Draft.Version, run.Actor, now), Submitted))!;
            var approved = (await first.Store.UpdateDraftStateAsync(run.Envelope("invoice-approve"), Hash("invoice-approve"),
                SalesDraftCommands.ApproveV1, draft.Id,
                (stored, now) => stored.Approve(submitted.Draft.Version, "SENTETIK-approver", now), Approved))!;
            var accepted = (await first.Store.UpdateDraftStateAsync(run.Envelope("invoice-accept"), Hash("invoice-accept"),
                SalesDraftCommands.RecordCustomerAcceptanceV1, draft.Id,
                (stored, now) => stored.RecordCustomerAcceptance(approved.Draft.Version, run.Actor, now), CustomerAccepted))!;
            await first.Store.ReviseDraftCommercialAsync(run.Envelope("invoice-archive"), Hash("invoice-archive"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id,
                (stored, now) => stored.ReviseCommercial(accepted.Draft.Version,
                    [new LineCommercialInput(lineId, new CommercialDraftFields("13.00", KgUnit, "5"))], run.Actor, now),
                Revised, _ => Task.CompletedTask);
        }

        await using (var reopened = run.Store(CompanyA))
        {
            var authority = new RecordingAuthority(SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead);
            var service = run.Service(reopened, run.Actor, authority);
            var answer = await ((IApprovedSalesRevisionInvoiceSourceV1)service).ReadAsync(draft.Id, 0, [lineId]);

            Assert.Equal(ApprovedSalesRevisionInvoiceSourceOutcomeV1.Available, answer.Outcome);
            var source = Assert.IsType<ApprovedSalesRevisionInvoiceSourceV1>(answer.Source);
            Assert.Equal((true, 0, run.Actor),
                (source.CustomerAcceptance.Required, source.CustomerAcceptance.AcceptedRevision, source.CustomerAcceptance.AcceptedBy));
            Assert.Equal(("12.50", "5", "11.875"),
                (source.Lines[0].UnitPrice, source.Lines[0].DiscountPercent, source.Lines[0].NetUnitPrice));
            Assert.Equal([
                (SalesPermissionManifestV1.DraftsRead, CompanyA),
                (SalesPermissionManifestV1.OrdersPriceRead, CompanyA),
                (SalesPermissionManifestV1.DraftsRead, CompanyA),
                (SalesPermissionManifestV1.OrdersPriceRead, CompanyA),
            ], authority.Measured);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftRevisions WHERE DraftId = @id AND Revision = 0 "
            + "AND CustomerAcceptedRevision = 0 AND CustomerAcceptedBy = @actor AND CustomerAcceptedAtUtc IS NOT NULL",
            ("@id", draft.Id), ("@actor", run.Actor)));
    }
}
