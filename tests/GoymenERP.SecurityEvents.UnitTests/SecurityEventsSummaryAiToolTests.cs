using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Ai;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;

namespace GoymenERP.SecurityEvents.UnitTests;

/// <summary>
/// WP-C10 <c>security.events.summary.v1</c> over a SENTETIK owner read: counts come only from the owner's pages, an
/// unauthorized caller gets nothing, another company's row releases nothing, and actor/correlation/integrity values never
/// leave the tool (actors become per-response pseudonyms).
/// </summary>
public sealed class SecurityEventsSummaryAiToolTests
{
    private static readonly Guid CompanyB = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private const string ActorOne = "5e0c1d2a-7b3f-4c8e-9a10-0000000c1001";
    private const string ActorTwo = "5e0c1d2a-7b3f-4c8e-9a10-0000000c1002";

    [Fact]
    public void The_descriptor_is_a_confidential_read_tool_that_needs_only_the_existing_security_event_permission()
    {
        var descriptor = Tool(new FakeQuery()).Descriptor;

        Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
        Assert.Equal(AiDataClassV1.Confidential, descriptor.DataClass);
        Assert.Equal(new[] { ErpPermissions.PlatformSecurityEventsRead }, descriptor.RequiredPermissions);
        Assert.Null(descriptor.OwnerCommandName);
        Assert.Equal("SecurityEvents", descriptor.OwnerModule);
    }

    [Fact]
    public async Task An_unauthorized_caller_is_denied_and_nothing_is_released()
    {
        var query = new FakeQuery { Status = SecurityEventQueryStatusV1.Denied };
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AuthorizationDenied, ActorOne, 0));

        var result = await Tool(query).ReadAsync(new AiToolInputV1("{}"), Context(SecurityEventFixtures.CompanyA));

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.ContentJson);
        Assert.Null(result.Source);
    }

    [Fact]
    public async Task A_row_of_another_company_releases_nothing()
    {
        var query = new FakeQuery();
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AuthorizationDenied, ActorOne, 0));
        query.Add(Row(CompanyB, SecurityEventTypesV1.AuthorizationDenied, ActorTwo, 1));

        var result = await Tool(query).ReadAsync(new AiToolInputV1("{}"), Context(SecurityEventFixtures.CompanyA));

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task A_server_context_of_another_company_than_the_owner_read_releases_nothing()
    {
        var query = new FakeQuery();
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.PermissionChanged, ActorOne, 0));

        var result = await Tool(query).ReadAsync(new AiToolInputV1("{}"), Context(CompanyB));

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task Actors_are_masked_as_per_response_pseudonyms_and_no_identity_correlation_or_hash_leaves()
    {
        var query = new FakeQuery();
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AuthorizationDenied, ActorOne, 0));
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AuthorizationDenied, ActorOne, 1));
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AiAuthorizationDenied, ActorTwo, 2));
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.PermissionChanged, ActorTwo, 3,
            severity: SecurityEventSeveritiesV1.High, requestedCompany: CompanyB));

        var result = await Tool(query).ReadAsync(new AiToolInputV1("{}"), Context(SecurityEventFixtures.CompanyA));

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        var json = result.ContentJson!;
        foreach (var leaked in new[] { ActorOne, ActorTwo, "corr-sentetik", "hash-sentetik", CompanyB.ToString("D"),
                     SecurityEventFixtures.CompanyA.ToString("D"), "retain", "policy" })
        {
            Assert.DoesNotContain(leaked, json, StringComparison.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        // Newest first: ActorTwo appears first and is "Kişi 1"; ActorOne becomes "Kişi 2" on both of its rows.
        Assert.Equal(new[] { "Kişi 1", "Kişi 1", "Kişi 2", "Kişi 2" },
            items.Select(item => item.GetProperty("actor").GetString()).ToArray());
        Assert.True(items[0].GetProperty("crossCompanyAttempt").GetBoolean());
        Assert.False(items[1].GetProperty("crossCompanyAttempt").GetBoolean());
        Assert.Equal(AiDataClassV1.Confidential, result.Source!.DataClass);
        Assert.Equal(SecurityEventFixtures.CompanyA, result.Source.TargetId);
    }

    [Fact]
    public async Task Counts_come_from_the_owner_rows_per_category_and_aggregated_denials_are_summed()
    {
        var query = new FakeQuery();
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.LoginFailed, null, 0, occurrences: 4));
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AuthorizationDenied, ActorOne, 1, occurrences: 3));
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AiRequestRefused, ActorOne, 2));
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.ExportPerformed, ActorTwo, 3));
        query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AiToolCalled, ActorTwo, 4));

        var result = await Tool(query).ReadAsync(new AiToolInputV1("{\"limit\":2}"), Context(SecurityEventFixtures.CompanyA));

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var document = JsonDocument.Parse(result.ContentJson!);
        var root = document.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        var counts = root.GetProperty("categories").EnumerateArray()
            .ToDictionary(item => item.GetProperty("category").GetString()!, item => (
                item.GetProperty("occurrenceCount").GetInt32(), item.GetProperty("rowCount").GetInt32()));
        Assert.Equal((4, 1), counts[SecurityEventDigestReader.FailedLogins]);
        Assert.Equal((4, 2), counts[SecurityEventDigestReader.AuthorizationDenials]);
        Assert.Equal((1, 1), counts[SecurityEventDigestReader.CriticalChanges]);
        Assert.Equal(2, root.GetProperty("listedCount").GetInt32());
        // ai.tool.called is not a summary type and is never read.
        Assert.DoesNotContain(SecurityEventTypesV1.AiToolCalled, query.RequestedTypes);
        Assert.Equal(SecurityEventDigestReader.Categories.Values.Sum(types => types.Count), query.RequestedTypes.Count);
    }

    [Fact]
    public async Task A_type_with_more_rows_than_one_page_marks_its_category_incomplete()
    {
        var query = new FakeQuery();
        for (var minute = 0; minute <= SecurityEventDigestReader.PageSize; minute++)
        {
            query.Add(Row(SecurityEventFixtures.CompanyA, SecurityEventTypesV1.AuthorizationDenied, ActorOne, minute));
        }

        var result = await Tool(query).ReadAsync(
            new AiToolInputV1("{\"category\":\"authorization-denials\"}"), Context(SecurityEventFixtures.CompanyA));

        using var document = JsonDocument.Parse(result.ContentJson!);
        Assert.False(document.RootElement.GetProperty("complete").GetBoolean());
        var category = Assert.Single(document.RootElement.GetProperty("categories").EnumerateArray().ToArray());
        Assert.False(category.GetProperty("complete").GetBoolean());
        Assert.Equal(SecurityEventDigestReader.PageSize, category.GetProperty("rowCount").GetInt32());
        Assert.Equal(new[] { SecurityEventTypesV1.AuthorizationDenied },
            document.RootElement.GetProperty("incompleteEventTypes").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [Theory]
    [InlineData("{\"companyId\":\"a1a1a1a1-0000-4000-8000-000000000001\"}")]
    [InlineData("{\"category\":\"ai-tool-calls\"}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":51}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task Malformed_or_unscoped_arguments_are_invalid_and_nothing_is_read(string arguments)
    {
        var query = new FakeQuery();

        var result = await Tool(query).ReadAsync(new AiToolInputV1(arguments), Context(SecurityEventFixtures.CompanyA));

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Empty(query.RequestedTypes);
    }

    [Fact]
    public async Task An_unrecorded_owner_read_is_unavailable()
    {
        var query = new FakeQuery { Status = SecurityEventQueryStatusV1.Unavailable };

        var result = await Tool(query).ReadAsync(new AiToolInputV1("{}"), Context(SecurityEventFixtures.CompanyA));

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
    }

    private static SecurityEventsSummaryAiTool Tool(FakeQuery query) =>
        new(new SecurityEventDigestReader(query), new FixedTime(Now));

    private static AiServerContextV1 Context(Guid company) => new(company, SecurityEventFixtures.UserActorId,
        "v1.session-sentetik", $"company:{company:D}", "roles:sentetik", "corr-ai-c10-sentetik", null, null, null);

    private static SecurityEventItemV1 Row(Guid company, string eventType, string? actorId, int minute,
        string severity = SecurityEventSeveritiesV1.Warning, Guid? requestedCompany = null, int occurrences = 1) => new(
        Guid.NewGuid(),
        1,
        SecurityEventFixtures.OccurredAt.AddMinutes(minute),
        company,
        requestedCompany,
        actorId is null ? SecurityEventActorTypesV1.Anonymous : SecurityEventActorTypesV1.User,
        actorId,
        eventType,
        SecurityEventPhasesV1.Single,
        SecurityEventOutcomesV1.Denied,
        SecurityEventReasonCodesV1.PermissionMissing,
        severity,
        "purchasing.requests.approve",
        "policy-sentetik",
        1,
        $"corr-sentetik-{minute}",
        null,
        SourceChannels.Web,
        null,
        null,
        null,
        null,
        SecurityEventFixtures.OccurredAt.AddYears(2),
        occurrences,
        null,
        null,
        $"hash-sentetik-{minute}");

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A SENTETIK owner read: newest first, one page per type, the status it is told to answer.</summary>
    private sealed class FakeQuery : ISecurityEventQueryServiceV1
    {
        private readonly List<SecurityEventItemV1> rows = [];

        public SecurityEventQueryStatusV1 Status { get; init; } = SecurityEventQueryStatusV1.Ok;

        public List<string> RequestedTypes { get; } = [];

        public void Add(SecurityEventItemV1 row) => rows.Add(row);

        public Task<SecurityEventQueryResultV1> GetEventsAsync(int pageSize, string? cursor, string? eventType,
            string? correlationId, CancellationToken cancellationToken = default)
        {
            RequestedTypes.Add(eventType!);
            if (Status != SecurityEventQueryStatusV1.Ok)
            {
                return Task.FromResult(new SecurityEventQueryResultV1(Status, null));
            }

            var matching = rows.Where(row => row.EventType == eventType)
                .OrderByDescending(row => row.OccurredAtUtc).ThenByDescending(row => row.Id).ToArray();
            var page = matching.Take(pageSize).ToArray();
            return Task.FromResult(SecurityEventQueryResultV1.Ok(
                new SecurityEventPageV1(page, pageSize, matching.Length > pageSize ? "next-sentetik" : null)));
        }
    }
}
