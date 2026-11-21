using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-001 local session to the shell: after a local login the capability snapshot opens for the company the user is
/// assigned to, and the generation follows the session (logout and a new login give a new one). In-memory HTTP host,
/// SENTETIK user and role; not real SQL evidence.
/// </summary>
public sealed partial class LocalAccountSessionApiTests
{
    private const string ShellCapabilitiesPath = "/api/v1/shell/capabilities";

    [Fact]
    public async Task A_local_login_opens_the_shell_capabilities_and_a_new_login_gets_a_new_generation()
    {
        await using var factory = new LocalAccountApiFactory();
        var user = await factory.SeedLocalUserAsync(UserName, Password);
        await GrantCompanyRoleAsync(factory, user, TestCompanyIds.CompanyA, ErpPermissions.OrganizationSitesRead);
        using var client = factory.CreateClient();

        using (var anonymous = await client.GetAsync(ShellCapabilitiesPath))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using (var login = await LoginAsync(client, UserName, Password))
        {
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }

        var first = await ShellGenerationAsync(client);
        Assert.Equal(first, await ShellGenerationAsync(client));

        using (var logout = await LogoutAsync(client))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using (var closed = await client.GetAsync(ShellCapabilitiesPath))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, closed.StatusCode);
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var again = await LoginAsync(client, UserName, Password))
        {
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        Assert.NotEqual(first, await ShellGenerationAsync(client));
    }

    private static async Task<string> ShellGenerationAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ShellCapabilitiesPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var generation = body.RootElement.GetProperty("generation").GetString();
        Assert.False(string.IsNullOrWhiteSpace(generation));
        return generation!;
    }

    private static async Task GrantCompanyRoleAsync(LocalAccountApiFactory factory, Guid userId, Guid companyId,
        params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"SENTETIK-LOCAL-SHELL-{roleId:N}", "SENTETIK yerel oturum menü rolü", true);
        foreach (var permission in permissions)
        {
            role.AddPermission(permission);
        }

        var assignment = new RoleAssignment(Guid.NewGuid(), userId, roleId, companyId,
            factory.Clock.GetUtcNow().AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, companyId);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }
}
