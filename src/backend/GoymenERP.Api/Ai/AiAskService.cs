using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

/// <summary>
/// AI-3 READ-only ask runtime. The policy order is ERP-000J §8: actor, server company context (fail-closed),
/// AI capability, tool allowlist, risk/permission, fresh authority, input schema/size/canonicalization, data class,
/// owner READ through the owner port, security events. Tool arguments never set the company: the owner tool receives
/// the server-built <see cref="AiServerContextV1"/>. Every call and every refusal is a security event (Group E):
/// <c>ai.tool.called</c> Started is fail-closed, Completed and the refusals are fail-open with alarm. Every event carries
/// the published AI policy reference, or <c>default-deny</c> when none is published (ASSUMPTION-AI-POLICYREF-1).
/// WP-A1: after the fence, the released tool results go to the AI gateway (<see cref="IAiGatewayV1"/>, feature Panel, task
/// Answer), which composes a natural language answer whose every number comes from a cited source, or says it cannot answer.
/// A gateway refusal (feature switch, budget, data class route) is recorded as an <c>ai.request.refused</c> event.
/// </summary>
public sealed class AiAskService(
    IOptions<AiRuntimeOptions> options,
    IHostEnvironment environment,
    IAiModelProviderV1 provider,
    AiToolCatalog catalog,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorityRevalidationService authority,
    IActorSessionBindingResolver sessionBindingResolver,
    ICorrelationContext correlationContext,
    ISecurityEventWriterV1 securityEvents,
    TimeProvider timeProvider,
    IAiGatewayV1 gateway)
{
    private const string CompanyArgumentName = "companyId";

    public async Task<AiAskResultV1> AskAsync(AiAskRequestV1? request, CancellationToken cancellationToken = default)
    {
        // Without correlation metadata no security event can be written, so nothing may run.
        if (!correlationContext.IsResolved)
        {
            return AiAskResultV1.Of(AiAskStatusV1.Unavailable);
        }

        var actorId = ResolveActorId();
        var state = AiRuntimeStateEvaluator.Evaluate(options, environment, provider);
        if (!state.IsReady)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused,
                ResolvedCompanyOrNull(actorId), actorId, null, null, cancellationToken);
            return AiAskResultV1.Of(state.Availability == AiRuntimeAvailability.Disabled
                ? AiAskStatusV1.Disabled
                : AiAskStatusV1.Unavailable);
        }

        var limits = state.Limits!;
        if (actorId is null)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.SessionInvalid,
                null, null, null, null, cancellationToken);
            return AiAskResultV1.Of(AiAskStatusV1.Refused);
        }

        // Server company context only; the provider is never called without it.
        if (ResolvedCompanyOrNull(actorId) is not { } companyId)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.CompanyContextMismatch,
                null, actorId, null, null, cancellationToken);
            return AiAskResultV1.Of(AiAskStatusV1.Refused);
        }

        var panelScope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.AiPanelUse, companyId, cancellationToken);
        if (!IsCompanyWideGrant(panelScope))
        {
            await RefuseAsync(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventReasonCodesV1.PermissionMissing,
                companyId, actorId, ErpPermissions.AiPanelUse, null, cancellationToken);
            return AiAskResultV1.Of(AiAskStatusV1.Refused);
        }

        if (!TryReadQuestion(request, limits, out var question) || !TryReadHint(request, out var hint))
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused,
                companyId, actorId, null, null, cancellationToken);
            return AiAskResultV1.InvalidInput(question is null ? AiInvalidFieldsV1.Question : AiInvalidFieldsV1.Tool);
        }

        AiModelPlanV1 plan;
        try
        {
            var available = catalog.Descriptors
                .Where(descriptor => descriptor.DataClass <= limits.MaxDataClass)
                .Select(descriptor => new AiModelToolV1(descriptor.ToolId, descriptor.Version, descriptor.DataClass, descriptor.FieldAllowlist))
                .ToArray();
            plan = await provider.PlanAsync(new AiModelRequestV1(question, available, hint), cancellationToken);
        }
        catch (AiExternalEgressRefusedException)
        {
            // AI-D4: the question holds a sensitive value or the Settings policy keeps it in; nothing was sent.
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused,
                companyId, actorId, null, null, cancellationToken);
            return AiAskResultV1.Of(AiAskStatusV1.Refused);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiProviderFailed,
                companyId, actorId, null, null, cancellationToken);
            return AiAskResultV1.Of(AiAskStatusV1.Unavailable);
        }

        // Model output is untrusted: a missing plan or too many calls is refused before any tool runs.
        if (plan?.ToolCalls is null
            || plan.ToolCalls.Any(call => call is null)
            || plan.ToolCalls.Count > limits.MaxToolCallsPerRequest)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused,
                companyId, actorId, null, null, cancellationToken);
            return AiAskResultV1.Of(AiAskStatusV1.Refused);
        }

        var results = new List<AiToolResultV1>(plan.ToolCalls.Count);
        var grantedRoleCodes = new SortedSet<string>(panelScope.RoleCodes, StringComparer.Ordinal);
        var usedTools = new List<AiToolDescriptorV1>(plan.ToolCalls.Count);
        foreach (var call in plan.ToolCalls)
        {
            var outcome = await RunToolAsync(call, companyId, actorId, limits, grantedRoleCodes, cancellationToken);
            if (outcome.Failure is not null)
            {
                return outcome.Failure;
            }

            results.Add(outcome.Result!);
            usedTools.Add(outcome.Tool!);
        }

        // Fence: nothing is released if the actor, company or authority changed while the tools ran.
        if (!await RemainsCurrentAsync(actorId, companyId, usedTools, cancellationToken))
        {
            await RefuseAsync(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventReasonCodesV1.PermissionMissing,
                companyId, actorId, ErpPermissions.AiPanelUse, null, cancellationToken);
            return AiAskResultV1.Of(AiAskStatusV1.Refused);
        }

        return await ComposeAnswerAsync(question, results, companyId, actorId, cancellationToken);
    }

    /// <summary>
    /// WP-A1: turns the released results into text through the gateway. The question is classified Internal (ERP panel
    /// context); each source carries its owner's data class, so the gateway routes by the highest of them (AI-D34 d).
    /// </summary>
    private async Task<AiAskResultV1> ComposeAnswerAsync(
        string question,
        IReadOnlyList<AiToolResultV1> results,
        Guid companyId,
        string actorId,
        CancellationToken cancellationToken)
    {
        var grounding = new List<AiGroundingItemV1>(results.Count);
        foreach (var result in results)
        {
            if (result.Outcome != AiToolResultOutcomesV1.Succeeded || result.Source is not { } source || result.ContentJson is null
                || !Enum.TryParse<AiDataClassV1>(source.DataClass, ignoreCase: false, out var dataClass))
            {
                continue;
            }

            grounding.Add(new AiGroundingItemV1(result.ToolId, result.ToolVersion, result.ContentJson,
                new AiSourceV1(result.ToolId, result.ToolVersion, source.TargetKind, source.TargetId, source.Title,
                    source.SourceVersion, source.AsOfUtc, dataClass)));
        }

        var composed = await gateway.ComposeAsync(
            new AiGatewayRequestV1(AiFeatureV1.Panel, AiTaskKindV1.Answer, question, AiDataClassV1.Internal, grounding,
                new AiGatewayCallerV1(companyId, actorId, correlationContext.CorrelationId, correlationContext.CausationId)),
            cancellationToken);

        switch (composed.Status)
        {
            case AiGatewayStatusV1.Answered when composed.Answer is { } answer:
                return AiAskResultV1.Answered(new AiAskResponseV1(AiAskOutcomesV1.Answered, results,
                    new AiAnswerV1(answer.Text,
                        answer.Citations.Select(citation => new AiAnswerCitationV1(citation.ToolId, citation.ToolVersion,
                            citation.TargetKind, citation.TargetId, citation.Title, citation.SourceVersion, citation.AsOfUtc)).ToArray(),
                        answer.ModelDeployment, answer.PolicyVersion)));
            case AiGatewayStatusV1.CannotAnswer:
                return AiAskResultV1.Answered(new AiAskResponseV1(AiAskOutcomesV1.CannotAnswer, results, null));
            case AiGatewayStatusV1.FeatureDisabled:
                await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused,
                    companyId, actorId, null, null, cancellationToken);
                return AiAskResultV1.Of(AiAskStatusV1.FeatureDisabled);
            case AiGatewayStatusV1.BudgetExhausted:
                await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused,
                    companyId, actorId, null, null, cancellationToken);
                return AiAskResultV1.Of(AiAskStatusV1.BudgetExhausted);
            case AiGatewayStatusV1.DataClassRefused:
                await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiDataClassNotAllowed,
                    companyId, actorId, null, null, cancellationToken);
                return AiAskResultV1.Of(AiAskStatusV1.Refused);
            default:
                await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiProviderFailed,
                    companyId, actorId, null, null, cancellationToken);
                return AiAskResultV1.Of(AiAskStatusV1.Unavailable);
        }
    }

    private async Task<ToolRun> RunToolAsync(
        AiPlannedToolCallV1 call,
        Guid companyId,
        string actorId,
        AiRuntimeLimits limits,
        SortedSet<string> grantedRoleCodes,
        CancellationToken cancellationToken)
    {
        var tool = catalog.Find(call.ToolId, call.ToolVersion);
        if (tool is null)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiToolNotAllowed,
                companyId, actorId, null, null, cancellationToken);
            return ToolRun.Failed(AiAskResultV1.Of(AiAskStatusV1.Refused));
        }

        var descriptor = tool.Descriptor;
        foreach (var permission in descriptor.RequiredPermissions)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
            if (!IsCompanyWideGrant(scope))
            {
                await RefuseAsync(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventReasonCodesV1.PermissionMissing,
                    companyId, actorId, permission, descriptor, cancellationToken);
                return ToolRun.Failed(AiAskResultV1.Of(AiAskStatusV1.Refused));
            }

            grantedRoleCodes.UnionWith(scope.RoleCodes);
        }

        if (!TryCanonicalizeArguments(call.ArgumentsJson, descriptor, limits, out var argumentsJson))
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused,
                companyId, actorId, null, descriptor, cancellationToken);
            return ToolRun.Failed(AiAskResultV1.InvalidInput(AiInvalidFieldsV1.Arguments));
        }

        if (descriptor.DataClass > limits.MaxDataClass)
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiDataClassNotAllowed,
                companyId, actorId, null, descriptor, cancellationToken);
            return ToolRun.Failed(AiAskResultV1.Of(AiAskStatusV1.Refused));
        }

        var sessionId = await sessionBindingResolver.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await RefuseAsync(SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.SessionInvalid,
                companyId, actorId, null, descriptor, cancellationToken);
            return ToolRun.Failed(AiAskResultV1.Of(AiAskStatusV1.Refused));
        }

        var context = new AiServerContextV1(
            companyId,
            actorId,
            sessionId,
            ScopeRevision: $"company:{companyId:D}",
            AuthorityRevision: AuthorityRevisionOf(grantedRoleCodes),
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            ModelDeployment: null,
            PromptPolicyVersion: null);

        // Group E: the call is recorded before the owner tool runs; if it cannot be recorded, the tool does not run.
        var started = await securityEvents.WriteAsync(
            NewEvent(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Started, SecurityEventOutcomesV1.Allowed,
                null, SecurityEventSeveritiesV1.Info, companyId, actorId, null, descriptor),
            cancellationToken);
        if (!started.Recorded || !started.CallerMayContinue)
        {
            return ToolRun.Failed(AiAskResultV1.Of(AiAskStatusV1.Unavailable));
        }

        AiReadResultV1? read;
        try
        {
            read = await tool.ReadAsync(new AiToolInputV1(argumentsJson), context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            read = null;
        }

        var released = read is not null
            && (read.Outcome == AiToolOutcomeV1.NotFound
                || (read.Outcome == AiToolOutcomeV1.Succeeded && SourceIsReleasable(read.Source, descriptor, limits)));
        await securityEvents.WriteAsync(
            NewEvent(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Completed,
                released ? SecurityEventOutcomesV1.Allowed : SecurityEventOutcomesV1.Failed,
                released ? null : SecurityEventReasonCodesV1.OperationFailed,
                released ? SecurityEventSeveritiesV1.Info : SecurityEventSeveritiesV1.Warning,
                companyId, actorId, null, descriptor),
            cancellationToken);

        if (!released)
        {
            return ToolRun.Failed(read?.Outcome switch
            {
                AiToolOutcomeV1.Denied => AiAskResultV1.Of(AiAskStatusV1.Refused),
                AiToolOutcomeV1.Invalid => AiAskResultV1.InvalidInput(AiInvalidFieldsV1.Arguments),
                _ => AiAskResultV1.Of(AiAskStatusV1.ToolUnavailable),
            });
        }

        var result = read!.Outcome == AiToolOutcomeV1.Succeeded
            ? new AiToolResultV1(descriptor.ToolId, descriptor.Version, AiToolResultOutcomesV1.Succeeded, read.ContentJson,
                read.Source is { } source
                    ? new AiToolSourceV1(source.TargetKind, source.TargetId, source.Title, source.SourceVersion,
                        source.AsOfUtc, source.DataClass.ToString())
                    : null)
            : new AiToolResultV1(descriptor.ToolId, descriptor.Version, AiToolResultOutcomesV1.NotFound, null, null);
        return new ToolRun(result, descriptor, null);
    }

    private async Task<bool> RemainsCurrentAsync(
        string actorId,
        Guid companyId,
        IReadOnlyList<AiToolDescriptorV1> usedTools,
        CancellationToken cancellationToken)
    {
        if (ResolveActorId() != actorId || ResolvedCompanyOrNull(actorId) != companyId)
        {
            return false;
        }

        var permissions = usedTools
            .SelectMany(descriptor => descriptor.RequiredPermissions)
            .Prepend(ErpPermissions.AiPanelUse)
            .Distinct(StringComparer.Ordinal);
        foreach (var permission in permissions)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
            if (!IsCompanyWideGrant(scope))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SourceIsReleasable(AiSourceV1? source, AiToolDescriptorV1 descriptor, AiRuntimeLimits limits) =>
        source is null
        || (string.Equals(source.ToolId, descriptor.ToolId, StringComparison.Ordinal)
            && string.Equals(source.ToolVersion, descriptor.Version, StringComparison.Ordinal)
            && source.DataClass <= descriptor.DataClass
            && source.DataClass <= limits.MaxDataClass);

    private static bool IsCompanyWideGrant(PermissionScope? scope) => scope is { IsGranted: true, IsCompanyWide: true };

    private static bool TryReadQuestion(AiAskRequestV1? request, AiRuntimeLimits limits, [NotNullWhen(true)] out string? question)
    {
        var trimmed = request?.Question?.Trim();
        question = string.IsNullOrEmpty(trimmed) || trimmed.Length > limits.MaxQuestionLength ? null : trimmed;
        return question is not null;
    }

    private static bool TryReadHint(AiAskRequestV1? request, out AiToolHintV1? hint)
    {
        hint = null;
        if (request is null || (request.ToolId is null && request.ToolVersion is null && request.Arguments is null))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.ToolId) || string.IsNullOrWhiteSpace(request.ToolVersion))
        {
            return false;
        }

        if (request.Arguments is { } arguments && arguments.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        hint = new AiToolHintV1(request.ToolId, request.ToolVersion, request.Arguments?.GetRawText() ?? "{}");
        return true;
    }

    /// <summary>
    /// Canonical JSON object within the configured byte budget, with only the owner's allowlisted top-level fields.
    /// A company field is refused outright: the company comes from the server context, never from arguments.
    /// </summary>
    private static bool TryCanonicalizeArguments(
        string? argumentsJson,
        AiToolDescriptorV1 descriptor,
        AiRuntimeLimits limits,
        out string canonical)
    {
        canonical = string.Empty;
        if (argumentsJson is null || Encoding.UTF8.GetByteCount(argumentsJson) > limits.MaxArgumentBytes)
        {
            return false;
        }

        try
        {
            canonical = AiCanonicalJson.Canonicalize(argumentsJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(canonical) > limits.MaxArgumentBytes)
        {
            return false;
        }

        using var document = JsonDocument.Parse(canonical);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, CompanyArgumentName, StringComparison.OrdinalIgnoreCase)
                || !descriptor.FieldAllowlist.Contains(property.Name, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string AuthorityRevisionOf(IEnumerable<string> roleCodes)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', roleCodes)));
        return $"roles:{Convert.ToHexStringLower(digest)}";
    }

    private string? ResolveActorId() =>
        currentUser.IsAuthenticated && Guid.TryParse(currentUser.UserId, out var userId) && userId != Guid.Empty
            ? userId.ToString("D")
            : null;

    private Guid? ResolvedCompanyOrNull(string? actorId) =>
        actorId is not null
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
            ? companyContext.CompanyId
            : null;

    private async Task RefuseAsync(
        string eventType,
        string reasonCode,
        Guid? companyId,
        string? actorId,
        string? permissionCode,
        AiToolDescriptorV1? tool,
        CancellationToken cancellationToken)
    {
        // Refusals are fail-open with alarm: the refusal stands whether or not the event was stored.
        await securityEvents.WriteAsync(
            NewEvent(eventType, SecurityEventPhasesV1.Single, SecurityEventOutcomesV1.Denied, reasonCode,
                SecurityEventSeveritiesV1.Warning, companyId, actorId, permissionCode, tool),
            cancellationToken);
    }

    private SecurityEventV1 NewEvent(
        string eventType,
        string phase,
        string outcome,
        string? reasonCode,
        string severity,
        Guid? companyId,
        string? actorId,
        string? permissionCode,
        AiToolDescriptorV1? tool)
    {
        var policy = AiPolicyReferenceV1.Resolve(AiRuntimeOptionsReader.TryRead(options, out var value) ? value : null);
        return new SecurityEventV1
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
            ScopeKind = companyId is null ? SecurityEventScopeKindsV1.Platform : SecurityEventScopeKindsV1.Company,
            CompanyId = companyId,
            ActorType = actorId is null ? SecurityEventActorTypesV1.Anonymous : SecurityEventActorTypesV1.User,
            ActorId = actorId,
            EventType = eventType,
            Phase = phase,
            Outcome = outcome,
            ReasonCode = reasonCode,
            Severity = severity,
            PermissionCode = permissionCode,
            PolicyId = policy.PolicyId,
            PolicyVersion = policy.PolicyVersion,
            CorrelationId = correlationContext.CorrelationId,
            CausationId = correlationContext.CausationId,
            SourceChannel = SourceChannels.AiAssisted,
            AiToolId = tool?.ToolId,
            AiToolVersion = tool?.Version,
            AiRiskClass = tool?.Risk,
            AiDataClass = tool?.DataClass,
        };
    }

    private sealed record ToolRun(AiToolResultV1? Result, AiToolDescriptorV1? Tool, AiAskResultV1? Failure)
    {
        public static ToolRun Failed(AiAskResultV1 failure) => new(null, null, failure);
    }
}
