using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Answers;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AiAnswerReceiptTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly AiCitationV1 Citation = new("test.source.v1", "1", "synthetic-record", Guid.NewGuid(), "SENTETIK", "source-7", At);
    private static AiGatewayAnswerV1 Answer() => new("SENTETIK answer", true, [Citation], "SENTETIK-model", AiModelPlacementV1.OnPremises, "7", 1);

    [Fact]
    public async Task Canonical_answer_and_citation_hashes_bind_the_exact_released_content_model_policy_and_source_version()
    {
        var store = new Capture(); var writer = new AiAnswerReceiptWriterV1(store, new Clock());
        var answer = Answer();
        foreach (var variant in new[] { answer, answer, answer with { Text = "SENTETIK changed" }, answer with { PolicyVersion = "8" },
            answer with { ModelDeployment = "SENTETIK-other" }, answer with { Citations = [Citation with { SourceVersion = "source-8" }] } })
            Assert.NotNull(await writer.RecordAsync(Company, "actor", "correlation", variant, _ => Task.CompletedTask));
        Assert.Equal(store.Values[0].AnswerSha256, store.Values[1].AnswerSha256);
        Assert.Equal(store.Values[0].CitationsSha256, store.Values[1].CitationsSha256);
        Assert.All(store.Values.Skip(2), x => Assert.NotEqual(store.Values[0].AnswerSha256, x.AnswerSha256));
        Assert.Equal(store.Values[0].CitationsSha256, store.Values[2].CitationsSha256);
        Assert.NotEqual(store.Values[0].CitationsSha256, store.Values[5].CitationsSha256);
        Assert.All(store.Values, x => Assert.Equal((At, At.AddDays(90)), (x.CreatedAtUtc, x.RetainUntilUtc)));
        Assert.Equal(store.Values.Count, store.Values.Select(x => x.AnswerId).Distinct().Count());
    }

    [Fact]
    public async Task Citation_order_is_bound_to_the_released_answer_and_is_not_reconstructed_as_an_unordered_set()
    {
        var store = new Capture(); var writer = new AiAnswerReceiptWriterV1(store, new Clock());
        var other = Citation with { TargetId = Guid.NewGuid(), SourceVersion = "source-8" };
        Assert.NotNull(await writer.RecordAsync(Company, "actor", "correlation", Answer() with { Citations = [Citation, other] }, _ => Task.CompletedTask));
        Assert.NotNull(await writer.RecordAsync(Company, "actor", "correlation", Answer() with { Citations = [other, Citation] }, _ => Task.CompletedTask));
        Assert.NotEqual(store.Values[0].CitationsSha256, store.Values[1].CitationsSha256);
        Assert.NotEqual(store.Values[0].AnswerSha256, store.Values[1].AnswerSha256);
    }

    [Theory]
    [InlineData("ungrounded")] [InlineData("empty")] [InlineData("no-citation")] [InlineData("foreign-shape")]
    public async Task Invalid_or_unsupported_output_does_not_reach_persistence(string fault)
    {
        var store = new Capture(); var answer = Answer();
        answer = fault switch { "ungrounded" => answer with { Grounded = false }, "empty" => answer with { Text = " " },
            "no-citation" => answer with { Citations = [] }, _ => answer with { Citations = [Citation with { TargetId = Guid.Empty }] } };
        Assert.Null(await new AiAnswerReceiptWriterV1(store, new Clock()).RecordAsync(Company, "actor", "correlation", answer, _ => Task.CompletedTask));
        Assert.Empty(store.Values);
    }

    [Fact]
    public async Task A_receipt_storage_collision_is_not_retried_or_presented_as_a_successful_answer()
    {
        var store = new Capture { Accepted = false };
        Assert.Null(await new AiAnswerReceiptWriterV1(store, new Clock()).RecordAsync(Company, "actor", "correlation", Answer(), _ => Task.CompletedTask));
        Assert.Single(store.Values);
    }

    [Fact]
    public void The_model_separates_identity_from_content_free_receipts_and_preserves_company_and_retention_constraints()
    {
        using var db = new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseSqlServer("Server=unused;Database=unused").Options, new CompanyContext());
        var model = db.GetService<IDesignTimeModel>().Model;
        var receipt = model.FindEntityType(typeof(AiAnswerReceiptRow))!;
        var identity = model.FindEntityType(typeof(AiAnswerReceiptIdentityRow))!;
        Assert.Equal("ai", receipt.GetSchema()); Assert.Equal("AnswerReceipts", receipt.GetTableName());
        Assert.Equal(new[] { "CompanyId", "AnswerId" }, receipt.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.NotEmpty(receipt.GetDeclaredQueryFilters()); Assert.NotEmpty(identity.GetDeclaredQueryFilters());
        foreach (var field in new[] { "ActorId", "CorrelationId", "Text", "Question", "ContentJson", "TargetId", "Title" }) Assert.Null(receipt.FindProperty(field));
        Assert.NotNull(identity.FindProperty("ActorId")); Assert.NotNull(identity.FindProperty("CorrelationId"));
        Assert.Contains(identity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(AiAnswerReceiptRow)
            && fk.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "AnswerId"]));
        Assert.Contains("DATEADD(day,90", Assert.Single(receipt.GetCheckConstraints()).Sql);
    }

    [Fact]
    public void Answer_id_is_optional_and_omitted_from_legacy_and_ungrounded_answer_wires()
    {
        var legacy = new AiAnswerV1("SENTETIK", [], "model", "7"); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("answerId", JsonSerializer.Serialize(legacy, options));
        var id = Guid.NewGuid(); Assert.Contains(id.ToString("D"), JsonSerializer.Serialize(legacy with { AnswerId = id }, options));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => At; }
    private sealed class Capture : IAiAnswerReceiptPersistenceV1
    {
        public List<AiAnswerReceiptV1> Values { get; } = []; public bool Accepted = true;
        public async Task<bool> TryAddAsync(AiAnswerReceiptV1 receipt, AiAnswerReceiptIdentityV1 identity, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); Values.Add(receipt); return Accepted; }
        public Task<AiAnswerReceiptV1?> GetOwnAsync(Guid c, Guid id, string a, Func<CancellationToken, Task> fence, CancellationToken token = default) => throw new NotSupportedException();
    }
}
