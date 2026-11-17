using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class DocumentVaultSqlServerRelationalAcceptanceTests
{
    private const string InvoiceXmlContentKindMigration = "20261117112418_AddPurchaseInvoiceXmlContentKind";
    [SqlServerFact]
    public async Task Invoice_xml_kind_down_passes_without_xml_and_refuses_with_51995_while_a_source_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseInvoiceXmlContentKind().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var xml = "<Invoice xmlns='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2'/>"u8.ToArray();
        await using (var scope = run.Store(CompanyA))
            await scope.Store.StoreUploadAsync(run.Envelope("invoice-xml"), Hash("invoice-xml"),
                Upload(CompanyA, PartyA, xml, "fatura.xml", DocumentOwnerLinkKindsV1.PurchasingInvoiceImport, DocumentFileKind.UblInvoiceXml), PermitWriteAsync);
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51995, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.ContentBlobs WHERE ContentKind=7"));
        Assert.Equal((1, 1, 1, 1, 1), await run.CountsAsync());
    }
}
