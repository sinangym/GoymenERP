using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingPostingAccountMappingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CogsTrade621 = new("e0400000-0000-4000-8000-000000000621");
    private static readonly Guid CogsManufactured620 = new("e0400000-0000-4000-8000-000000000620");

    [SqlServerFact]
    public async Task Cogs_mapping_workspace_uses_read_authority_and_does_not_leak_another_company_plan()
    {
        await using var run = await Run.StartAsync();
        using var reader = run.Client(Preparer);
        using (var denied = await reader.GetAsync(MappingsPath + "/workspace"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Read);
        using (var available = await reader.GetAsync(MappingsPath + "/workspace"))
        {
            Assert.Equal(HttpStatusCode.OK, available.StatusCode);
            var value = await available.Content.ReadFromJsonAsync<AccountingPostingAccountMappingWorkspaceV1>();
            Assert.NotNull(value);
            Assert.Equal(CompanyA, value.CompanyId);
            Assert.Equal("Available", value.AccountPlanState);
            Assert.Contains(value.Accounts, account => account.AccountId == CogsTrade621 && account.Code == "621");
            Assert.True(available.Headers.CacheControl?.NoStore);
        }
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, AccountingPostingAccountMappingPermissionNamesV1.Read);
        using var other = run.Client(TestUsers.CompanyB, CompanyB);
        using var foreign = await other.GetAsync(MappingsPath + "/workspace");
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
        var body = await foreign.Content.ReadFromJsonAsync<AccountingPostingAccountMappingWorkspaceV1>();
        Assert.NotNull(body);
        Assert.Equal(CompanyB, body.CompanyId); Assert.Equal("Unavailable", body.AccountPlanState);
        Assert.Empty(body.Accounts); Assert.All(body.CogsDefaults, item => Assert.Null(item.AccountId));
        Assert.Null(body.Active); Assert.Empty(body.Prepared);
    }

    [SqlServerFact]
    public async Task The_cogs_mapping_constraint_Down_allows_old_mappings_and_refuses_while_a_cogs_choice_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new WidenPostingMappingsToCogs().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare);
        using var maker = run.Client(Preparer);
        using (var original = await PrepareAsync(maker, 0, Key("before-cogs")))
            Assert.Equal(HttpStatusCode.Created, original.StatusCode);
        await run.ExecuteAsync(guard);
        using var request = Command(HttpMethod.Post, MappingsPath, Key("protected-cogs"));
        request.Content = JsonContent.Create(new PrepareAccountingPostingAccountMappingRequestV1(1, new(2026, 11, 1),
            [new("cost-of-goods-sold", "trade-goods", null, null, null, CogsTrade621)]));
        using var prepared = await maker.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51929, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PostingAccountMappingRows "
            + "WHERE CompanyId = @c AND Purpose = N'cost-of-goods-sold'", ("@c", CompanyA)));
        var invalidSelector = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE accounting.PostingAccountMappingRows SET ItemType=N'Steel' WHERE Purpose=N'cost-of-goods-sold'"));
        Assert.Equal(547, invalidSelector.Number);
    }

    [SqlServerFact]
    public async Task Cogs_defaults_are_real_plan_choices_and_mapping_activation_keeps_the_existing_two_person_audited_flow()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Read,
            AccountingPostingAccountMappingPermissionNamesV1.Prepare, AccountingPostingAccountMappingPermissionNamesV1.Activate);
        await run.GrantAsync(Checker, AccountingPostingAccountMappingPermissionNamesV1.Read, AccountingPostingAccountMappingPermissionNamesV1.Activate);
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var workspace = await scope.ServiceProvider.GetRequiredService<AccountingPostingAccountMappingStoreV1>().WorkspaceAsync();
            Assert.Equal("Available", workspace.AccountPlanState);
            Assert.Equal(CogsTrade621, workspace.CogsDefaults.Single(x => x.MappingKey == "trade-goods").AccountId);
            Assert.Equal(CogsManufactured620, workspace.CogsDefaults.Single(x => x.MappingKey == "manufactured").AccountId);
            Assert.Equal(0, workspace.LatestMappingVersion); Assert.Null(workspace.Active); Assert.Empty(workspace.Prepared);
        }
        using var maker = run.Client(Preparer); using var checker = run.Client(Checker);
        using var request = Command(HttpMethod.Post, MappingsPath, Key("cogs-prepare"));
        request.Content = JsonContent.Create(new PrepareAccountingPostingAccountMappingRequestV1(0, new(2026, 11, 1),
            [new("cost-of-goods-sold", "trade-goods", null, null, null, CogsTrade621),
             new("cost-of-goods-sold", "manufactured", null, null, null, CogsManufactured620)]));
        using var prepared = await maker.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        using var self = await ActivateAsync(maker, 1, 1, 0, Key("cogs-self"));
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        using var approved = await ActivateAsync(checker, 1, 1, 0, Key("cogs-approve"));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(new Counts(1, 2, 3, 2), await run.CountsAsync());
        await using var finalScope = run.Factory.Services.CreateAsyncScope();
        finalScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var resolver = finalScope.ServiceProvider.GetRequiredService<IAccountingPostingAccountMappingResolverV1>();
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Mapped(CogsManufactured620, 1),
            await resolver.ResolveAsync(CompanyA, "cost-of-goods-sold", new(2026, 11, 17), "manufactured"));
        var invalid = await resolver.ResolveAsync(CompanyA, "cost-of-goods-sold", new(2026, 11, 17), "trade-goods", itemType: "Steel");
        Assert.Equal(AccountingPostingAccountMappingResolutionKindV1.Unavailable, invalid.Kind);
    }
}
