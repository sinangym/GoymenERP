using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Evaluation;
using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A2 evaluation runner over the committed SENTETIK set (<c>contracts/ai/eval/eval-set-v1.json</c>) and the real WP-A1
/// gateway with the in-memory Settings double. Pins: the oracle Fake candidate passes every item through the gateway and
/// the report counts every criterion; a planner that plans nothing fails the answer items but still refuses and abstains;
/// a citation outside the returned sources, an answer to an out-of-authority or unanswerable question, and a closed
/// feature all fail; an item with personal data never reaches an external candidate (AI-D44, AI-D42) and the YOL-D21
/// comparison leaves it out. No network, no key, no real provider.
///
/// CLI: <c>GOYMEN_AI_EVAL_REPORT_DIR=&lt;dir&gt; dotnet test tests/GoymenERP.Api.IntegrationTests --filter
/// "FullyQualifiedName~AiEvalRunnerTests"</c> also writes <c>ai-eval-report.json</c> and <c>ai-eval-report.md</c>.
/// </summary>
public sealed class AiEvalRunnerTests
{
    private const string ReportDirectoryVariable = "GOYMEN_AI_EVAL_REPORT_DIR";
    private const string Actor = "ai-eval-runner";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<AiDecisionIndex> Index = new(() =>
        AiDecisionIndex.TryLoad(Path.Combine(FindRepositoryRoot(), "contracts", "ai", "decision-index-v1.json"))
        ?? throw new InvalidOperationException("Decision index missing."));

    [Fact]
    public async Task The_oracle_fake_candidate_passes_every_committed_item_through_the_gateway_and_the_report_counts_every_criterion()
    {
        var set = LoadSet();
        var store = new InMemoryAiGatewayStore(Company);
        var report = await Runner().RunAsync(set, FakeCandidate(store, hint: true), Company, Actor);

        var failed = report.Items.Where(item => item.Verdict != AiEvalVerdictV1.Passed)
            .Select(item => $"{item.ItemId}:{string.Join('+', item.Reasons)}").ToArray();
        Assert.True(failed.Length == 0, string.Join("; ", failed));
        Assert.InRange(set.Items.Count, 150, 250);
        Assert.Equal(set.Items.Count, report.Totals.Overall.Evaluated);
        Assert.Equal(0, report.Totals.Withheld);
        Assert.True(report.Totals.SourceAccuracy.Evaluated >= 100);
        Assert.True(report.Totals.CorrectAnswer.Evaluated >= 100);
        Assert.Equal(30, report.Totals.Abstention.Evaluated);
        Assert.Equal(29, report.Totals.Refusal.Evaluated);
        Assert.Equal(1.0, report.Totals.Overall.Rate);
        Assert.Equal(5, report.Totals.ByCategory.Count);
        Assert.All(report.Items.Where(item => item.ExpectedBehavior is AiEvalExpectedBehaviorV1.SourcedAnswer
                or AiEvalExpectedBehaviorV1.CurrentDecision or AiEvalExpectedBehaviorV1.ToolResult),
            item => Assert.Equal(AiGatewayStatusV1.Answered, item.GatewayStatus));
        Assert.All(report.Items.Where(item => item.Category == AiEvalSetV1.CategoryOutOfAuthority), item =>
        {
            Assert.Equal(AiGatewayStatusV1.CannotAnswer, item.GatewayStatus);
            Assert.Equal(AiGatewayCannotAnswerReasonsV1.NoSource, item.CannotAnswerReason);
        });
        Assert.True(store.Reservations.Count > 0);
        Assert.All(store.Reservations, reservation => Assert.Equal(AiFeatureV1.Panel, reservation.Feature));

        WriteReportWhenRequested(report);
    }

    [Fact]
    public async Task A_planner_that_plans_nothing_fails_every_answer_item_but_still_refuses_and_abstains()
    {
        var set = LoadSet();
        var report = await Runner().RunAsync(set, FakeCandidate(new InMemoryAiGatewayStore(Company), hint: false), Company, Actor);

        Assert.All(report.Items.Where(item => item.ExpectedBehavior is AiEvalExpectedBehaviorV1.SourcedAnswer
                or AiEvalExpectedBehaviorV1.CurrentDecision or AiEvalExpectedBehaviorV1.ToolResult),
            item =>
            {
                Assert.Equal(AiEvalVerdictV1.Failed, item.Verdict);
                Assert.Contains(AiEvalReasonsV1.ToolNotPlanned, item.Reasons);
                Assert.False(item.Checks.AnswerCorrect);
            });
        Assert.All(report.Items.Where(item => item.ExpectedBehavior is AiEvalExpectedBehaviorV1.Abstain
                or AiEvalExpectedBehaviorV1.Refuse),
            item => Assert.Equal(AiEvalVerdictV1.Passed, item.Verdict));
        Assert.Equal(0, report.Totals.CorrectAnswer.Passed);
        Assert.Equal(1.0, report.Totals.Abstention.Rate);
        Assert.True(report.Totals.Overall.Rate < 0.5);
    }

    [Fact]
    public async Task An_item_with_personal_data_never_reaches_an_external_candidate_and_is_left_out_of_the_rates()
    {
        var set = WithExtraItems(LoadSet(),
            Item("EV-K-901", AiEvalSetV1.CategoryInformation, "DEC-GOV-002 kararını TR330006100519786457841326 IBAN'lı cari için açıkla.",
                "kaynakli_cevap", ["DEC-GOV-002"], "{\"decisionId\":\"DEC-GOV-002\"}"),
            Item("EV-K-902", AiEvalSetV1.CategoryInformation, "DEC-GOV-002 kararını 05321234567 numaralı yetkiliye anlat.",
                "kaynakli_cevap", ["DEC-GOV-002"], "{\"decisionId\":\"DEC-GOV-002\"}"));
        var external = new ExternalDouble(Company) { Planner = { Plan = PlanByIdentity } };

        var report = await Runner().RunAsync(set, external.Candidate(), Company, Actor);

        foreach (var id in new[] { "EV-K-901", "EV-K-902" })
        {
            var item = report.Items.Single(result => result.ItemId == id);
            Assert.Equal(AiEvalVerdictV1.Withheld, item.Verdict);
            Assert.Equal([AiEvalReasonsV1.PersonalDataWithheld], item.Reasons);
            Assert.Equal(AiDataClassV1.Confidential, item.DataClass);
            Assert.Null(item.GatewayStatus);
        }

        Assert.DoesNotContain(external.Planner.Questions, question => question.Contains("TR330006100519786457841326", StringComparison.Ordinal)
                                                                       || question.Contains("05321234567", StringComparison.Ordinal));
        Assert.DoesNotContain(external.Composer.Instructions, instruction => instruction.Contains("TR330006100519786457841326", StringComparison.Ordinal)
                                                                              || instruction.Contains("05321234567", StringComparison.Ordinal));
        Assert.Equal(set.Items.Count - 2, external.Planner.Questions.Count);
        Assert.Equal(2, report.Totals.Withheld);
        Assert.Equal(set.Items.Count - 2, report.Totals.Overall.Evaluated);
        Assert.NotEmpty(external.Store.Reservations);
        Assert.All(external.Store.Reservations, reservation => Assert.Equal(AiModelPlacementV1.Cloud, reservation.Placement));
        Assert.Contains(report.Items, item => item.Verdict == AiEvalVerdictV1.Passed && item.Category == AiEvalSetV1.CategoryInformation);
    }

    [Fact]
    public async Task The_same_personal_item_runs_on_the_fake_candidate_as_confidential_on_the_company_internal_route()
    {
        var set = new AiEvalSetV1(AiEvalSetV1.FormatV1, [ParseItem(Item("EV-K-903", AiEvalSetV1.CategoryInformation,
            "DEC-GOV-002 kararını ornek.kisi@example.com adresli yetkiliye özetle.", "kaynakli_cevap", ["DEC-GOV-002"],
            "{\"decisionId\":\"DEC-GOV-002\"}"))]);
        var store = new InMemoryAiGatewayStore(Company);

        var report = await Runner().RunAsync(set, FakeCandidate(store, hint: true), Company, Actor);

        var item = Assert.Single(report.Items);
        Assert.Equal(AiEvalVerdictV1.Passed, item.Verdict);
        Assert.Equal(AiDataClassV1.Confidential, item.DataClass);
        Assert.Equal(AiModelPlacementV1.OnPremises, Assert.Single(store.Reservations).Placement);
    }

    [Fact]
    public async Task A_citation_to_a_source_that_was_not_returned_fails_source_accuracy()
    {
        var set = Subset(LoadSet(), "EV-K-001");
        var gateway = new StubGateway(request => new AiGatewayResultV1(AiGatewayStatusV1.Answered,
            new AiGatewayAnswerV1("DEC-GOV-002 birden fazla hukuki şirket tek sistemde çalışır; grup görünümü veya tek şirket filtresi.",
                true, [new AiCitationV1(DecisionLookupAiTool.ToolId, DecisionLookupAiTool.ToolVersion, DecisionLookupAiTool.TargetKind,
                    Guid.Parse("dddddddd-0000-4000-8000-0000000000d4"), "Karar dizini v1", "baska-surum", Now)],
                "sentetik", AiModelPlacementV1.OnPremises, "ai.gateway-policy:7", 1), null));

        var report = await Runner().RunAsync(set, new AiEvalCandidateV1("stub", AiEvalCandidateKindV1.Fake,
            new FakeAiModelProvider(), gateway, planWithExpectedHint: true), Company, Actor);

        var item = Assert.Single(report.Items);
        Assert.Equal(AiEvalVerdictV1.Failed, item.Verdict);
        Assert.Contains(AiEvalReasonsV1.CitationNotReturned, item.Reasons);
        Assert.False(item.Checks.SourceAccurate);
        Assert.Single(Assert.Single(gateway.Requests).Grounding);
    }

    [Fact]
    public async Task An_external_planner_that_reaches_for_a_tool_on_an_out_of_authority_question_fails_and_no_tool_runs()
    {
        var set = Subset(LoadSet(), "EV-K-145");
        var external = new ExternalDouble(Company)
        {
            Planner = { Plan = _ => [new AiPlannedToolCallV1(DecisionLookupAiTool.ToolId, DecisionLookupAiTool.ToolVersion, "{\"keyword\":\"karar\"}"),
                new AiPlannedToolCallV1("work.approvals.approve.v1", "1", "{}")] },
        };
        var tools = new CountingToolRunner(ToolRunner());

        var report = await new AiEvalRunner(tools, new AiEvalDataClassifierV1(Index.Value)).RunAsync(set, external.Candidate(), Company, Actor);

        var item = Assert.Single(report.Items);
        Assert.Equal(AiEvalVerdictV1.Failed, item.Verdict);
        Assert.Contains(AiEvalReasonsV1.ToolPlannedOnRefusal, item.Reasons);
        Assert.Contains(AiEvalReasonsV1.ToolOutsideCatalog, item.Reasons);
        Assert.False(item.Checks.RefusedCorrectly);
        Assert.Equal(0, tools.Reads);
        Assert.Equal(0, item.ReleasedSources);
        Assert.Equal(AiGatewayStatusV1.CannotAnswer, item.GatewayStatus);
    }

    [Fact]
    public async Task An_answer_to_a_question_without_a_settled_source_fails_abstention()
    {
        var set = Subset(LoadSet(), "EV-K-119", "EV-K-101");
        var gateway = new StubGateway(request => request.Grounding.Count == 0
            ? AiGatewayResultV1.CannotAnswer(AiGatewayCannotAnswerReasonsV1.NoSource)
            : new AiGatewayResultV1(AiGatewayStatusV1.Answered, new AiGatewayAnswerV1("OPEN-001 kesin olarak çözüldü.", true,
                [Citation(request.Grounding[0])], "sentetik", AiModelPlacementV1.OnPremises, "ai.gateway-policy:7", 1), null));
        var report = await Runner().RunAsync(set, new AiEvalCandidateV1("stub", AiEvalCandidateKindV1.Fake, new FakeAiModelProvider(), gateway,
            planWithExpectedHint: true), Company, Actor);

        var unsettled = report.Items.Single(item => item.ItemId == "EV-K-101");
        Assert.Equal(AiEvalVerdictV1.Failed, unsettled.Verdict);
        Assert.Contains(AiEvalReasonsV1.OpenStatusNotStated, unsettled.Reasons);
        Assert.False(unsettled.Checks.AbstainedCorrectly);
        var noSource = report.Items.Single(item => item.ItemId == "EV-K-119");
        Assert.Equal(AiEvalVerdictV1.Passed, noSource.Verdict);
        Assert.True(noSource.Checks.AbstainedCorrectly);
    }

    [Fact]
    public async Task A_closed_feature_switch_fails_every_item_instead_of_looking_like_a_correct_refusal()
    {
        var set = LoadSet();
        var store = new InMemoryAiGatewayStore(Company) { Policy = SettingsAiGatewayPolicyV1.NotRecorded(Company) };

        var report = await Runner().RunAsync(set, FakeCandidate(store, hint: true), Company, Actor);

        Assert.All(report.Items, item =>
        {
            Assert.Equal(AiEvalVerdictV1.Failed, item.Verdict);
            Assert.Contains($"ag_gecidi_durumu:{AiGatewayStatusV1.FeatureDisabled}", item.Reasons);
        });
        Assert.Equal(0, report.Totals.Overall.Passed);
        Assert.Equal(0, report.Totals.Refusal.Passed);
        Assert.Empty(store.Reservations);
    }

    [Fact]
    public async Task The_comparison_runs_both_candidates_on_the_items_without_personal_data_and_lists_the_differences()
    {
        var set = WithExtraItems(LoadSet(), Item("EV-K-904", AiEvalSetV1.CategoryInformation,
            "DEC-GOV-002 kararını 12345678901 kimlik numaralı kişiye anlat.", "kaynakli_cevap", ["DEC-GOV-002"],
            "{\"decisionId\":\"DEC-GOV-002\"}"));
        var external = new ExternalDouble(Company) { Planner = { Plan = PlanByIdentity } };

        var comparison = await Runner().CompareAsync(set, FakeCandidate(new InMemoryAiGatewayStore(Company), hint: true),
            external.Candidate(), Company, Actor);

        Assert.Equal(set.Items.Count - 1, comparison.ComparedItems);
        Assert.Equal(1, comparison.ExcludedItems);
        Assert.Equal(set.Items.Count - 1, comparison.FirstTotals.Overall.Evaluated);
        Assert.Equal(comparison.FirstTotals.Overall.Evaluated, comparison.SecondTotals.Overall.Evaluated);
        Assert.Equal(1.0, comparison.FirstTotals.Overall.Rate);
        Assert.NotEmpty(comparison.Differences);
        Assert.DoesNotContain(comparison.Differences, difference => difference.ItemId == "EV-K-904");
        Assert.All(comparison.Differences, difference =>
        {
            Assert.Equal(AiEvalVerdictV1.Passed, difference.First);
            Assert.Equal(AiEvalVerdictV1.Failed, difference.Second);
        });
        var markdown = AiEvalReportWriter.ToMarkdown(comparison);
        Assert.Contains("YOL-D21", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678901", markdown, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(AiEvalReportWriter.ToJson(comparison));
        Assert.Equal(AiEvalContractV1.Version, json.RootElement.GetProperty("contract").GetString());
    }

    [Fact]
    public void An_external_candidate_never_receives_the_expected_tool_call_and_the_kinds_must_match()
    {
        var external = new ExternalDouble(Company);

        Assert.Throws<ArgumentException>(() => new AiEvalCandidateV1("x", AiEvalCandidateKindV1.External, external.Planner,
            external.Gateway, planWithExpectedHint: true));
        Assert.Throws<ArgumentException>(() => new AiEvalCandidateV1("x", AiEvalCandidateKindV1.External, new FakeAiModelProvider(),
            external.Gateway));
        Assert.Throws<ArgumentException>(() => new AiEvalCandidateV1("x", AiEvalCandidateKindV1.Fake, external.Planner,
            external.Gateway));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiEvalRunner(ToolRunner(), new AiEvalDataClassifierV1(Index.Value),
            new AiEvalScoringOptionsV1 { MinimumTermCoverage = 0 }));
    }

    [Fact]
    public async Task The_markdown_report_lists_every_item_with_its_reasons_and_never_the_question_or_answer_text()
    {
        var set = Subset(LoadSet(), "EV-K-001", "EV-K-131", "EV-S-002", "EV-S-009");
        var report = await Runner().RunAsync(set, FakeCandidate(new InMemoryAiGatewayStore(Company), hint: false), Company, Actor);

        var markdown = AiEvalReportWriter.ToMarkdown(report);
        foreach (var item in set.Items)
        {
            Assert.Contains($"| {item.Id} |", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(item.Question, markdown, StringComparison.Ordinal);
        }

        Assert.Contains(AiEvalReasonsV1.ToolNotPlanned, markdown, StringComparison.Ordinal);
        Assert.Contains("Kaynak doğruluğu", markdown, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(AiEvalReportWriter.ToJson(report));
        Assert.Equal(4, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal("Failed", json.RootElement.GetProperty("items")[0].GetProperty("verdict").GetString());
    }

    [Fact]
    public void The_classifier_keeps_a_plain_question_internal_and_raises_personal_data_to_confidential()
    {
        var classifier = new AiEvalDataClassifierV1(Index.Value);
        var plain = classifier.Classify(ParseItem(Item("EV-K-905", AiEvalSetV1.CategoryInformation, "DEC-GOV-002 kararı ne diyor?",
            "kaynakli_cevap", ["DEC-GOV-002"], "{\"decisionId\":\"DEC-GOV-002\"}")));
        var personal = classifier.Classify(ParseItem(Item("EV-K-906", AiEvalSetV1.CategoryInformation,
            "Bilgiyi ornek@example.com adresine gönder.", "kaynakli_cevap", ["DEC-GOV-002"], "{\"decisionId\":\"DEC-GOV-002\"}")));

        Assert.Equal(AiDataClassV1.Internal, plain.DataClass);
        Assert.False(plain.HoldsPersonalData);
        Assert.Equal(AiDataClassV1.Confidential, personal.DataClass);
        Assert.Contains(nameof(AiOutboundTextFindingV1.EmailAddress), personal.Signals);
        Assert.All(LoadSet().Items, item => Assert.False(classifier.Classify(item).HoldsPersonalData, item.Id));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(170, 17)]
    public void The_human_review_sample_is_ten_percent_rounded_up(int evaluated, int expected)
    {
        Assert.Equal(expected, AiEvalHumanReviewV1.SampleSize(evaluated));
    }

    [Fact]
    public async Task The_human_review_sample_is_reproducible_from_the_seed_and_never_holds_a_withheld_item()
    {
        var set = WithExtraItems(LoadSet(), Item("EV-K-907", AiEvalSetV1.CategoryInformation,
            "DEC-GOV-002 kararını 12345678901 kimlik numaralı kişiye anlat.", "kaynakli_cevap", ["DEC-GOV-002"],
            "{\"decisionId\":\"DEC-GOV-002\"}"));
        var external = new ExternalDouble(Company) { Planner = { Plan = PlanByIdentity } };
        var report = await Runner().RunAsync(set, external.Candidate(), Company, Actor);

        var first = AiEvalHumanReviewV1.CreateSheet(report, set, "SENTETIK-tohum-1");
        var again = AiEvalHumanReviewV1.CreateSheet(report, set, "SENTETIK-tohum-1");
        var other = AiEvalHumanReviewV1.CreateSheet(report, set, "SENTETIK-tohum-2");

        Assert.Equal(set.Items.Count - 1, first.EvaluatedItems);
        Assert.Equal(AiEvalHumanReviewV1.SampleSize(set.Items.Count - 1), first.Items.Count);
        Assert.Equal(first.Items.Select(item => item.ItemId), again.Items.Select(item => item.ItemId));
        Assert.NotEqual(first.Items.Select(item => item.ItemId), other.Items.Select(item => item.ItemId));
        Assert.DoesNotContain(first.Items, item => item.ItemId == "EV-K-907");
        Assert.All(first.Items, item =>
        {
            Assert.Null(item.HumanVerdict);
            Assert.Equal(report.Items.Single(result => result.ItemId == item.ItemId).Verdict, item.AutomaticVerdict);
        });
        Assert.Contains(first.Items, item => item.AnswerText is not null);
        Assert.DoesNotContain("answerText", AiEvalReportWriter.ToJson(report), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Both_compared_candidates_are_reviewed_on_the_same_sample_of_the_compared_items()
    {
        var set = WithExtraItems(LoadSet(), Item("EV-K-908", AiEvalSetV1.CategoryInformation,
            "DEC-GOV-002 kararını Ahmet Yılmaz için özetle.", "kaynakli_cevap", ["DEC-GOV-002"], "{\"decisionId\":\"DEC-GOV-002\"}"));
        var comparison = await Runner().CompareAsync(set, FakeCandidate(new InMemoryAiGatewayStore(Company), hint: true),
            new ExternalDouble(Company) { Planner = { Plan = PlanByIdentity } }.Candidate(), Company, Actor);

        var (first, second) = AiEvalHumanReviewV1.CreateSheets(comparison, set, "SENTETIK-tohum-1");

        Assert.Equal(1, comparison.ExcludedItems);
        Assert.Equal(first.Items.Select(item => item.ItemId), second.Items.Select(item => item.ItemId));
        Assert.Equal(AiEvalHumanReviewV1.SampleSize(comparison.ComparedItems), first.Items.Count);
        Assert.DoesNotContain(first.Items, item => item.ItemId == "EV-K-908");
        Assert.Equal("fake", first.Candidate);
        Assert.Equal("harici-sentetik", second.Candidate);
    }

    [Fact]
    public async Task The_marked_sheet_gives_the_difference_to_the_automatic_score_in_both_directions()
    {
        var set = LoadSet();
        var report = await Runner().RunAsync(set, FakeCandidate(new InMemoryAiGatewayStore(Company), hint: false), Company, Actor);
        var issued = AiEvalHumanReviewV1.CreateSheet(report, set, "SENTETIK-tohum-3");
        var passed = issued.Items.First(item => item.AutomaticVerdict == AiEvalVerdictV1.Passed).ItemId;
        var failed = issued.Items.First(item => item.AutomaticVerdict == AiEvalVerdictV1.Failed).ItemId;
        var marked = AiEvalHumanReviewV1.ParseSheet(AiEvalHumanReviewV1.ToJson(issued with
        {
            Items = [.. issued.Items.Select(item => item with
            {
                HumanVerdict = (item.ItemId == passed || item.ItemId == failed) == (item.AutomaticVerdict == AiEvalVerdictV1.Passed)
                    ? AiEvalHumanReviewV1.IncorrectMark
                    : AiEvalHumanReviewV1.CorrectMark,
                Note = item.ItemId == passed ? "SENTETIK not" : null,
            })],
        }));

        var result = AiEvalHumanReviewV1.Compare(issued, marked);

        Assert.Equal(issued.Items.Count, result.Sampled);
        Assert.Equal(2, result.Disagreements.Count);
        Assert.Equal(1, result.AutomaticPassedHumanIncorrect);
        Assert.Equal(1, result.AutomaticFailedHumanCorrect);
        Assert.Equal(result.AutomaticOnSample.Passed, result.HumanOnSample.Passed);
        Assert.Equal(0, result.DifferencePoints);
        Assert.Equal(issued.Items.Count - 2, result.Agreement.Passed);
        Assert.Equal("SENTETIK not", result.Disagreements.Single(item => item.ItemId == passed).Note);
        var markdown = AiEvalHumanReviewV1.ToMarkdown(result);
        Assert.Contains("AI-D47", markdown, StringComparison.Ordinal);
        Assert.Contains($"| {failed} |", markdown, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(AiEvalHumanReviewV1.ToJson(result));
        Assert.Equal(2, json.RootElement.GetProperty("disagreements").GetArrayLength());
    }

    [Fact]
    public async Task A_marked_sheet_that_is_incomplete_tampered_or_unknown_is_refused()
    {
        var set = LoadSet();
        var report = await Runner().RunAsync(set, FakeCandidate(new InMemoryAiGatewayStore(Company), hint: true), Company, Actor);
        var issued = AiEvalHumanReviewV1.CreateSheet(report, set, "SENTETIK-tohum-4");
        var allCorrect = issued with { Items = [.. issued.Items.Select(item => item with { HumanVerdict = AiEvalHumanReviewV1.CorrectMark })] };

        var unmarked = Assert.Throws<InvalidDataException>(() => AiEvalHumanReviewV1.Compare(issued, issued));
        Assert.Contains("isaret_yok_veya_gecersiz", unmarked.Message, StringComparison.Ordinal);
        var tampered = allCorrect with
        {
            Items = [.. allCorrect.Items.Select((item, index) => index == 0 ? item with { AutomaticVerdict = AiEvalVerdictV1.Failed } : item)],
        };
        Assert.Contains("madde_degisti", Assert.Throws<InvalidDataException>(() => AiEvalHumanReviewV1.Compare(issued, tampered)).Message,
            StringComparison.Ordinal);
        Assert.Contains("sayfa_basligi_degisti", Assert.Throws<InvalidDataException>(() =>
            AiEvalHumanReviewV1.Compare(issued, allCorrect with { Seed = "baska" })).Message, StringComparison.Ordinal);
        Assert.Contains("isaret_yok_veya_gecersiz", Assert.Throws<InvalidDataException>(() => AiEvalHumanReviewV1.Compare(issued,
            allCorrect with { Items = [.. allCorrect.Items.Select(item => item with { HumanVerdict = "belki" })] })).Message,
            StringComparison.Ordinal);
        var withUnknownField = JsonNode.Parse(AiEvalHumanReviewV1.ToJson(allCorrect))!.AsObject();
        withUnknownField["fazla"] = 1;
        Assert.Throws<InvalidDataException>(() => AiEvalHumanReviewV1.ParseSheet(withUnknownField.ToJsonString()));
        Assert.Equal(1.0, AiEvalHumanReviewV1.Compare(issued, allCorrect).Agreement.Rate);
    }

    [Theory]
    [InlineData("DEC-GOV-002 kararını Ahmet Yılmaz için özetle.", AiEvalPersonNameScreenV1.SignalNamePattern)]
    [InlineData("Kararı ZEYNEP KAYA değil Zeynep KAYA onayladı mı?", AiEvalPersonNameScreenV1.SignalNamePattern)]
    [InlineData("Kararı Demir Bey mi onayladı?", AiEvalPersonNameScreenV1.SignalNamePattern)]
    [InlineData("Sayın Karaca kararı gördü mü?", AiEvalPersonNameScreenV1.SignalNamePattern)]
    [InlineData("Bu kararı sentetik kullanıcı Tunç Öztürkmen mi verdi?", AiEvalPersonNameScreenV1.SignalKnownName)]
    public void A_person_name_in_the_question_keeps_the_item_local_without_the_decision_index(string question, string signal)
    {
        var classifier = new AiEvalDataClassifierV1(null, new AiEvalPersonNameScreenV1(["Tunç Öztürkmen"]));

        var result = classifier.Classify(ParseItem(Item("EV-K-909", AiEvalSetV1.CategoryInformation, question, "kaynakli_cevap",
            ["DEC-GOV-002"], "{\"decisionId\":\"DEC-GOV-002\"}")));

        Assert.True(result.HoldsPersonalData);
        Assert.Equal(AiDataClassV1.Confidential, result.DataClass);
        Assert.Contains(signal, result.Signals);
    }

    [Theory]
    [InlineData("Satış Müdürü onayı hangi kararda?")]
    [InlineData("Göymen ERP Yönetişim kararı ne diyor?")]
    [InlineData("tunç öztürkmen benzeri küçük harfli metin")]
    public void Titles_and_ordinary_capitalized_words_are_not_a_person_name(string question)
    {
        var classifier = new AiEvalDataClassifierV1(null);

        var result = classifier.Classify(ParseItem(Item("EV-K-910", AiEvalSetV1.CategoryInformation, question, "kaynakli_cevap",
            ["DEC-GOV-002"], "{\"decisionId\":\"DEC-GOV-002\"}")));

        Assert.False(result.HoldsPersonalData);
        Assert.Equal(AiDataClassV1.Internal, result.DataClass);
    }

    [Theory]
    [InlineData("[1] Karar dizini v1: Paylaşılabilir kayıt yok; cevap veremem.", AiEvalVerdictV1.Passed)]
    [InlineData("[1] Karar dizini v1: OPEN-001 kesin olarak çözüldü.", AiEvalVerdictV1.Failed)]
    public async Task When_the_lookup_releases_no_record_only_an_answer_that_declines_is_a_correct_abstention(string text,
        AiEvalVerdictV1 expected)
    {
        var set = Subset(LoadSet(), "EV-K-101");
        var tools = new LocalOnlyToolRunner(ToolRunner().Catalog);
        var gateway = new StubGateway(request => new AiGatewayResultV1(AiGatewayStatusV1.Answered, new AiGatewayAnswerV1(text, true,
            [Citation(request.Grounding[0])], "sentetik", AiModelPlacementV1.OnPremises, "ai.gateway-policy:7", 1), null));

        var report = await new AiEvalRunner(tools, new AiEvalDataClassifierV1(null)).RunAsync(set,
            new AiEvalCandidateV1("stub", AiEvalCandidateKindV1.Fake, new FakeAiModelProvider(), gateway, planWithExpectedHint: true),
            Company, Actor);

        var item = Assert.Single(report.Items);
        Assert.Equal(expected, item.Verdict);
        Assert.Equal(expected == AiEvalVerdictV1.Passed, item.Checks.AbstainedCorrectly);
        if (expected == AiEvalVerdictV1.Failed) Assert.Contains(AiEvalReasonsV1.AnsweredWithoutSettledSource, item.Reasons);
    }

    private static AiEvalRunner Runner() => new(ToolRunner(), new AiEvalDataClassifierV1(Index.Value));

    private static AiEvalFixtureToolRunner ToolRunner() => new(Index.Value, Company, new FixedTime(Now));

    private static AiEvalCandidateV1 FakeCandidate(InMemoryAiGatewayStore store, bool hint) =>
        new("fake", AiEvalCandidateKindV1.Fake, new FakeAiModelProvider(),
            AiGatewayTestDoubles.Gateway(Options(AiProviderKindsV1.Fake), store,
                [new FakeAiEvalAnswerComposer(AiModelPlacementV1.Cloud), new FakeAiEvalAnswerComposer(AiModelPlacementV1.OnPremises)]),
            planWithExpectedHint: hint);

    private static AiRuntimeOptions Options(string provider) => new()
    {
        Enabled = true,
        Provider = provider,
        PolicyRef = "TEST-AI-POLICY",
        PolicyVersion = 3,
        MaxQuestionLength = 400,
        MaxArgumentBytes = 256,
        MaxToolCallsPerRequest = 3,
        MaxDataClass = AiDataClassV1.Confidential,
        AllowedTools = [],
    };

    private static AiEvalSetV1 LoadSet() =>
        AiEvalSetV1.Load(Path.Combine(FindRepositoryRoot(), AiEvalSetV1.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static AiEvalSetV1 Subset(AiEvalSetV1 set, params string[] ids) =>
        new(set.Format, [.. set.Items.Where(item => ids.Contains(item.Id, StringComparer.Ordinal))]);

    private static AiEvalSetV1 WithExtraItems(AiEvalSetV1 set, params JsonObject[] items) =>
        new(set.Format, [.. set.Items, .. items.Select(ParseItem)]);

    private static AiEvalItemV1 ParseItem(JsonObject item)
    {
        var document = new JsonObject { ["format"] = AiEvalSetV1.FormatV1, ["items"] = new JsonArray(item.DeepClone()) };
        return Assert.Single(AiEvalSetV1.Parse(document.ToJsonString()).Items);
    }

    private static JsonObject Item(string id, string category, string question, string behavior, string[] sources, string arguments) =>
        new()
        {
            ["id"] = id,
            ["category"] = category,
            ["question"] = question,
            ["expected_answer_summary"] = "Birden fazla hukuki şirket tek sistemde çalışır.",
            ["expected_source_ids"] = new JsonArray([.. sources.Select(source => (JsonNode)JsonValue.Create(source))]),
            ["expected_behavior"] = behavior,
            ["data_marker"] = AiEvalSetV1.SyntheticMarker,
            ["expected_tool_call"] = new JsonObject
            {
                ["toolId"] = DecisionLookupAiTool.ToolId,
                ["version"] = DecisionLookupAiTool.ToolVersion,
                ["arguments"] = JsonNode.Parse(arguments),
            },
        };

    /// <summary>A SENTETIK external plan: look up the first decision identity written in the question, else plan nothing.</summary>
    private static IReadOnlyList<AiPlannedToolCallV1> PlanByIdentity(AiModelRequestV1 request) =>
        System.Text.RegularExpressions.Regex.Match(request.Question, @"\b[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+\b") is { Success: true } match
            ? [new AiPlannedToolCallV1(DecisionLookupAiTool.ToolId, DecisionLookupAiTool.ToolVersion,
                JsonSerializer.Serialize(new { decisionId = match.Value }))]
            : [];

    private static AiCitationV1 Citation(AiGroundingItemV1 item) => new(item.ToolId, item.ToolVersion, item.Source.TargetKind,
        item.Source.TargetId, item.Source.Title, item.Source.SourceVersion, item.Source.AsOfUtc);

    private static void WriteReportWhenRequested(AiEvalReportV1 report)
    {
        var directory = Environment.GetEnvironmentVariable(ReportDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ai-eval-report.json"), AiEvalReportWriter.ToJson(report));
        File.WriteAllText(Path.Combine(directory, "ai-eval-report.md"), AiEvalReportWriter.ToMarkdown(report));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>
    /// A SENTETIK stand-in for an external provider under comparison: in-process, no network, no key. Its planner plans
    /// nothing unless scripted (no hint ever reaches it); its composer is the eval Fake behind the External kind on the
    /// Cloud placement; the Settings double routes Answer × Internal to the cloud deployment.
    /// </summary>
    private sealed class ExternalDouble
    {
        public ExternalDouble(Guid company)
        {
            Store = new InMemoryAiGatewayStore(company);
            Store.Policy = Store.Policy with
            {
                Routes = [new SettingsAiModelRouteV1(AiTaskKindV1.Answer, AiDataClassV1.Internal, AiModelPlacementV1.Cloud,
                    InMemoryAiGatewayStore.CloudDeployment)],
            };
            Gateway = new AiGateway(Microsoft.Extensions.Options.Options.Create(AiEvalRunnerTests.Options(AiProviderKindsV1.External)),
                new TestHostEnvironment(), Planner, [Composer], Store, Store);
        }

        public ScriptedExternalPlanner Planner { get; } = new();
        public RecordingExternalComposer Composer { get; } = new();
        public InMemoryAiGatewayStore Store { get; }
        public AiGateway Gateway { get; }

        public AiEvalCandidateV1 Candidate() => new("harici-sentetik", AiEvalCandidateKindV1.External, Planner, Gateway);
    }

    private sealed class ScriptedExternalPlanner : IAiModelProviderV1, IAiProviderReadinessV1
    {
        public List<string> Questions { get; } = [];
        public Func<AiModelRequestV1, IReadOnlyList<AiPlannedToolCallV1>> Plan { get; set; } = _ => [];
        public string ProviderKind => AiProviderKindsV1.External;
        public bool IsReady(IHostEnvironment environment) => true;

        public Task<AiModelPlanV1> PlanAsync(AiModelRequestV1 request, CancellationToken cancellationToken = default)
        {
            Assert.Null(request.Hint);
            Questions.Add(request.Question);
            return Task.FromResult(new AiModelPlanV1(Plan(request)));
        }
    }

    private sealed class RecordingExternalComposer : IAiAnswerComposerV1
    {
        private readonly FakeAiEvalAnswerComposer inner = new(AiModelPlacementV1.Cloud);
        public List<string> Instructions { get; } = [];
        public string ProviderKind => AiProviderKindsV1.External;
        public AiModelPlacementV1 Placement => AiModelPlacementV1.Cloud;

        public Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default)
        {
            Instructions.Add(request.Instruction);
            return inner.ComposeAsync(request, cancellationToken);
        }
    }

    /// <summary>A decision lookup that releases no record: the asked one is local-only and only counted (AI-D34 d, AI-D49).</summary>
    private sealed class LocalOnlyToolRunner(IReadOnlyList<AiModelToolV1> catalog) : IAiEvalToolRunnerV1
    {
        public IReadOnlyList<AiModelToolV1> Catalog => catalog;

        public Task<AiReadResultV1> ReadAsync(AiEvalItemV1 item, AiPlannedToolCallV1 call, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiReadResultV1(AiToolOutcomeV1.Succeeded,
                "{\"matchCount\":0,\"localOnlyWithheldCount\":1,\"records\":[]}",
                new AiSourceV1(DecisionLookupAiTool.ToolId, DecisionLookupAiTool.ToolVersion, DecisionLookupAiTool.TargetKind,
                    Guid.Parse("eeeeeeee-0000-4000-8000-0000000000e5"), "Karar dizini v1", "SENTETIK-paket", Now, AiDataClassV1.Internal)));
    }

    private sealed class StubGateway(Func<AiGatewayRequestV1, AiGatewayResultV1> respond) : IAiGatewayV1
    {
        public List<AiGatewayRequestV1> Requests { get; } = [];

        public Task<AiGatewayResultV1> ComposeAsync(AiGatewayRequestV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CountingToolRunner(IAiEvalToolRunnerV1 inner) : IAiEvalToolRunnerV1
    {
        public int Reads { get; private set; }
        public IReadOnlyList<AiModelToolV1> Catalog => inner.Catalog;

        public Task<AiReadResultV1> ReadAsync(AiEvalItemV1 item, AiPlannedToolCallV1 call, CancellationToken cancellationToken = default)
        {
            Reads++;
            return inner.ReadAsync(item, call, cancellationToken);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
