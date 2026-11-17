using System.Text;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Documents.UnitTests;

public sealed class DocumentUblInvoiceContentRulesTests
{
    [Fact]
    public void Xml_content_kind_migration_preserves_existing_bytes_and_guards_down_before_narrowing()
    {
        var migration = new AddPurchaseInvoiceXmlContentKind();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal("[ContentKind] IN (1, 2, 3, 4, 5, 6, 7)", Assert.IsType<AddCheckConstraintOperation>(migration.UpOperations[1]).Sql);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("WHERE [ContentKind] = 7", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51995", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal("[ContentKind] IN (1, 2, 3, 4, 5, 6)", Assert.IsType<AddCheckConstraintOperation>(migration.DownOperations[2]).Sql);
    }
    private const string Namespace = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly byte[] Invoice = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\"?><Invoice xmlns=\"{Namespace}\"><Note>Sentetik</Note></Invoice>");

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/xml; charset=utf-8")]
    [InlineData("application/octet-stream")]
    public void Ubl_invoice_is_owner_opt_in_and_canonical_xml_even_when_the_declared_type_differs(string declared)
    {
        var name = DocumentFileName.TryCreate("fatura.xml")!;
        var result = DocumentContentPolicy.Classify(name, declared, Invoice, 65536, DocumentContentPolicy.PurchaseInvoiceImportKinds);
        Assert.True(result.IsAccepted);
        Assert.Equal(DocumentFileKind.UblInvoiceXml, result.Kind!.Kind);
        Assert.Equal("application/xml", result.Kind.ContentType);
        Assert.Equal(DocumentRuleCodes.TypeNotAllowed, DocumentContentPolicy.Classify(name, declared, Invoice, 65536).RefusalCode);
        Assert.Equal(DocumentRuleCodes.TypeNotAllowed, DocumentContentPolicy.Classify(name, declared, Invoice, 65536,
            DocumentContentPolicy.BankStatementImportKinds).RefusalCode);
    }

    [Theory]
    [InlineData("<!DOCTYPE Invoice [<!ENTITY file SYSTEM 'file:///unavailable'>]><Invoice xmlns='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2'>&file;</Invoice>")]
    [InlineData("<?xml-stylesheet type='text/xsl' href='https://example.invalid/transform'?><Invoice xmlns='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2'/>")]
    [InlineData("<Invoice/>")]
    [InlineData("<Invoice xmlns='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2'/><extra/>")]
    [InlineData("<Invoice xmlns='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2'>")]
    public void Malformed_external_or_unrecognized_xml_is_refused_before_storage(string source)
    {
        var result = DocumentContentPolicy.Classify(DocumentFileName.TryCreate("fatura.xml")!, "application/xml",
            Encoding.UTF8.GetBytes(source), 65536, DocumentContentPolicy.PurchaseInvoiceImportKinds);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, result.RefusalCode);
    }

    [Fact]
    public void Xml_depth_and_actual_bytes_are_checked_without_fetching_or_rendering_content()
    {
        var source = $"<Invoice xmlns='{Namespace}'>" + string.Concat(Enumerable.Repeat("<item>", 70))
            + string.Concat(Enumerable.Repeat("</item>", 70)) + "</Invoice>";
        Assert.False(DocumentContentPolicy.Classify(DocumentFileName.TryCreate("fatura.xml")!, null,
            Encoding.UTF8.GetBytes(source), 65536, DocumentContentPolicy.PurchaseInvoiceImportKinds).IsAccepted);
        Assert.False(DocumentContentPolicy.Classify(DocumentFileName.TryCreate("fatura.xml")!, "text/html",
            Invoice, 65536, DocumentContentPolicy.PurchaseInvoiceImportKinds).IsAccepted);
    }
}
