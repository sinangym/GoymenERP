using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Answers;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

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
    public async Task An_unconfigured_owner_never_invents_a_receipt_or_runs_a_consumer_fence()
    {
        var calls = 0;
        Assert.Null(await new AiAnswerReceiptWriterV1(null, new Clock()).RecordAsync(Company, "actor", "correlation", Answer(),
            _ => { calls++; return Task.CompletedTask; }));
        Assert.Equal(0, calls);
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
        Assert.Equal(AiKnowledgeDbContext.AnswerReceiptCheck, Assert.Single(receipt.GetCheckConstraints()).Sql);
        Assert.Equal(AiKnowledgeDbContext.AnswerIdentityCheck, Assert.Single(identity.GetCheckConstraints()).Sql);
        Assert.Contains(receipt.GetIndexes(), index => index.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "RetainUntilUtc", "AnswerId"]));
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Single(db.Database.GetMigrations(), id => id.EndsWith("_AddAiAnswerReceipts", StringComparison.Ordinal));
        var migration = new AddAiAnswerReceipts();
        Assert.Equal(new[] { "AnswerReceipts", "AnswerReceiptIdentities" }, migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name));
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 52312", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void Answer_id_is_optional_and_omitted_from_legacy_and_ungrounded_answer_wires()
    {
        var legacy = new AiAnswerV1("SENTETIK", [], "model", "7"); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("answerId", JsonSerializer.Serialize(legacy, options));
        var id = Guid.NewGuid(); Assert.Contains(id.ToString("D"), JsonSerializer.Serialize(legacy with { AnswerId = id }, options));
    }
    [Fact]
    public async Task Explicit_general_chat_receipt_is_content_free_mode_bound_and_uses_existing_panel_retention()
    {
        var store = new Capture(); var writer = new AiAnswerReceiptWriterV1(store, new Clock());
        var chat = Answer() with { Grounded = false, Citations = [] };
        Assert.NotNull(await writer.RecordGeneralChatAsync(Company, "actor", "correlation", chat, _ => Task.CompletedTask));
        Assert.NotNull(await writer.RecordGeneralChatAsync(Company, "actor", "correlation", chat, _ => Task.CompletedTask));
        Assert.NotNull(await writer.RecordGeneralChatAsync(Company, "actor", "correlation", chat with { Text = "SENTETIK changed" }, _ => Task.CompletedTask));
        Assert.NotNull(await writer.RecordGeneralChatAsync(Company, "actor", "correlation", chat with { PolicyVersion = "8" }, _ => Task.CompletedTask));
        var receipt = store.Values[0];
        Assert.True(AiAnswerReceiptRulesV1.IsValid(receipt)); Assert.Equal("Panel", receipt.Feature);
        Assert.Equal((At, At.AddDays(90)), (receipt.CreatedAtUtc, receipt.RetainUntilUtc));
        Assert.Equal(receipt.AnswerSha256, store.Values[1].AnswerSha256);
        Assert.All(store.Values.Skip(2), x => Assert.NotEqual(receipt.AnswerSha256, x.AnswerSha256));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string Hash(object value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(AiCanonicalJson.Canonicalize(JsonSerializer.Serialize(value, options)))));
        Assert.Equal(Hash(Array.Empty<string>()), receipt.CitationsSha256);
        // The explicit chat mode is not the legacy ERP fingerprint, even with identical text/model/policy.
        Assert.NotEqual(Hash(new { companyId = Company, chat.Text, chat.ModelDeployment, chat.Placement, chat.PolicyVersion,
            citationsHash = receipt.CitationsSha256 }), receipt.AnswerSha256);
        var grounded = Answer();
        Assert.NotNull(await writer.RecordAsync(Company, "actor", "correlation", grounded, _ => Task.CompletedTask));
        var legacy = store.Values[^1];
        Assert.Equal(Hash(new { companyId = Company, grounded.Text, grounded.ModelDeployment, grounded.Placement,
            grounded.PolicyVersion, citationsHash = legacy.CitationsSha256 }), legacy.AnswerSha256);
        Assert.DoesNotContain(chat.Text, JsonSerializer.Serialize(receipt));
        Assert.DoesNotContain("actor", JsonSerializer.Serialize(receipt));
    }

    [Theory]
    [InlineData("grounded-empty")] [InlineData("ungrounded-cited")] [InlineData("grounded-cited")]
    [InlineData("empty")] [InlineData("bad-policy")]
    public async Task General_chat_profile_never_turns_empty_citations_into_a_grounded_ERP_bypass(string fault)
    {
        var store = new Capture(); var writer = new AiAnswerReceiptWriterV1(store, new Clock());
        var chat = Answer() with { Grounded = false, Citations = [] };
        chat = fault switch { "grounded-empty" => chat with { Grounded = true },
            "ungrounded-cited" => chat with { Citations = [Citation] }, "grounded-cited" => Answer(),
            "empty" => chat with { Text = " " }, _ => chat with { PolicyVersion = " " } };
        Assert.Null(await writer.RecordGeneralChatAsync(Company, "actor", "correlation", chat, _ => Task.CompletedTask));
        Assert.Empty(store.Values);
    }

    [Fact]
    public async Task Precancelled_general_chat_does_not_reach_a_token_ignoring_owner()
    {
        var store = new Capture(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AiAnswerReceiptWriterV1(store, new Clock())
            .RecordGeneralChatAsync(Company, "actor", "correlation", Answer() with { Grounded = false, Citations = [] },
                _ => Task.CompletedTask, cancelled.Token));
        Assert.Empty(store.Values);
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
