using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Documents.Application.Contracts;
using Microsoft.AspNetCore.Http;

namespace GoymenERP.Api.IntegrationTests;

public sealed class DocumentBrandingRequestContractTests
{
    [Theory]
    [InlineData("{\"module\":1,\"templateCode\":\"sales.order-pdf\",\"expectedDraftVersion\":\"0\",\"expectedBrandingVersion\":\"1\",\"reason\":\"SENTETIK\",\"html\":\"<script>\"}")]
    [InlineData("{\"module\":1,\"module\":2,\"templateCode\":\"sales.order-pdf\",\"expectedDraftVersion\":\"0\",\"expectedBrandingVersion\":\"1\",\"reason\":\"SENTETIK\"}")]
    [InlineData("{\"Module\":1,\"templateCode\":\"sales.order-pdf\",\"expectedDraftVersion\":\"0\",\"expectedBrandingVersion\":\"1\",\"reason\":\"SENTETIK\"}")]
    public async Task The_prepare_wire_refuses_unknown_duplicate_and_wrong_case_members(string body)
    {
        var context = Context(body);
        await Assert.ThrowsAsync<JsonException>(() => DocumentBrandingEndpoints.Body<PrepareDocumentTemplateV1>(context, CancellationToken.None));
    }
    [Fact]
    public async Task A_nested_asset_cannot_override_its_document_identity_with_a_duplicate_member()
    {
        var context = Context("{\"expectedVersion\":\"0\",\"logo\":{\"documentId\":\"10000000-0000-0000-0000-000000000001\",\"documentId\":\"20000000-0000-0000-0000-000000000001\",\"versionNo\":1},\"letterhead\":null,\"reason\":\"SENTETIK\"}");
        await Assert.ThrowsAsync<JsonException>(() => DocumentBrandingEndpoints.Body<SetCompanyDocumentBrandingV1>(context, CancellationToken.None));
    }
    [Fact]
    public async Task Preparing_accepts_only_the_closed_identity_and_version_strings_without_any_asset_bytes_or_layout()
    {
        var context = Context("{\"module\":2,\"templateCode\":\"inventory.material-label.technical\",\"expectedDraftVersion\":\"0\",\"expectedBrandingVersion\":\"7\",\"reason\":\"SENTETIK\"}");
        var value = await DocumentBrandingEndpoints.Body<PrepareDocumentTemplateV1>(context, CancellationToken.None);
        Assert.Equal(DocumentTemplateModuleV1.Inventory, value.Module);
        Assert.Equal("7", value.ExpectedBrandingVersion);
        Assert.True(DocumentBrandingContractV1.IsKnown(value.Module, value.TemplateCode));
    }
    [Fact]
    public async Task Content_type_and_bounded_request_bytes_are_rejected_before_any_service_call()
    {
        var context = Context("{}"); context.Request.ContentType = "text/html";
        Assert.Equal(415, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => DocumentBrandingEndpoints.Body<SetCompanyDocumentBrandingV1>(context, CancellationToken.None))).Status);
        context = Context(new string(' ', 8193));
        Assert.Equal(400, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => DocumentBrandingEndpoints.Body<SetCompanyDocumentBrandingV1>(context, CancellationToken.None))).Status);
    }
    private static DefaultHttpContext Context(string body)
    {
        var context = new DefaultHttpContext(); context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body)); return context;
    }
}
