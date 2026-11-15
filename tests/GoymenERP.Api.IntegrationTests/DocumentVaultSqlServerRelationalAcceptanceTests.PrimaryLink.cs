using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D49 (4) on real SQL Server: the upload's link is stored as the document's primary link and a later link is not;
/// the filtered unique index refuses a second primary link for the same document; the primary-link migration's
/// rollback passes while the marker equals "the earliest link" and is refused with 51447 once it does not. SENTETIK.
/// </summary>
public sealed partial class DocumentVaultSqlServerRelationalAcceptanceTests
{
    private const string PrimaryOf =
        "SELECT CAST([IsPrimary] AS int) FROM documents.DocumentLinks WHERE CompanyId = @company AND DocumentId = @document AND OwnerId = @owner";

    [SqlServerFact]
    public async Task The_upload_link_is_the_primary_link_a_later_link_is_not_and_a_second_primary_link_is_refused()
    {
        await using var run = await Run.StartAsync();
        var document = await UploadAndLinkAsync(run);

        Assert.Equal(1, await run.ScalarAsync<int>(PrimaryOf, ("@company", CompanyA), ("@document", document), ("@owner", PartyA)));
        Assert.Equal(0, await run.ScalarAsync<int>(PrimaryOf, ("@company", CompanyA), ("@document", document), ("@owner", PurchaseOrderA)));

        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE documents.DocumentLinks SET [IsPrimary] = 1 WHERE CompanyId = @company AND DocumentId = @document AND OwnerId = @owner",
            ("@company", CompanyA), ("@document", document), ("@owner", PurchaseOrderA)));

        // 2601: the filtered unique index UX_DocumentLinks_Primary.
        Assert.Equal(2601, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM documents.DocumentLinks WHERE CompanyId = @company AND DocumentId = @document AND [IsPrimary] = 1",
            ("@company", CompanyA), ("@document", document)));
    }

    [SqlServerFact]
    public async Task The_primary_link_migrations_Down_passes_with_the_upload_link_primary_and_refuses_with_51447_while_a_later_link_is_primary()
    {
        await using var run = await Run.StartAsync();
        var document = await UploadAndLinkAsync(run);
        var guard = Assert.IsType<SqlOperation>(new AddDocumentLinkPrimaryMarker().DownOperations[0]).Sql;

        // The marker equals the link order: the rollback would lose nothing.
        await run.ExecuteAsync(guard);

        // A primary link that is not the document's earliest link: the order can no longer tell it, so the rollback is refused.
        await run.ExecuteAsync(
            """
            UPDATE documents.DocumentLinks SET [IsPrimary] = 0 WHERE CompanyId = @company AND DocumentId = @document AND OwnerId = @upload;
            UPDATE documents.DocumentLinks SET [IsPrimary] = 1 WHERE CompanyId = @company AND DocumentId = @document AND OwnerId = @later;
            """,
            ("@company", CompanyA), ("@document", document), ("@upload", PartyA), ("@later", PurchaseOrderA));
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));

        Assert.Equal(51447, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(PrimaryOf, ("@company", CompanyA), ("@document", document), ("@owner", PurchaseOrderA)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM documents.DocumentLinks WHERE CompanyId = @company AND DocumentId = @document",
            ("@company", CompanyA), ("@document", document)));
    }

    /// <summary>Stores one upload under Cari party A and links the same document to purchase order A one hour later.</summary>
    private static async Task<Guid> UploadAndLinkAsync(Run run)
    {
        await using var a = run.Store(CompanyA);
        var stored = (await a.Store.StoreUploadAsync(run.Envelope("k-primary"), Hash("primary-upload"),
            Upload(CompanyA, PartyA, Pdf, "Birincil.pdf"), PermitWriteAsync)).Document;
        await a.Store.StoreLinkAsync(run.Envelope("k-primary-link"), Hash("primary-link"),
            new NewDocumentLink(stored.Id, 1, DocumentOwnerLink.TryCreate(DocumentOwnerLinkKindsV1.PurchasingPurchaseOrder, PurchaseOrderA)!,
                T0.AddHours(1)),
            PermitWriteAsync);
        return stored.Id;
    }
}
