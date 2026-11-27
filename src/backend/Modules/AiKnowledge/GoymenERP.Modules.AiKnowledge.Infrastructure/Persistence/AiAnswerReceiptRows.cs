namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

internal sealed class AiAnswerReceiptRow
{
    public Guid CompanyId { get; set; }
    public Guid AnswerId { get; set; }
    public string AnswerSha256 { get; set; } = "";
    public string CitationsSha256 { get; set; } = "";
    public string Feature { get; set; } = "";
    public string ModelDeployment { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset RetainUntilUtc { get; set; }
}
internal sealed class AiAnswerReceiptIdentityRow
{
    public Guid CompanyId { get; set; }
    public Guid AnswerId { get; set; }
    public string ActorId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
}
