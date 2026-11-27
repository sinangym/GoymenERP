using System.Text.Json;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class IdentityAccessRequestImpactContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"userId\":\"11111111-1111-4111-8111-111111111111\"}")]
    [InlineData("{\"roleId\":\"11111111-1111-4111-8111-111111111111\"}")]
    [InlineData("{\"userId\":\"11111111-1111-4111-8111-111111111111\",\"roleId\":\"22222222-2222-4222-8222-222222222222\",\"companyId\":\"33333333-3333-4333-8333-333333333333\"}")]
    [InlineData("{\"userId\":\"11111111-1111-4111-8111-111111111111\",\"roleId\":\"22222222-2222-4222-8222-222222222222\",\"approve\":true}")]
    public void Request_requires_only_the_two_target_ids_and_rejects_missing_or_extra_fields(string body) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IdentityRoleRequestImpactQueryV1>(body, Json));

    [Fact]
    public void Code_only_wire_preserves_unknown_SoD_and_scope_without_names_or_an_assignment_command()
    {
        var company = Guid.NewGuid(); var target = Guid.NewGuid(); var role = Guid.NewGuid();
        var request = new IdentityRoleRequestImpactQueryV1(target, role);
        using var query = JsonDocument.Parse(JsonSerializer.Serialize(request, Json));
        Assert.Equal(["userId", "roleId"], query.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(request, JsonSerializer.Deserialize<IdentityRoleRequestImpactQueryV1>(query.RootElement.GetRawText(), Json));
        using var impact = JsonDocument.Parse(JsonSerializer.Serialize(new IdentityRoleRequestImpactV1(company, target, role,
            [], [], [], [], false, null, null), Json));
        Assert.Equal(["companyId", "userId", "roleId", "currentPermissionCodes", "requestedRolePermissionCodes", "addedPermissionCodes",
            "alreadyHeldCodes", "roleHasCriticalPermissions", "hasSegregationOfDutiesConflict", "expandsScope"], impact.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, impact.RootElement.GetProperty("hasSegregationOfDutiesConflict").ValueKind);
        Assert.Equal(JsonValueKind.Null, impact.RootElement.GetProperty("expandsScope").ValueKind);
    }
}
