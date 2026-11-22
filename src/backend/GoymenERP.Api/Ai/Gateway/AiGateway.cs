using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai.Gateway;

/// <summary>
/// WP-A1 AI gateway (<see cref="AiGatewayContractV1.Version"/>). The order is fixed and every step is fail-closed:
/// <list type="number">
/// <item>Runtime ready (<c>Ai:Enabled</c>, provider admitted, limits bound) — otherwise <see cref="AiGatewayStatusV1.RuntimeUnavailable"/>.</item>
/// <item>Request shape: server caller, defined feature/task/data class, bounded instruction; general chat carries no grounding
/// and only the <see cref="AiTaskKindV1.Converse"/> task, every other feature never uses that task.</item>
/// <item>The company's policy in force (Settings, versioned): the feature switch must be on.</item>
/// <item>No source → "cannot answer" (no model call, no budget) except general chat.</item>
/// <item>Effective data class = highest of the content class and every source; above the runtime cap
/// (<c>Ai:MaxDataClass</c>) or without an exact task × data class route, or a route the fixed placement rules forbid
/// (cloud above Internal; document/e-mail text on a cloud model) → <see cref="AiGatewayStatusV1.DataClassRefused"/>.</item>
/// <item>A registered composer for the runtime provider kind and the route placement, else unavailable.</item>
/// <item>Budget reservation in the Settings ledger against the version routed with; closed or reached → <see cref="AiGatewayStatusV1.BudgetExhausted"/>.</item>
/// <item>Compose, settle the reported units (zero on failure), then check the text against the cited sources: unsupported
/// text is "cannot answer" (the units are still recorded).</item>
/// </list>
/// The gateway runs no tool and writes no business record; the caller owns authority, tool calls and security events.
/// </summary>
public sealed class AiGateway(
    IOptions<AiRuntimeOptions> options,
    IHostEnvironment environment,
    IAiModelProviderV1 provider,
    IEnumerable<IAiAnswerComposerV1> composers,
    ISettingsAiGatewayPolicyProviderV1 policies,
    ISettingsAiBudgetLedgerV1 ledger) : IAiGatewayV1
{
    private const int CallerTokenMaximumLength = 128;

    public async Task<AiGatewayResultV1> ComposeAsync(AiGatewayRequestV1 request, CancellationToken cancellationToken = default)
    {
        var state = AiRuntimeStateEvaluator.Evaluate(options, environment, provider);
        if (!state.IsReady || !AiRuntimeOptionsReader.TryRead(options, out var runtime))
            return AiGatewayResultV1.Of(AiGatewayStatusV1.RuntimeUnavailable);

        var limits = state.Limits!;
        if (!IsWellFormed(request, limits))
            return AiGatewayResultV1.Of(AiGatewayStatusV1.Invalid);

        var caller = request.Caller;
        SettingsAiGatewayPolicyV1 policy;
        try
        {
            policy = await policies.GetAiGatewayPolicyAsync(caller.CompanyId, cancellationToken);
        }
        catch (SettingsPrerequisiteException)
        {
            return AiGatewayResultV1.Of(AiGatewayStatusV1.Unavailable);
        }

        if (policy is null || policy.CompanyId != caller.CompanyId || !policy.EnabledFeatures.Contains(request.Feature))
            return AiGatewayResultV1.Of(AiGatewayStatusV1.FeatureDisabled);

        var chat = request.Feature == AiFeatureV1.Chat;
        if (!chat && request.Grounding.Count == 0)
            return AiGatewayResultV1.CannotAnswer(AiGatewayCannotAnswerReasonsV1.NoSource);

        var effective = request.Grounding.Select(item => item.Source.DataClass).Append(request.ContentDataClass).Max();
        var route = policy.Routes.SingleOrDefault(candidate => candidate.TaskKind == request.TaskKind && candidate.DataClass == effective);
        if (effective > limits.MaxDataClass
            || route is null
            || !AiGatewayPlacementRulesV1.PlacementAllows(route.Placement, effective)
            || !AiGatewayPlacementRulesV1.FeatureAllows(request.Feature, route.Placement))
            return AiGatewayResultV1.Of(AiGatewayStatusV1.DataClassRefused);

        var composer = composers.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderKind, runtime!.Provider, StringComparison.Ordinal) && candidate.Placement == route.Placement);
        if (composer is null)
            return AiGatewayResultV1.Of(AiGatewayStatusV1.Unavailable);

        SettingsAiBudgetReservationV1 reservation;
        try
        {
            reservation = await ledger.ReserveAsync(new SettingsAiBudgetReservationRequestV1(caller.CompanyId, policy.Version,
                request.Feature, request.TaskKind, route.Placement, route.ModelDeployment, caller.ActorId, caller.CorrelationId),
                cancellationToken);
        }
        catch (SettingsPrerequisiteException)
        {
            return AiGatewayResultV1.Of(AiGatewayStatusV1.Unavailable);
        }

        switch (reservation.Outcome)
        {
            case SettingsAiBudgetReservationOutcomeV1.Reserved when reservation.EntryId is not null:
                break;
            case SettingsAiBudgetReservationOutcomeV1.CeilingReached:
            case SettingsAiBudgetReservationOutcomeV1.BudgetClosed:
                return AiGatewayResultV1.Of(AiGatewayStatusV1.BudgetExhausted);
            default:
                return AiGatewayResultV1.Of(AiGatewayStatusV1.Unavailable);
        }

        var entryId = reservation.EntryId.Value;
        var sources = request.Grounding
            .Select((item, index) => new AiComposeSourceV1(index + 1, item.Source.Title, item.Source.SourceVersion,
                item.Source.AsOfUtc, item.ContentJson))
            .ToArray();
        AiComposedTextV1? composed;
        try
        {
            composed = await composer.ComposeAsync(
                new AiComposeRequestV1(route.ModelDeployment, request.TaskKind, request.Instruction, sources), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await TrySettleAsync(caller.CompanyId, entryId, 0);
            throw;
        }
        catch (Exception)
        {
            await TrySettleAsync(caller.CompanyId, entryId, 0);
            return AiGatewayResultV1.Of(AiGatewayStatusV1.Unavailable);
        }

        var units = composed is null ? 0 : Math.Max(0, composed.UsageUnits);
        if (!await TrySettleAsync(caller.CompanyId, entryId, units))
            return AiGatewayResultV1.Of(AiGatewayStatusV1.Unavailable);

        if (chat)
        {
            return string.IsNullOrWhiteSpace(composed?.Text)
                ? AiGatewayResultV1.Of(AiGatewayStatusV1.Unavailable)
                : new AiGatewayResultV1(AiGatewayStatusV1.Answered,
                    new AiGatewayAnswerV1(composed.Text, Grounded: false, [], route.ModelDeployment, route.Placement,
                        policy.VersionText, units), null);
        }

        if (!AiGroundingValidator.IsSupported(composed, sources))
            return AiGatewayResultV1.CannotAnswer(AiGatewayCannotAnswerReasonsV1.Unsupported);

        var citations = composed!.CitedSources.Order()
            .Select(number => request.Grounding[number - 1])
            .Select(item => new AiCitationV1(item.ToolId, item.ToolVersion, item.Source.TargetKind, item.Source.TargetId,
                item.Source.Title, item.Source.SourceVersion, item.Source.AsOfUtc))
            .ToArray();
        return new AiGatewayResultV1(AiGatewayStatusV1.Answered,
            new AiGatewayAnswerV1(composed.Text, Grounded: true, citations, route.ModelDeployment, route.Placement,
                policy.VersionText, units), null);
    }

    private async Task<bool> TrySettleAsync(Guid companyId, Guid entryId, int units)
    {
        try
        {
            // Settlement is not cancelled with the request: the reservation must be closed with what was used.
            await ledger.SettleAsync(companyId, entryId, units, CancellationToken.None);
            return true;
        }
        catch (SettingsPrerequisiteException)
        {
            // The reservation stays counted in full (conservative); nothing is released.
            return false;
        }
    }

    private static bool IsWellFormed(AiGatewayRequestV1? request, AiRuntimeLimits limits)
    {
        if (request?.Caller is not { } caller
            || caller.CompanyId == Guid.Empty
            || !IsToken(caller.ActorId)
            || !IsToken(caller.CorrelationId)
            || !Enum.IsDefined(request.Feature)
            || !Enum.IsDefined(request.TaskKind)
            || !Enum.IsDefined(request.ContentDataClass)
            || string.IsNullOrWhiteSpace(request.Instruction)
            || request.Instruction.Length > limits.MaxQuestionLength
            || request.Grounding is null
            || request.Grounding.Count > limits.MaxToolCallsPerRequest)
            return false;

        var chat = request.Feature == AiFeatureV1.Chat;
        if (chat != (request.TaskKind == AiTaskKindV1.Converse) || (chat && request.Grounding.Count > 0))
            return false;

        return request.Grounding.All(item => item?.Source is { } source
            && !string.IsNullOrWhiteSpace(item.ContentJson)
            && string.Equals(source.ToolId, item.ToolId, StringComparison.Ordinal)
            && string.Equals(source.ToolVersion, item.ToolVersion, StringComparison.Ordinal)
            && Enum.IsDefined(source.DataClass));
    }

    private static bool IsToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= CallerTokenMaximumLength;
}
