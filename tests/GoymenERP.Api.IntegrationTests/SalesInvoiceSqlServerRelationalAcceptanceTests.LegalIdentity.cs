using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Legal_identity_reader_uses_native_finalization_receipt_and_same_save_outbox_without_current_match_or_price_access()
    {
        await using var run = await Run.StartAsync();
        var ettn = Guid.NewGuid().ToString("D");
        var invoice = await FinalizedAsync(run, "legal-read", "SENTETIK-LEGAL-READ", ettn, "SENTETIK-checker", "SENTETIK-finalizer");
        await using var scope = run.Store(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var calls = 0;
        var answer = await new SalesFinalizedInvoiceIdentityReadPortV1(scope.Context, company).ReadAsync(CompanyA,
            [new(invoice.Id, invoice.Finalization!.FinalizedVersion)], _ => { calls++; return Task.CompletedTask; });
        Assert.True(answer.Available);
        var item = Assert.Single(answer.Items);
        Assert.Equal(invoice.Finalization.AdmissionId, item.AdmissionId);
        Assert.Equal(invoice.Header.PartyId, item.PartyId);
        Assert.Equal(invoice.Header.CompanyAccountId, item.CompanyAccountId);
        Assert.Equal("SENTETIK-LEGAL-READ", item.OfficialInvoiceNo);
        Assert.Equal(ettn, item.Ettn);
        Assert.NotEqual(Guid.Empty, item.FinalizedEventId);
        Assert.Equal(64, item.SourceVersion.Length);
        Assert.True(calls >= 14); // Fence surrounds each of the two bounded owner snapshots and release.
        var wire = System.Text.Json.JsonSerializer.Serialize(answer);
        Assert.DoesNotContain("Amount", wire);
        Assert.DoesNotContain("Price", wire);
        Assert.DoesNotContain("PayloadJson", wire);
    }

    [SqlServerFact]
    public async Task Legal_identity_reader_refuses_wrong_company_original_version_and_revoked_release_without_partial_identity()
    {
        await using var run = await Run.StartAsync();
        var invoice = await FinalizedAsync(run, "legal-fence", "SENTETIK-LEGAL-FENCE", Guid.NewGuid().ToString("D"),
            "SENTETIK-checker", "SENTETIK-finalizer");
        await using var scope = run.Store(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var reader = new SalesFinalizedInvoiceIdentityReadPortV1(scope.Context, company);
        var version = invoice.Finalization!.FinalizedVersion;
        var foreign = await reader.ReadAsync(CompanyB, [new(invoice.Id, version)], _ => throw new InvalidOperationException("Must not call consumer"));
        Assert.False(foreign.Available); Assert.Empty(foreign.Items);
        var wrongVersion = await reader.ReadAsync(CompanyA, [new(invoice.Id, version + 1)], _ => Task.CompletedTask);
        Assert.False(wrongVersion.Available); Assert.Empty(wrongVersion.Items);
        var count = 0;
        await reader.ReadAsync(CompanyA, [new(invoice.Id, version)], _ => { count++; return Task.CompletedTask; });
        var checks = 0;
        var revoked = await reader.ReadAsync(CompanyA, [new(invoice.Id, version)],
            _ => ++checks == count ? throw new UnauthorizedAccessException("SENTETIK release revocation") : Task.CompletedTask);
        Assert.Equal(count, checks); Assert.False(revoked.Available); Assert.Empty(revoked.Items);
    }
}
