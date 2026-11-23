using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>
/// WP-A2 evaluation runner (<see cref="AiEvalContractV1.Version"/>). For every item of the SENTETIK set and one candidate:
/// <list type="number">
/// <item>Classify the item (AI-D44, AI-D42). An item with personal data is <see cref="AiEvalVerdictV1.Withheld"/> for an
/// external candidate: neither its planner nor its gateway is called (YOL-D21 compares on items without personal data).</item>
/// <item>Plan with the candidate's planner (the same port the ask endpoint uses); the expected call is a hint only for an
/// oracle Fake candidate.</item>
/// <item>Run every planned catalog READ tool only when the item grants the permission (an out-of-authority item grants none;
/// a fixture without the permission grants none). A non-catalog call never runs.</item>
/// <item>Compose through the WP-A1 gateway (<see cref="IAiGatewayV1"/>, feature <see cref="AiFeatureV1.Panel"/>, task
/// <see cref="AiTaskKindV1.Answer"/>), with the item's data class as the content class.</item>
/// <item>Score the four criteria: source accuracy (every citation is a returned source and the expected sources are among
/// the cited content), correct answer, knowing "I cannot answer", and refusing an out-of-authority question.</item>
/// </list>
/// Gateway statuses other than <see cref="AiGatewayStatusV1.Answered"/> and <see cref="AiGatewayStatusV1.CannotAnswer"/>
/// fail the item (a closed feature or budget must not look like a correct refusal). No item is skipped or softened.
/// </summary>
public sealed partial class AiEvalRunner
{
    private readonly IAiEvalToolRunnerV1 tools;
    private readonly AiEvalDataClassifierV1 classifier;
    private readonly AiEvalScoringOptionsV1 scoring;

    public AiEvalRunner(IAiEvalToolRunnerV1 tools, AiEvalDataClassifierV1 classifier, AiEvalScoringOptionsV1? scoring = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(classifier);
        this.scoring = scoring ?? new AiEvalScoringOptionsV1();
        this.scoring.Validate();
        this.tools = tools;
        this.classifier = classifier;
    }

    public async Task<AiEvalReportV1> RunAsync(AiEvalSetV1 set, AiEvalCandidateV1 candidate, Guid companyId, string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));

        var results = new List<AiEvalItemResultV1>(set.Items.Count);
        foreach (var item in set.Items)
        {
            var caller = new AiGatewayCallerV1(companyId, actorId, $"ai-eval:{candidate.Name}:{item.Id}", null);
            results.Add(await RunItemAsync(item, candidate, caller, cancellationToken));
        }

        return new AiEvalReportV1(AiEvalContractV1.Version, set.Format, candidate.Name, candidate.Kind,
            candidate.PlanWithExpectedHint, results, Totals(results));
    }

    /// <summary>YOL-D21: run two candidates and compare them on the items neither withheld.</summary>
    public async Task<AiEvalComparisonV1> CompareAsync(AiEvalSetV1 set, AiEvalCandidateV1 first, AiEvalCandidateV1 second,
        Guid companyId, string actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (string.Equals(first.Name, second.Name, StringComparison.Ordinal))
            throw new ArgumentException("The two candidates must have different names.", nameof(second));

        var a = await RunAsync(set, first, companyId, actorId, cancellationToken);
        var b = await RunAsync(set, second, companyId, actorId, cancellationToken);
        var common = a.Items.Zip(b.Items)
            .Where(pair => pair.First.Verdict != AiEvalVerdictV1.Withheld && pair.Second.Verdict != AiEvalVerdictV1.Withheld)
            .ToArray();
        var differences = common
            .Where(pair => pair.First.Verdict != pair.Second.Verdict)
            .Select(pair => new AiEvalDifferenceV1(pair.First.ItemId, pair.First.Category, pair.First.Verdict, pair.Second.Verdict))
            .ToArray();
        return new AiEvalComparisonV1(AiEvalContractV1.Version, a, b, common.Length, set.Items.Count - common.Length,
            Totals(common.Select(pair => pair.First).ToArray()), Totals(common.Select(pair => pair.Second).ToArray()), differences);
    }

    private async Task<AiEvalItemResultV1> RunItemAsync(AiEvalItemV1 item, AiEvalCandidateV1 candidate, AiGatewayCallerV1 caller,
        CancellationToken cancellationToken)
    {
        var classification = classifier.Classify(item);
        if (candidate.Kind == AiEvalCandidateKindV1.External && classification.HoldsPersonalData)
            return Result(item, AiEvalVerdictV1.Withheld, [AiEvalReasonsV1.PersonalDataWithheld], new(null, null, null, null),
                classification.DataClass, 0, 0, null);

        var hint = candidate.PlanWithExpectedHint && item.ExpectedToolCall is { } expected
            ? new AiToolHintV1(expected.ToolId, expected.Version, expected.ArgumentsJson)
            : null;
        IReadOnlyList<AiPlannedToolCallV1> calls;
        try
        {
            var plan = await candidate.Planner.PlanAsync(new AiModelRequestV1(item.Question, tools.Catalog, hint), cancellationToken);
            calls = plan?.ToolCalls ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result(item, AiEvalVerdictV1.Failed, [AiEvalReasonsV1.PlanFailed], Failed(item), classification.DataClass, 0, 0, null);
        }

        var reasons = new List<string>();
        var granted = item.Category != AiEvalSetV1.CategoryOutOfAuthority && item.FixturePermission != AiEvalSetV1.PermissionMissing;
        var grounding = new List<AiGroundingItemV1>();
        foreach (var call in calls)
        {
            if (call is null || !tools.Catalog.Any(tool => string.Equals(tool.ToolId, call.ToolId, StringComparison.Ordinal)
                    && string.Equals(tool.Version, call.ToolVersion, StringComparison.Ordinal)))
            {
                AddOnce(reasons, AiEvalReasonsV1.ToolOutsideCatalog);
                continue;
            }

            if (!granted) continue;
            var read = await tools.ReadAsync(item, call, cancellationToken);
            if (read.Outcome == AiToolOutcomeV1.Succeeded && read.Source is { } source && read.ContentJson is { } content)
                grounding.Add(new AiGroundingItemV1(call.ToolId, call.ToolVersion, content, source));
        }

        AiGatewayResultV1 composed;
        try
        {
            composed = await candidate.Gateway.ComposeAsync(new AiGatewayRequestV1(AiFeatureV1.Panel, AiTaskKindV1.Answer,
                item.Question, classification.DataClass, grounding, caller), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            reasons.Add(AiEvalReasonsV1.GatewayFailed);
            return Result(item, AiEvalVerdictV1.Failed, reasons, Failed(item), classification.DataClass, calls.Count, grounding.Count, null);
        }

        var checks = Score(item, calls, grounding, composed, reasons);
        var passed = reasons.Count == 0 && new[] { checks.SourceAccurate, checks.AnswerCorrect, checks.AbstainedCorrectly,
            checks.RefusedCorrectly }.All(check => check is null or true);
        return Result(item, passed ? AiEvalVerdictV1.Passed : AiEvalVerdictV1.Failed, reasons, checks, classification.DataClass,
            calls.Count, grounding.Count, composed);
    }

    private AiEvalChecksV1 Score(AiEvalItemV1 item, IReadOnlyList<AiPlannedToolCallV1> calls, IReadOnlyList<AiGroundingItemV1> grounding,
        AiGatewayResultV1 composed, List<string> reasons)
    {
        if (composed.Status is not (AiGatewayStatusV1.Answered or AiGatewayStatusV1.CannotAnswer))
        {
            reasons.Add($"ag_gecidi_durumu:{composed.Status}");
            return Failed(item);
        }

        var answer = composed.Status == AiGatewayStatusV1.Answered ? composed.Answer : null;
        var cited = answer is null ? [] : CitedGrounding(answer, grounding, reasons);
        var text = answer is null ? string.Empty : AiDecisionIndex.Fold(answer.Text);

        switch (item.ExpectedBehavior)
        {
            case AiEvalExpectedBehaviorV1.SourcedAnswer:
            case AiEvalExpectedBehaviorV1.CurrentDecision:
            {
                if (answer is null)
                {
                    reasons.Add(calls.Count == 0 ? AiEvalReasonsV1.ToolNotPlanned : AiEvalReasonsV1.NotAnswered);
                    return new AiEvalChecksV1(null, false, null, null);
                }

                var sourceOk = !reasons.Contains(AiEvalReasonsV1.CitationNotReturned) && ExpectedSourcesCited(item, cited, reasons);
                var answerOk = ContainsIdentities(item, text, reasons);
                if (item.ExpectedBehavior == AiEvalExpectedBehaviorV1.SourcedAnswer && TermCoverage(item.ExpectedAnswerSummary, text)
                    < scoring.MinimumTermCoverage)
                {
                    reasons.Add(AiEvalReasonsV1.LowTermCoverage);
                    answerOk = false;
                }

                return new AiEvalChecksV1(sourceOk, answerOk, null, null);
            }

            case AiEvalExpectedBehaviorV1.Abstain:
            {
                if (answer is null) return new AiEvalChecksV1(null, null, true, null);
                if (item.ExpectedSourceIds.Count == 0)
                {
                    reasons.Add(AiEvalReasonsV1.AnsweredWithoutSettledSource);
                    return new AiEvalChecksV1(false, null, false, null);
                }

                var sourceOk = !reasons.Contains(AiEvalReasonsV1.CitationNotReturned) && ExpectedSourcesCited(item, cited, reasons);
                var open = item.ExpectedSourceIds.All(id => DecisionStatuses(cited, id)
                               .Any(status => scoring.OpenDecisionStatuses.Contains(status, StringComparer.Ordinal)))
                           && scoring.OpenDecisionMarkers.Any(marker => text.Contains(AiDecisionIndex.Fold(marker), StringComparison.Ordinal));
                if (!open) reasons.Add(AiEvalReasonsV1.OpenStatusNotStated);
                return new AiEvalChecksV1(sourceOk, null, open, null);
            }

            case AiEvalExpectedBehaviorV1.Refuse:
            {
                var refused = answer is null;
                if (!refused) reasons.Add(AiEvalReasonsV1.AnsweredWhenRefusalExpected);
                if (item.ExpectedToolCall is null && calls.Count > 0)
                {
                    reasons.Add(AiEvalReasonsV1.ToolPlannedOnRefusal);
                    refused = false;
                }

                if (grounding.Count > 0)
                {
                    reasons.Add(AiEvalReasonsV1.ContentReleasedOnRefusal);
                    refused = false;
                }

                return new AiEvalChecksV1(null, null, null, refused);
            }

            case AiEvalExpectedBehaviorV1.ToolResult:
            {
                if (answer is null)
                {
                    reasons.Add(calls.Count == 0 ? AiEvalReasonsV1.ToolNotPlanned : AiEvalReasonsV1.NotAnswered);
                    return new AiEvalChecksV1(null, false, null, null);
                }

                var expectedTool = item.ExpectedToolCall?.ToolId;
                var sourceOk = !reasons.Contains(AiEvalReasonsV1.CitationNotReturned)
                               && cited.Any(entry => string.Equals(entry.ToolId, expectedTool, StringComparison.Ordinal));
                if (!sourceOk) AddOnce(reasons, AiEvalReasonsV1.ExpectedSourceNotCited);

                bool answerOk;
                if (item.ExpectedToolResult?.PendingCount is { } count)
                {
                    answerOk = Numbers(answer.Text).Contains(count.ToString(CultureInfo.InvariantCulture));
                    if (!answerOk) reasons.Add(AiEvalReasonsV1.CountMissing);
                }
                else
                {
                    answerOk = scoring.IncompleteResultMarkers.Any(marker => text.Contains(AiDecisionIndex.Fold(marker), StringComparison.Ordinal));
                    if (!answerOk) reasons.Add(AiEvalReasonsV1.IncompleteNotStated);
                }

                return new AiEvalChecksV1(sourceOk, answerOk, null, null);
            }

            default:
                throw new InvalidOperationException("Unknown expected behaviour.");
        }
    }

    /// <summary>"Atıf yalnız dönen kaynağa": every citation must be one of the sources released in this run.</summary>
    private static List<AiGroundingItemV1> CitedGrounding(AiGatewayAnswerV1 answer, IReadOnlyList<AiGroundingItemV1> grounding,
        List<string> reasons)
    {
        var cited = new List<AiGroundingItemV1>();
        if (answer.Citations.Count == 0) AddOnce(reasons, AiEvalReasonsV1.CitationNotReturned);
        foreach (var citation in answer.Citations)
        {
            var match = grounding.FirstOrDefault(item =>
                string.Equals(item.ToolId, citation.ToolId, StringComparison.Ordinal)
                && string.Equals(item.ToolVersion, citation.ToolVersion, StringComparison.Ordinal)
                && item.Source.TargetId == citation.TargetId
                && string.Equals(item.Source.SourceVersion, citation.SourceVersion, StringComparison.Ordinal));
            if (match is null) AddOnce(reasons, AiEvalReasonsV1.CitationNotReturned);
            else cited.Add(match);
        }

        return cited;
    }

    private static bool ExpectedSourcesCited(AiEvalItemV1 item, IReadOnlyList<AiGroundingItemV1> cited, List<string> reasons)
    {
        var identities = cited.SelectMany(entry => DecisionIdentities(entry.ContentJson)).ToHashSet(StringComparer.Ordinal);
        var ok = item.ExpectedSourceIds.All(identities.Contains);
        if (!ok) AddOnce(reasons, AiEvalReasonsV1.ExpectedSourceNotCited);
        return ok;
    }

    private static bool ContainsIdentities(AiEvalItemV1 item, string foldedText, List<string> reasons)
    {
        var ok = item.ExpectedSourceIds.All(id => ContainsToken(foldedText, AiDecisionIndex.Fold(id)));
        if (!ok) reasons.Add(AiEvalReasonsV1.ExpectedIdentityMissing);
        return ok;
    }

    /// <summary>Decision identities in a decision lookup result: each record and every decision that updates it.</summary>
    private static IEnumerable<string> DecisionIdentities(string contentJson)
    {
        using var document = JsonDocument.Parse(contentJson);
        if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            return [];
        var identities = new List<string>();
        foreach (var record in records.EnumerateArray())
        {
            if (record.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) identities.Add(id.GetString()!);
            if (record.TryGetProperty("supersededBy", out var links) && links.ValueKind == JsonValueKind.Array)
            {
                identities.AddRange(links.EnumerateArray()
                    .Where(link => link.TryGetProperty("by", out var by) && by.ValueKind == JsonValueKind.String)
                    .Select(link => link.GetProperty("by").GetString()!));
            }
        }

        return identities;
    }

    private static IEnumerable<string> DecisionStatuses(IReadOnlyList<AiGroundingItemV1> cited, string decisionId)
    {
        foreach (var entry in cited)
        {
            using var document = JsonDocument.Parse(entry.ContentJson);
            if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array) continue;
            foreach (var record in records.EnumerateArray())
            {
                if (record.TryGetProperty("id", out var id) && id.GetString() == decisionId
                    && record.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
                    yield return status.GetString()!;
            }
        }
    }

    /// <summary>Share of the summary's content terms (folded, at least <see cref="AiEvalScoringOptionsV1.MinimumTermLength"/> letters) present in the answer.</summary>
    private double TermCoverage(string summary, string foldedText)
    {
        var terms = TermPattern().Matches(AiDecisionIndex.Fold(summary))
            .Select(match => match.Value)
            .Where(term => term.Length >= scoring.MinimumTermLength)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return terms.Length == 0 ? 1 : (double)terms.Count(term => foldedText.Contains(term, StringComparison.Ordinal)) / terms.Length;
    }

    private static bool ContainsToken(string foldedText, string foldedToken) =>
        Regex.IsMatch(foldedText, $"(?<![\\p{{L}}\\p{{N}}-]){Regex.Escape(foldedToken)}(?![\\p{{L}}\\p{{N}}])", RegexOptions.CultureInvariant);

    private static HashSet<string> Numbers(string text) =>
        NumberToken().Matches(CitationMarker().Replace(text, " ")).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    private static AiEvalChecksV1 Failed(AiEvalItemV1 item) => item.ExpectedBehavior switch
    {
        AiEvalExpectedBehaviorV1.Abstain => new AiEvalChecksV1(null, null, false, null),
        AiEvalExpectedBehaviorV1.Refuse => new AiEvalChecksV1(null, null, null, false),
        _ => new AiEvalChecksV1(null, false, null, null),
    };

    private static void AddOnce(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason)) reasons.Add(reason);
    }

    private static AiEvalItemResultV1 Result(AiEvalItemV1 item, AiEvalVerdictV1 verdict, IReadOnlyList<string> reasons,
        AiEvalChecksV1 checks, AiDataClassV1 dataClass, int plannedCalls, int releasedSources, AiGatewayResultV1? composed) =>
        new(item.Id, item.Category, item.ExpectedBehavior, verdict, [.. reasons], checks, dataClass, plannedCalls, releasedSources,
            composed?.Status, composed?.CannotAnswerReason, composed?.Answer?.Citations.Count ?? 0, composed?.Answer?.UsageUnits ?? 0);

    internal static AiEvalTotalsV1 Totals(IReadOnlyList<AiEvalItemResultV1> results)
    {
        var evaluated = results.Where(result => result.Verdict != AiEvalVerdictV1.Withheld).ToArray();
        static AiEvalRateV1 Rate(IEnumerable<bool?> values)
        {
            var applied = values.Where(value => value is not null).ToArray();
            return new AiEvalRateV1(applied.Count(value => value == true), applied.Length);
        }

        var byCategory = evaluated
            .GroupBy(result => result.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => new AiEvalRateV1(group.Count(result => result.Verdict == AiEvalVerdictV1.Passed), group.Count()),
                StringComparer.Ordinal);
        return new AiEvalTotalsV1(
            results.Count,
            results.Count - evaluated.Length,
            new AiEvalRateV1(evaluated.Count(result => result.Verdict == AiEvalVerdictV1.Passed), evaluated.Length),
            Rate(evaluated.Select(result => result.Checks.SourceAccurate)),
            Rate(evaluated.Select(result => result.Checks.AnswerCorrect)),
            Rate(evaluated.Select(result => result.Checks.AbstainedCorrectly)),
            Rate(evaluated.Select(result => result.Checks.RefusedCorrectly)),
            byCategory,
            evaluated.Sum(result => result.UsageUnits));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TermPattern();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"\[\d+\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationMarker();
}
