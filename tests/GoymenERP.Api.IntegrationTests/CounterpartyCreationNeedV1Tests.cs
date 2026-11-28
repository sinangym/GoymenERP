using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyCreationNeedV1Tests
{
    [Fact]
    public void Need_wire_is_exactly_the_existing_six_task_fields_and_has_no_fake_Party_or_company_identity()
    {
        var q = new CounterpartyCreationNeedRequestV1("SENTETIK ihtiyaç", "  SENTETIK ham açıklama  ", Guid.NewGuid(),
            new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.FromHours(3)), null, null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); var json = JsonSerializer.SerializeToElement(q, options);
        Assert.Equal(new[] { "title", "details", "assigneeUserId", "dueAtUtc", "recordType", "recordId" }, json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(q, json.Deserialize<CounterpartyCreationNeedRequestV1>(options));
        Assert.Equal(JsonSerializer.Serialize(new CreateWorkTaskRequestV1(q.Title, q.Details, q.AssigneeUserId, q.DueAtUtc, null, null), options), json.GetRawText());
        Assert.Equal(q.Details, json.GetProperty("details").GetString());
        Assert.Equal(q.DueAtUtc, json.GetProperty("dueAtUtc").GetDateTimeOffset());
        Assert.DoesNotContain(typeof(CounterpartyCreationNeedHostV1).GetConstructors().Single().GetParameters(), p => p.ParameterType == typeof(ICounterpartyCommandService));
    }
    [Theory] [InlineData("companyId")] [InlineData("partyId")] [InlineData("isActive")] [InlineData("taxIdentityNumber")] [InlineData("approved")]
    public void Unknown_identity_activation_and_approval_fields_are_not_a_creation_need_contract(string field)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var node = JsonSerializer.SerializeToNode(new CounterpartyCreationNeedRequestV1("SENTETIK", null, Guid.NewGuid(), DateTimeOffset.UtcNow, null, null), options)!.AsObject();
        node[field] = JsonValue.Create("SENTETIK"); Assert.Throws<JsonException>(() => node.Deserialize<CounterpartyCreationNeedRequestV1>(options));
    }
    [Theory] [InlineData("assigneeUserId")] [InlineData("dueAtUtc")] [InlineData("recordType")] [InlineData("recordId")]
    public void Recipient_deadline_and_explicit_nullable_source_are_never_silently_defaulted(string field)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var node = JsonSerializer.SerializeToNode(new CounterpartyCreationNeedRequestV1("SENTETIK", null, Guid.NewGuid(), DateTimeOffset.UtcNow, null, null), options)!.AsObject();
        Assert.True(node.Remove(field)); Assert.Throws<JsonException>(() => node.Deserialize<CounterpartyCreationNeedRequestV1>(options));
    }
}
