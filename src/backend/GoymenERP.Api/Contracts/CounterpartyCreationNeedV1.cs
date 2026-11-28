using System.Text.Json.Serialization;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.Contracts;

/// <summary>
/// A request to an existing authorized person to create a counterparty later. These are the existing Work task's fields,
/// not a Party/account command or a placeholder Party identity. Company and assigner come only from the server context.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CounterpartyCreationNeedRequestV1(
    [property: JsonRequired] string Title,
    [property: JsonRequired] string? Details,
    [property: JsonRequired] Guid AssigneeUserId,
    [property: JsonRequired] DateTimeOffset DueAtUtc,
    [property: JsonRequired] string? RecordType,
    [property: JsonRequired] Guid? RecordId)
{
    internal CreateWorkTaskRequestV1 ToWorkTask() => new(Title, Details, AssigneeUserId, DueAtUtc, RecordType, RecordId);
}
