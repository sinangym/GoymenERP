namespace GoymenERP.BuildingBlocks.Ai;

public enum AiToolRiskClassV1 { Read = 1, Draft = 2, Simulate = 3, Write = 4 }
public enum AiDataClassV1 { Public = 1, Internal = 2, Confidential = 3, StrictlyConfidential = 4 }
public enum AiToolOutcomeV1 { Succeeded = 1, NotFound = 2, Denied = 3, Invalid = 4, Conflict = 5, Unavailable = 6 }
public enum AiCommandOutcomeV1 { Applied = 1, Refused = 2, Unknown = 3, NotRecorded = 4 }

/// <summary>Opaque reviewed identities, not schemas, budget values, policies or registry admission.</summary>
public sealed record AiContractRevisionV1(string InputSchema, string OutputSchema, string FieldPolicy,
    string LimitPolicy, string Catalogue, string AuthorizationPolicy, string SourceContract, string RedactionPolicy)
{
    internal void RequireBound()
    {
        foreach (var value in new[] { InputSchema, OutputSchema, FieldPolicy, LimitPolicy, Catalogue,
                     AuthorizationPolicy, SourceContract, RedactionPolicy }) AiContractGuard.Text(value);
    }
}

public sealed record AiToolDescriptorV1
{
    public string ToolId { get; }
    public string Version { get; }
    public string OwnerModule { get; }
    public AiToolRiskClassV1 Risk { get; }
    public AiDataClassV1 DataClass { get; }
    public IReadOnlyList<string> RequiredPermissions { get; }
    public IReadOnlyList<string> FieldAllowlist { get; }
    public string? OwnerCommandName { get; }
    public AiContractRevisionV1 Contract { get; }

    public AiToolDescriptorV1(string toolId, string version, string ownerModule, AiToolRiskClassV1 risk,
        AiDataClassV1 dataClass, IEnumerable<string> requiredPermissions, IEnumerable<string> fieldAllowlist,
        string? ownerCommandName, AiContractRevisionV1 contract)
    {
        if (!IsWellFormedToolId(toolId)) throw new ArgumentException("Malformed tool identity.", nameof(toolId));
        if (!Enum.IsDefined(risk) || !Enum.IsDefined(dataClass)) throw new ArgumentException("Unknown classification.");
        if ((risk == AiToolRiskClassV1.Write) != (ownerCommandName is not null))
            throw new ArgumentException("Only a future write descriptor names an owner command.");
        ToolId = toolId; Version = AiContractGuard.Text(version); OwnerModule = AiContractGuard.Text(ownerModule);
        Risk = risk; DataClass = dataClass; OwnerCommandName = ownerCommandName is null ? null : AiContractGuard.Text(ownerCommandName);
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Contract.RequireBound();
        RequiredPermissions = AiContractGuard.Names(requiredPermissions);
        FieldAllowlist = AiContractGuard.Names(fieldAllowlist);
    }

    public static bool IsWellFormedToolId(string? id) => id is { Length: > 3 and <= 96 }
        && System.Text.RegularExpressions.Regex.IsMatch(id,
            "\\A[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+\\.v[1-9][0-9]*\\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

/// <summary>Only the server may supply these values. Constructing a DTO proves no authority.</summary>
public sealed record AiServerContextV1(Guid CompanyId, string ActorId, string SessionId, string ScopeRevision,
    string AuthorityRevision, string CorrelationId, string? CausationId, string? ModelDeployment, string? PromptPolicyVersion)
{
    internal void RequireBound()
    {
        AiContractGuard.Id(CompanyId);
        foreach (var value in new[] { ActorId, SessionId, ScopeRevision, AuthorityRevision, CorrelationId }) AiContractGuard.Text(value);
        foreach (var value in new[] { CausationId, ModelDeployment, PromptPolicyVersion })
            if (value is not null) AiContractGuard.Text(value);
    }
}

public sealed record AiToolInputV1(string ArgumentsJson); // Untrusted; owner strict schema/authorization still required.
public sealed record AiSourceV1(string ToolId, string ToolVersion, string TargetKind, Guid TargetId,
    string Title, string SourceVersion, DateTimeOffset AsOfUtc, AiDataClassV1 DataClass);

public sealed record AiPreviewV1
{
    public Guid Id { get; }
    public string TargetKind { get; }
    public Guid TargetId { get; }
    public string TargetVersion { get; }
    public string CanonicalCommandJson { get; }
    public string DifferenceJson { get; }
    public IReadOnlyList<AiSourceV1> Sources { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public AiPreviewV1(Guid id, string targetKind, Guid targetId, string targetVersion, string commandJson,
        string differenceJson, IEnumerable<AiSourceV1> sources, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        AiContractGuard.Id(id); AiContractGuard.Id(targetId);
        AiContractGuard.Utc(createdAtUtc); AiContractGuard.Utc(expiresAtUtc);
        if (expiresAtUtc <= createdAtUtc) throw new ArgumentException("Preview expiry must follow creation.");
        Id = id; TargetKind = AiContractGuard.Text(targetKind); TargetId = targetId;
        TargetVersion = AiContractGuard.Text(targetVersion);
        CanonicalCommandJson = AiCanonicalJson.Canonicalize(commandJson);
        DifferenceJson = AiCanonicalJson.Canonicalize(differenceJson);
        Sources = AiContractGuard.Sources(sources);
        CreatedAtUtc = createdAtUtc; ExpiresAtUtc = expiresAtUtc;
    }
}

public sealed record AiReadResultV1
{
    public AiToolOutcomeV1 Outcome { get; }
    public string? ContentJson { get; }
    public AiSourceV1? Source { get; }
    public IReadOnlyList<string> InvalidFields { get; }
    public AiReadResultV1(AiToolOutcomeV1 outcome, string? contentJson = null, AiSourceV1? source = null,
        IEnumerable<string>? invalidFields = null)
    {
        InvalidFields = AiContractGuard.Copy(invalidFields ?? []);
        AiContractGuard.Result(outcome, contentJson is not null || source is not null, InvalidFields);
        if (outcome == AiToolOutcomeV1.Succeeded && contentJson is null) throw new ArgumentException("Missing content.");
        Outcome = outcome; ContentJson = contentJson is null ? null : AiCanonicalJson.Canonicalize(contentJson);
        if (source is not null) AiContractGuard.Sources([source]);
        Source = source;
    }
}

public sealed record AiDraftResultV1
{
    public AiToolOutcomeV1 Outcome { get; }
    public string? DraftJson { get; }
    public IReadOnlyList<AiSourceV1> Sources { get; }
    public IReadOnlyList<string> InvalidFields { get; }
    public AiDraftResultV1(AiToolOutcomeV1 outcome, string? draftJson = null, IEnumerable<AiSourceV1>? sources = null,
        IEnumerable<string>? invalidFields = null)
    {
        Sources = AiContractGuard.Sources(sources ?? []); InvalidFields = AiContractGuard.Copy(invalidFields ?? []);
        AiContractGuard.Result(outcome, draftJson is not null || Sources.Count != 0, InvalidFields);
        if (outcome == AiToolOutcomeV1.Succeeded && draftJson is null) throw new ArgumentException("Missing draft.");
        Outcome = outcome; DraftJson = draftJson is null ? null : AiCanonicalJson.Canonicalize(draftJson);
    }
}

public sealed record AiPreviewResultV1
{
    public AiToolOutcomeV1 Outcome { get; }
    public AiPreviewV1? Preview { get; }
    public IReadOnlyList<string> InvalidFields { get; }
    public AiPreviewResultV1(AiToolOutcomeV1 outcome, AiPreviewV1? preview = null, IEnumerable<string>? invalidFields = null)
    {
        InvalidFields = AiContractGuard.Copy(invalidFields ?? []);
        AiContractGuard.Result(outcome, preview is not null, InvalidFields);
        if (outcome == AiToolOutcomeV1.Succeeded && preview is null) throw new ArgumentException("Missing preview.");
        Outcome = outcome; Preview = preview;
    }
}

public sealed record AiCommandContextV1
{
    public Guid PreviewId { get; }
    public string IdempotencyKey => "ai:" + PreviewId.ToString("D");
    public AiAuditContextV1 Audit { get; }
    public AiCommandContextV1(Guid previewId, AiAuditContextV1 audit)
    {
        AiContractGuard.Id(previewId);
        Audit = audit ?? throw new ArgumentNullException(nameof(audit));
        if (audit.PreviewId != previewId) throw new ArgumentException("Audit belongs to another preview.");
        PreviewId = previewId;
    }
}

/// <summary>One frozen owner request is used for both execution and result lookup, never a new key/body.</summary>
public sealed record AiOwnerCommandRequestV1
{
    public AiToolDescriptorV1 Tool { get; }
    public AiServerContextV1 ServerContext { get; }
    public AiPreviewV1 Preview { get; }
    public AiCommandContextV1 CommandContext { get; }
    public string PreviewIdentityJson { get; }
    public string RequestIdentityJson { get; }
    public AiOwnerCommandRequestV1(AiToolDescriptorV1 tool, AiServerContextV1 context, AiPreviewV1 preview,
        AiCommandContextV1 commandContext, string approvedPreviewIdentityJson)
    {
        ArgumentNullException.ThrowIfNull(commandContext);
        AiContractGuard.Text(commandContext.IdempotencyKey);
        PreviewIdentityJson = AiCanonicalJson.PreviewIdentity(context, tool, preview);
        if (tool.Risk != AiToolRiskClassV1.Write || commandContext.Audit is not { } audit
            || approvedPreviewIdentityJson != PreviewIdentityJson || commandContext.PreviewId != preview.Id
            || audit.PreviewId != preview.Id || audit.PreviewHash != AiCanonicalJson.PreviewHash(context, tool, preview)
            || audit.CompanyId != context.CompanyId || audit.ActorId != context.ActorId
            || audit.SessionId != context.SessionId || audit.CorrelationId != context.CorrelationId
            || audit.CausationId != context.CausationId || audit.ModelDeployment != context.ModelDeployment
            || audit.PromptPolicyVersion != context.PromptPolicyVersion
            || audit.ToolId != tool.ToolId || audit.ToolVersion != tool.Version
            || audit.ScopeRevision != context.ScopeRevision || audit.AuthorityRevision != context.AuthorityRevision
            || audit.Contract != tool.Contract)
            throw new ArgumentException("Approval metadata does not bind this owner request.");
        Tool = tool; ServerContext = context; Preview = preview; CommandContext = commandContext;
        // Include the complete approval/audit and key, not merely the preview digest or payload.
        RequestIdentityJson = AiCanonicalJson.Canonicalize(System.Text.Json.JsonSerializer.Serialize(new
        {
            format = "goymen.ai.owner-request.v1", previewIdentityJson = PreviewIdentityJson, commandContext,
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }
}

public sealed record AiCommandResultV1
{
    public AiCommandOutcomeV1 Outcome { get; }
    public string? ResultVersion { get; }
    public string? RefusalCode { get; }
    public bool IsReplay { get; }
    public AiCommandResultV1(AiCommandOutcomeV1 outcome, string? resultVersion = null, string? refusalCode = null, bool isReplay = false)
    {
        if (outcome is not (AiCommandOutcomeV1.Applied or AiCommandOutcomeV1.Refused or AiCommandOutcomeV1.Unknown)
            || (outcome == AiCommandOutcomeV1.Applied) != (resultVersion is not null)
            || (outcome == AiCommandOutcomeV1.Refused) != (refusalCode is not null)
            || (isReplay && outcome != AiCommandOutcomeV1.Applied)) throw new ArgumentException("Invalid owner execution result.");
        if (resultVersion is not null) AiContractGuard.Text(resultVersion);
        if (refusalCode is not null) AiContractGuard.Text(refusalCode);
        Outcome = outcome; ResultVersion = resultVersion; RefusalCode = refusalCode; IsReplay = isReplay;
    }
}

/// <summary>
/// Identity/provenance supplied by a trusted owner adapter, never synthesized from an unrelated result.
/// Construction validates shape only; it does not authenticate an owner or perform a durable lookup.
/// </summary>
public sealed record AiOwnerEvidenceBindingV1
{
    public string RequestIdentityJson { get; }
    public string IdempotencyKey { get; }
    public string OwnerModule { get; }
    public string OwnerCommandName { get; }
    public string SourceContractRevision { get; }
    public string OwnerEvidenceReference { get; }
    public DateTimeOffset ObservedAtUtc { get; }

    public AiOwnerEvidenceBindingV1(string requestIdentityJson, string idempotencyKey, string ownerModule,
        string ownerCommandName, string sourceContractRevision, string ownerEvidenceReference, DateTimeOffset observedAtUtc)
    {
        RequestIdentityJson = AiContractGuard.Text(requestIdentityJson);
        IdempotencyKey = AiContractGuard.Text(idempotencyKey); OwnerModule = AiContractGuard.Text(ownerModule);
        OwnerCommandName = AiContractGuard.Text(ownerCommandName);
        SourceContractRevision = AiContractGuard.Text(sourceContractRevision);
        OwnerEvidenceReference = AiContractGuard.Text(ownerEvidenceReference);
        AiContractGuard.Utc(observedAtUtc); ObservedAtUtc = observedAtUtc;
    }

    /// <summary>Exact structural binding only; the caller must authenticate the owner evidence source.</summary>
    public void RequireMatches(AiOwnerCommandRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RequestIdentityJson != request.RequestIdentityJson || IdempotencyKey != request.CommandContext.IdempotencyKey
            || OwnerModule != request.Tool.OwnerModule || OwnerCommandName != request.Tool.OwnerCommandName
            || SourceContractRevision != request.Tool.Contract.SourceContract
            || ObservedAtUtc < request.CommandContext.Audit.ApprovedAtUtc)
            throw new ArgumentException("Owner evidence does not bind this frozen request.");
    }
}

/// <summary>
/// A direct execute response must be bound to the actual request at construction, before a consumer
/// can observe its result. Lookup performs its own check as well; it is not the execute admission gate.
/// This structural check does not authenticate the owner or establish a real execution producer.
/// </summary>
public sealed record AiOwnerRecordedResultV1
{
    public AiOwnerEvidenceBindingV1 Binding { get; }
    public AiCommandResultV1 Result { get; }
    public AiOwnerRecordedResultV1(AiOwnerCommandRequestV1 request, AiOwnerEvidenceBindingV1 binding, AiCommandResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(request);
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Binding.RequireMatches(request);
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}

public enum AiOwnerObservationStateV1 { Absent = 1, Pending = 2, InFlight = 3, Uncertain = 4 }

/// <summary>An ordinary lookup observation, including a late absent row, supplies no no-effect finality.</summary>
public sealed record AiOwnerLookupObservationV1
{
    public AiOwnerEvidenceBindingV1 Binding { get; }
    public AiOwnerObservationStateV1 State { get; }
    public AiOwnerLookupObservationV1(AiOwnerEvidenceBindingV1 binding, AiOwnerObservationStateV1 state)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        if (!Enum.IsDefined(state)) throw new ArgumentException("Unknown owner observation.");
        State = state;
    }
}

public enum AiOwnerNoEffectConclusionV1 { DefinitivelyNotAdmittedAndCannotTakeEffect = 1 }

/// <summary>
/// An explicit owner-issued final conclusion for this exact command: it was not admitted and cannot
/// still take effect. A missing row, timeout, timestamp or arbitrary reference must never produce this
/// conclusion. No real producer/finality mechanism is supplied here; the future trusted owner adapter
/// must establish it from its own authoritative protocol, not from null FindCommand.
/// </summary>
public sealed record AiOwnerDefinitiveNoEffectEvidenceV1
{
    public AiOwnerEvidenceBindingV1 Binding { get; }
    public AiOwnerNoEffectConclusionV1 Conclusion { get; }
    public string OwnerFinalityReference { get; }
    public AiOwnerDefinitiveNoEffectEvidenceV1(AiOwnerEvidenceBindingV1 binding,
        AiOwnerNoEffectConclusionV1 conclusion, string ownerFinalityReference)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        if (conclusion != AiOwnerNoEffectConclusionV1.DefinitivelyNotAdmittedAndCannotTakeEffect)
            throw new ArgumentException("Explicit definitive owner no-effect conclusion required.");
        Conclusion = conclusion; OwnerFinalityReference = AiContractGuard.Text(ownerFinalityReference);
    }
}

public sealed record AiOwnerLookupResultV1
{
    public AiCommandOutcomeV1 Outcome { get; }
    public AiOwnerRecordedResultV1? RecordedResult { get; }
    public AiOwnerLookupObservationV1? Observation { get; }
    public AiOwnerDefinitiveNoEffectEvidenceV1? DefinitiveNoEffect { get; }
    public AiOwnerLookupResultV1(AiOwnerCommandRequestV1 request, AiOwnerRecordedResultV1? recordedResult = null,
        AiOwnerLookupObservationV1? observation = null, AiOwnerDefinitiveNoEffectEvidenceV1? definitiveNoEffect = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((recordedResult is null ? 0 : 1) + (observation is null ? 0 : 1) + (definitiveNoEffect is null ? 0 : 1) > 1)
            throw new ArgumentException("Ambiguous owner evidence.");
        recordedResult?.Binding.RequireMatches(request);
        observation?.Binding.RequireMatches(request);
        definitiveNoEffect?.Binding.RequireMatches(request);
        Outcome = recordedResult?.Result.Outcome
            ?? (definitiveNoEffect is null ? AiCommandOutcomeV1.Unknown : AiCommandOutcomeV1.NotRecorded);
        RecordedResult = recordedResult; Observation = observation; DefinitiveNoEffect = definitiveNoEffect;
    }
}

public interface IAiOwnerToolV1 { AiToolDescriptorV1 Descriptor { get; } }
public interface IAiOwnerReadToolV1 : IAiOwnerToolV1
{ Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default); }
public interface IAiOwnerDraftToolV1 : IAiOwnerToolV1
{ Task<AiDraftResultV1> DraftAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default); }
public interface IAiOwnerPreviewToolV1 : IAiOwnerToolV1
{ Task<AiPreviewResultV1> PreviewAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default); }
/// <summary>Future owner ports only: no implementation, registration, execution or resend policy is supplied.</summary>
public interface IAiOwnerCommandToolV1 : IAiOwnerPreviewToolV1
{
    Task<AiOwnerRecordedResultV1> ExecuteAsync(AiOwnerCommandRequestV1 request, CancellationToken cancellationToken = default);
    Task<AiOwnerLookupResultV1> FindResultAsync(AiOwnerCommandRequestV1 request, CancellationToken cancellationToken = default);
}

internal static class AiContractGuard
{
    internal static string Text(string value) { ArgumentException.ThrowIfNullOrWhiteSpace(value); return value; }
    internal static void Id(Guid id) { if (id == Guid.Empty) throw new ArgumentException("Empty identity."); }
    internal static void Utc(DateTimeOffset value) { if (value.Offset != TimeSpan.Zero) throw new ArgumentException("UTC required."); }
    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Any(value => value is null)) throw new ArgumentException("Null collection member.");
        return Array.AsReadOnly(copy);
    }
    internal static IReadOnlyList<string> Names(IEnumerable<string> values)
    {
        var copy = Copy(values).Select(Text).Order(StringComparer.Ordinal).ToArray();
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length) throw new ArgumentException("Duplicate metadata name.");
        return Array.AsReadOnly(copy);
    }
    internal static IReadOnlyList<AiSourceV1> Sources(IEnumerable<AiSourceV1> values)
    {
        var copy = Copy(values);
        foreach (var source in copy)
        {
            if (!AiToolDescriptorV1.IsWellFormedToolId(source.ToolId) || !Enum.IsDefined(source.DataClass))
                throw new ArgumentException("Invalid source identity.");
            Id(source.TargetId); Utc(source.AsOfUtc);
            foreach (var value in new[] { source.ToolVersion, source.TargetKind, source.Title, source.SourceVersion }) Text(value);
        }
        return copy;
    }
    internal static void Result(AiToolOutcomeV1 outcome, bool hasPayload, IReadOnlyList<string> fields)
    {
        if (!Enum.IsDefined(outcome) || (outcome != AiToolOutcomeV1.Succeeded && hasPayload)
            || (outcome != AiToolOutcomeV1.Invalid && fields.Count != 0))
            throw new ArgumentException("A non-success result cannot disclose content or sources.");
        foreach (var field in fields) Text(field);
    }
}
