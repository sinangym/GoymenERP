using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ProductGlobalRuntimeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task A_known_Logistics_document_product_has_real_price_free_identity_under_only_Logistics_read_without_a_global_Product_grant() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var admin = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var createdResponse = await CreateAsync(admin, "s29-product-display", "SENTETIK İĞÜŞÖÇ");
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = (await createdResponse.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;
            var actor = Guid.Parse("58000000-0000-4000-8000-000000000060");
            var roleId = Guid.NewGuid();
            await using (var seed = factory.Services.CreateAsyncScope())
            {
                var identity = seed.ServiceProvider.GetRequiredService<IdentityDbContext>();
                identity.Users.Add(new UserAccount(actor, "SENTETIK Logistics document reader", null));
                var role = new RoleDefinition(roleId, "SENTETIK-S29-" + roleId.ToString("N"), "SENTETIK only Logistics read", true);
                role.AddPermission(ErpPermissions.LogisticsLoadingInstructionsRead); identity.Roles.Add(role);
                var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, CompanyA, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
                assignment.AddScope(AccessScopeType.Company, CompanyA); identity.RoleAssignments.Add(assignment); await identity.SaveChangesAsync();
            }
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(GoymenClaimTypes.UserId, actor.ToString("D")), new Claim(GoymenClaimTypes.CompanyAccess, CompanyA.ToString("D"))], "IsolatedSqlFixture")) };
            var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var authority = scope.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>();
            var owner = new ProductLogisticsDocumentDisplay(scope.ServiceProvider.GetRequiredService<ProductDbContext>(), company,
                scope.ServiceProvider.GetRequiredService<ICurrentUser>(), authority);
            var answer = await owner.ReadAsync(CompanyA, created.Id);
            Assert.Equal(ProductLogisticsDocumentDisplayOutcomeV1.Available, answer.Outcome);
            Assert.Equal((CompanyA, created.Id, created.Version, created.Code, created.Name), (answer.CompanyId, answer.ProductId, answer.ProductVersion, answer.Code, answer.Name));
            Assert.Equal(ProductLogisticsDocumentDisplayOutcomeV1.NotFound, (await owner.ReadAsync(CompanyA, Guid.NewGuid())).Outcome);
            Assert.Equal(ProductLogisticsDocumentDisplayOutcomeV1.Forbidden, (await owner.ReadAsync(CompanyB, created.Id)).Outcome);
            Assert.Equal(new[] { "Outcome", "CompanyId", "ProductId", "ProductVersion", "Code", "Name" }, typeof(ProductLogisticsDocumentDisplayV1).GetProperties().Select(x => x.Name));
        });
}
