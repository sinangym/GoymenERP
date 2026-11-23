using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Ai;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-C10 <c>security.events.summary.v1</c> on real SQL Server through <c>POST /api/v1/ai/ask</c>: the tool reads only
/// the caller company's rows through the owner read (each read recorded as <c>security.events.read</c>), masks the actor
/// identity, and a user without <c>platform.security-events.read</c> is refused before anything is read. The AI runtime is
/// enabled only inside this test host (Fake provider); shipped configuration keeps <c>Ai:Enabled=false</c> and
/// <c>Ai:AllowedTools</c> empty, and no shipped role holds the permission.
/// </summary>
public sealed partial class SecurityEventsSqlServerRelationalAcceptanceTests
{
    private const string MaskedActorA = "7c4f2b1e-3d5a-4e6f-8a9b-0000000c10a1";
    private const string MaskedActorB = "7c4f2b1e-3d5a-4e6f-8a9b-0000000c10a2";

    [SqlServerFact]
    public async Task The_ai_security_event_summary_reads_only_own_company_rows_masks_actors_and_refuses_without_the_grant_on_real_sql()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(
            baseConnection,
            databaseName: $"GoymenERP_SecurityEventsAiSummary_{Guid.NewGuid():N}",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Ai:Enabled"] = "true",
                ["Ai:Provider"] = AiProviderKindsV1.Fake,
                ["Ai:PolicyRef"] = "TEST-AI-POLICY",
                ["Ai:PolicyVersion"] = "1",
                ["Ai:MaxQuestionLength"] = "2000",
                ["Ai:MaxToolCallsPerRequest"] = "1",
                ["Ai:MaxArgumentBytes"] = "1024",
                ["Ai:MaxDataClass"] = "Confidential",
                ["Ai:AllowedTools:0:ToolId"] = SecurityEventsSummaryAiTool.ToolId,
                ["Ai:AllowedTools:0:Version"] = SecurityEventsSummaryAiTool.ToolVersion,
                ["Ai:AllowedTools:0:DescriptionSha256"] = AiToolDescriptionPinV1.Compute(
                    new SecurityEventsSummaryAiTool(null!, TimeProvider.System).Descriptor),
            });
        try
        {
            using (var health = await factory.CreateClient().GetAsync("/health/live"))
            {
                health.EnsureSuccessStatusCode();
            }

            await GrantAiSecurityReaderAsync(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA,
                ErpPermissions.AiPanelUse, ErpPermissions.PlatformSecurityEventsRead);
            // Company B's user may use the panel but holds no security event read.
            await GrantAiSecurityReaderAsync(factory, TestUsers.CompanyB, TestCompanyIds.CompanyB, ErpPermissions.AiPanelUse);
            var writer = factory.Services.GetRequiredService<ISecurityEventWriterV1>();
            Assert.True((await writer.WriteAsync(ActorDenial(TestCompanyIds.CompanyA, MaskedActorA, 0), CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(ActorDenial(TestCompanyIds.CompanyA, MaskedActorA, 1), CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(ActorDenial(TestCompanyIds.CompanyA, MaskedActorB, 2), CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(ActorDenial(TestCompanyIds.CompanyB, MaskedActorB, 3), CancellationToken.None)).Recorded);

            using var clientA = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
            using var answered = await clientA.PostAsJsonAsync("/api/v1/ai/ask", SummaryAsk());
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
            var response = Assert.IsType<AiAskResponseV1>(await answered.Content.ReadFromJsonAsync<AiAskResponseV1>());
            var toolResult = Assert.Single(response.ToolResults);
            Assert.Equal(AiToolResultOutcomesV1.Succeeded, toolResult.Outcome);
            var contentJson = toolResult.ContentJson!;
            using var content = JsonDocument.Parse(contentJson);
            var denials = content.RootElement.GetProperty("categories").EnumerateArray()
                .Single(category => category.GetProperty("category").GetString() == SecurityEventDigestReader.AuthorizationDenials);
            Assert.Equal(3, denials.GetProperty("rowCount").GetInt32());
            var actors = content.RootElement.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("eventType").GetString() == SecurityEventTypesV1.AuthorizationDenied)
                .Select(item => item.GetProperty("actor").GetString())
                .ToArray();
            Assert.Equal(3, actors.Length);
            Assert.Equal(2, actors.Distinct().Count());
            Assert.All(actors, actor => Assert.StartsWith(SecurityEventDigestReader.PersonPseudonymPrefix, actor, StringComparison.Ordinal));
            Assert.DoesNotContain(MaskedActorA, contentJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(MaskedActorB, contentJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TestCompanyIds.CompanyB.ToString("D"), contentJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("corr-c10", contentJson, StringComparison.Ordinal);
            Assert.DoesNotContain("integrityHash", contentJson, StringComparison.Ordinal);
            // Every per-type owner read was recorded as a company A security.events.read before its rows were read.
            Assert.Equal(
                SecurityEventDigestReader.Categories.Values.Sum(types => types.Count).ToString(System.Globalization.CultureInfo.InvariantCulture),
                await ScalarAsync(factory.DatabaseConnectionString,
                    "SELECT COUNT(*) FROM [security].[SecurityEvents] WHERE [EventType] = N'security.events.read' "
                    + $"AND [CompanyId] = '{TestCompanyIds.CompanyA:D}'"));

            using var clientB = factory.CreateClientFor(TestUsers.CompanyB, TestCompanyIds.CompanyB);
            using var refused = await clientB.PostAsJsonAsync("/api/v1/ai/ask", SummaryAsk());
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("0", await ScalarAsync(factory.DatabaseConnectionString,
                "SELECT COUNT(*) FROM [security].[SecurityEvents] WHERE [EventType] = N'security.events.read' "
                + $"AND [CompanyId] = '{TestCompanyIds.CompanyB:D}'"));
            Assert.Equal("1", await ScalarAsync(factory.DatabaseConnectionString,
                "SELECT COUNT(*) FROM [security].[SecurityEvents] WHERE [EventType] = N'ai.authz.denied' "
                + $"AND [CompanyId] = '{TestCompanyIds.CompanyB:D}' AND [PermissionCode] = N'platform.security-events.read'"));
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            await cleanupScope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>().Database.EnsureDeletedAsync();
        }
    }

    private static object SummaryAsk() => new
    {
        question = "SENTETIK güvenlik olayı özeti",
        toolId = SecurityEventsSummaryAiTool.ToolId,
        toolVersion = SecurityEventsSummaryAiTool.ToolVersion,
        arguments = new { category = SecurityEventDigestReader.AuthorizationDenials, limit = 10 },
    };

    private static SecurityEventV1 ActorDenial(Guid companyId, string actorId, int minute) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = OccurredAt.AddMinutes(minute),
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = companyId,
        ActorType = SecurityEventActorTypesV1.User,
        ActorId = actorId,
        EventType = SecurityEventTypesV1.AuthorizationDenied,
        Outcome = SecurityEventOutcomesV1.Denied,
        ReasonCode = SecurityEventReasonCodesV1.PermissionMissing,
        Severity = SecurityEventSeveritiesV1.Warning,
        PermissionCode = "purchasing.requests.approve",
        CorrelationId = $"corr-c10-{Guid.NewGuid():N}",
        SourceChannel = SourceChannels.Web,
    };

    private static async Task GrantAiSecurityReaderAsync(
        SqlServerRelationalApiFactory factory, string subject, Guid company, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await identity.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == subject);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            identity.Users.Add(new UserAccount(actor, "Synthetic AI security reader", null));
            identity.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, subject));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"C10-AI-{roleId:N}", "Synthetic AI security reader role", false);
        foreach (var permission in permissions)
        {
            role.AddPermission(permission);
        }

        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
    }
}
