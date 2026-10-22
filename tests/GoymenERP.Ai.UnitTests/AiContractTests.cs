using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Ai.UnitTests;

// Pure contract examples only: no registered tool, provider, owner invocation or approval service.
public sealed class AiContractTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PreviewId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ApprovalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Created = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nested_object_order_and_whitespace_do_not_change_canonical_value()
    {
        var first = AiCanonicalJson.Canonicalize(" { \"z\": [ {\"b\":2, \"a\":1}, null ], \"a\":true } ");
        var second = AiCanonicalJson.Canonicalize("{\"a\":true,\"z\":[{\"a\":1,\"b\":2},null]}");
        Assert.Equal("{\"a\":true,\"z\":[{\"a\":1,\"b\":2},null]}", first);
        Assert.Equal(first, second);
        Assert.Equal(AiCanonicalJson.Canonicalize("\"a\""), AiCanonicalJson.Canonicalize("\"\\u0061\""));
    }

    [Theory]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("1", "1.0")]
    [InlineData("1", "1e0")]
    [InlineData("0", "-0")]
    [InlineData("1", "\"1\"")]
    [InlineData("true", "false")]
    [InlineData("null", "\"null\"")]
    [InlineData("{\"a\":1}", "{\"a\":2}")]
    public void Scalar_spelling_type_array_order_and_value_changes_are_preserved(string left, string right) =>
        Assert.NotEqual(AiCanonicalJson.Canonicalize(left), AiCanonicalJson.Canonicalize(right));

    [Theory]
    [InlineData("")]
    [InlineData("{\"a\":}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1,]")]
    [InlineData("1 2")]
    [InlineData("/* comment */ {}")]
    [InlineData("{} // comment")]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"a\":1,\"\\u0061\":2}")]
    [InlineData("{\"outer\":[{\"x\":1,\"x\":2}]}")]
    public void Malformed_or_ambiguous_json_is_rejected(string json) =>
        Assert.ThrowsAny<JsonException>(() => AiCanonicalJson.Canonicalize(json));

    [Fact]
    public void Excessive_json_depth_is_rejected_without_numeric_conversion()
    {
        Assert.ThrowsAny<JsonException>(() => AiCanonicalJson.Canonicalize(new string('[', 33) + "0" + new string(']', 33)));
        const string token = "123456789012345678901234567890.12345678901234567890";
        Assert.Equal(token, AiCanonicalJson.Canonicalize(token));
    }

    [Fact]
    public void Every_server_identity_and_authority_change_invalidates_the_preview_identity()
    {
        var context = Context();
        var variants = new[]
        {
            context with { CompanyId = Target }, context with { ActorId = "actor-2" },
            context with { SessionId = "session-2" }, context with { ScopeRevision = "scope-2" },
            context with { AuthorityRevision = "authority-2" }, context with { CorrelationId = "correlation-2" },
            context with { CausationId = "cause-2" }, context with { ModelDeployment = "synthetic-deployment-2" },
            context with { PromptPolicyVersion = "prompt-2" },
        };
        var tool = Tool(); var preview = Preview();
        var identity = AiCanonicalJson.PreviewIdentity(context, tool, preview);
        var hash = AiCanonicalJson.PreviewHash(context, tool, preview);
        foreach (var changed in variants)
        {
            Assert.NotEqual(identity, AiCanonicalJson.PreviewIdentity(changed, tool, preview));
            Assert.NotEqual(hash, AiCanonicalJson.PreviewHash(changed, tool, preview));
        }
    }

    [Fact]
    public void All_contract_revision_fields_and_descriptor_metadata_are_bound()
    {
        var revision = Revision();
        var revisions = new[]
        {
            revision with { InputSchema = "input-2" }, revision with { OutputSchema = "output-2" },
            revision with { FieldPolicy = "field-2" }, revision with { LimitPolicy = "limit-2" },
            revision with { Catalogue = "catalogue-2" }, revision with { AuthorizationPolicy = "auth-2" },
            revision with { SourceContract = "source-2" }, revision with { RedactionPolicy = "redaction-2" },
        };
        var context = Context(); var preview = Preview();
        var identity = AiCanonicalJson.PreviewIdentity(context, Tool(), preview);
        foreach (var changed in revisions)
            Assert.NotEqual(identity, AiCanonicalJson.PreviewIdentity(context, Tool(revision: changed), preview));
        var tools = new[]
        {
            Tool(version: "2"), Tool(toolId: "sample.other.v1"), Tool(owner: "Other"),
            Tool(command: "OtherCommand"), Tool(permissions: ["sample.other"]), Tool(fields: ["other"]),
            Tool(dataClass: AiDataClassV1.Confidential), Tool(risk: AiToolRiskClassV1.Read),
        };
        foreach (var changed in tools)
            Assert.NotEqual(identity, AiCanonicalJson.PreviewIdentity(context, changed, preview));
    }

    [Fact]
    public void Target_command_shown_difference_source_and_time_are_bound()
    {
        var context = Context(); var tool = Tool();
        var identity = AiCanonicalJson.PreviewIdentity(context, tool, Preview());
        var variants = new[]
        {
            Preview(id: ApprovalId), Preview(targetId: Company), Preview(targetKind: "other"),
            Preview(targetVersion: "target-2"), Preview(command: "{\"quantity\":2}"),
            Preview(difference: "{\"quantity\":{\"from\":0,\"to\":2}}"),
            Preview(sources: [Source() with { SourceVersion = "source-2" }]),
            Preview(sources: [Source() with { TargetId = Company }]),
            Preview(sources: [Source() with { AsOfUtc = Created.AddSeconds(1) }]),
            Preview(created: Created.AddSeconds(1)), Preview(expires: Created.AddMinutes(14)),
        };
        foreach (var changed in variants)
            Assert.NotEqual(identity, AiCanonicalJson.PreviewIdentity(context, tool, changed));
        Assert.Equal(identity, AiCanonicalJson.PreviewIdentity(context, tool,
            Preview(command: " { \"quantity\" : 1 } ")));
    }

    [Fact]
    public void Free_text_newlines_cannot_alias_adjacent_identity_fields()
    {
        var left = Context() with { ActorId = "a\nb", SessionId = "c" };
        var right = Context() with { ActorId = "a", SessionId = "b\nc" };
        // Regression counterexample: a naive newline join would produce the same text.
        Assert.Equal(left.ActorId + "\n" + left.SessionId, right.ActorId + "\n" + right.SessionId);
        var preview = Preview(command: "{\"note\":\"line1\\nline2\\\"end\"}");
        Assert.NotEqual(AiCanonicalJson.PreviewIdentity(left, Tool(), preview),
            AiCanonicalJson.PreviewIdentity(right, Tool(), preview));
        Assert.NotEqual(AiCanonicalJson.PreviewHash(left, Tool(), preview),
            AiCanonicalJson.PreviewHash(right, Tool(), preview));
    }

    [Fact]
    public void Metadata_sources_and_invalid_fields_are_snapshots_not_mutable_aliases()
    {
        var permissions = new List<string> { "sample.read" }; var fields = new List<string> { "quantity" };
        var tool = Tool(permissions: permissions, fields: fields);
        permissions[0] = "sample.admin"; fields.Clear();
        Assert.Equal("sample.read", Assert.Single(tool.RequiredPermissions));
        Assert.Equal("quantity", Assert.Single(tool.FieldAllowlist));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)tool.RequiredPermissions)[0] = "other");
        Assert.Equal(AiCanonicalJson.PreviewIdentity(Context(), Tool(permissions: ["b", "a"]), Preview()),
            AiCanonicalJson.PreviewIdentity(Context(), Tool(permissions: ["a", "b"]), Preview()));
        var sources = new List<AiSourceV1> { Source() };
        var preview = Preview(sources: sources);
        var draft = new AiDraftResultV1(AiToolOutcomeV1.Succeeded, "{}", sources);
        sources[0] = Source() with { SourceVersion = "changed" }; sources.Clear();
        Assert.Equal("source-1", Assert.Single(preview.Sources).SourceVersion);
        Assert.Equal("source-1", Assert.Single(draft.Sources).SourceVersion);
        Assert.Throws<NotSupportedException>(() => ((IList<AiSourceV1>)preview.Sources).Clear());
        var invalid = new List<string> { "quantity" };
        var result = new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: invalid);
        invalid.Clear();
        Assert.Equal("quantity", Assert.Single(result.InvalidFields));
    }

    [Theory]
    [InlineData(AiToolOutcomeV1.Denied)]
    [InlineData(AiToolOutcomeV1.NotFound)]
    [InlineData(AiToolOutcomeV1.Unavailable)]
    [InlineData(AiToolOutcomeV1.Conflict)]
    [InlineData(AiToolOutcomeV1.Invalid)]
    public void Non_success_cannot_disclose_content_or_sources(AiToolOutcomeV1 outcome)
    {
        Assert.Throws<ArgumentException>(() => new AiReadResultV1(outcome, "{}"));
        Assert.Throws<ArgumentException>(() => new AiReadResultV1(outcome, source: Source()));
        Assert.Throws<ArgumentException>(() => new AiDraftResultV1(outcome, "{}"));
        Assert.Throws<ArgumentException>(() => new AiDraftResultV1(outcome, sources: [Source()]));
        Assert.Throws<ArgumentException>(() => new AiPreviewResultV1(outcome, Preview()));
        var read = new AiReadResultV1(outcome);
        Assert.Null(read.ContentJson); Assert.Null(read.Source); Assert.Empty(read.InvalidFields);
        if (outcome != AiToolOutcomeV1.Invalid)
        {
            Assert.Throws<ArgumentException>(() => new AiReadResultV1(outcome, invalidFields: ["private-field"]));
            Assert.Throws<ArgumentException>(() => new AiDraftResultV1(outcome, invalidFields: ["private-field"]));
            Assert.Throws<ArgumentException>(() => new AiPreviewResultV1(outcome, invalidFields: ["private-field"]));
        }
    }

    [Fact]
    public void Success_requires_content_and_metadata_classification_is_explicit()
    {
        Assert.Throws<ArgumentException>(() => new AiReadResultV1(AiToolOutcomeV1.Succeeded));
        Assert.Throws<ArgumentException>(() => new AiDraftResultV1(AiToolOutcomeV1.Succeeded));
        Assert.Throws<ArgumentException>(() => new AiPreviewResultV1(AiToolOutcomeV1.Succeeded));
        Assert.Throws<ArgumentException>(() => Tool(permissions: ["same", "same"]));
        Assert.Throws<ArgumentException>(() => Tool(dataClass: (AiDataClassV1)0));
        Assert.Throws<ArgumentException>(() => Preview(sources: [Source() with { DataClass = (AiDataClassV1)99 }]));
        Assert.Equal("{\"a\":1,\"b\":2}", new AiReadResultV1(AiToolOutcomeV1.Succeeded, "{\"b\":2,\"a\":1}").ContentJson);
    }

    [Fact]
    public void Unknown_execution_and_missing_lookup_evidence_never_prove_not_recorded()
    {
        var request = Request();
        var unknown = new AiCommandResultV1(AiCommandOutcomeV1.Unknown);
        Assert.Equal(AiCommandOutcomeV1.Unknown, new AiOwnerLookupResultV1(request).Outcome);
        Assert.Equal(AiCommandOutcomeV1.Unknown,
            new AiOwnerLookupResultV1(request, new AiOwnerRecordedResultV1(SyntheticBinding(request), unknown)).Outcome);
        Assert.Throws<ArgumentException>(() => new AiCommandResultV1(AiCommandOutcomeV1.NotRecorded));
        Assert.Throws<ArgumentException>(() => new AiCommandResultV1(AiCommandOutcomeV1.Unknown, resultVersion: "v1"));
        Assert.Throws<ArgumentException>(() => new AiCommandResultV1(AiCommandOutcomeV1.Unknown, isReplay: true));
        Assert.Throws<ArgumentException>(() => new AiCommandResultV1(AiCommandOutcomeV1.Applied));
        Assert.Throws<ArgumentException>(() => new AiCommandResultV1(AiCommandOutcomeV1.Refused));
    }

    [Fact]
    public void Owner_recorded_applied_refused_and_replay_results_keep_their_exact_request_binding()
    {
        var request = Request();
        var results = new[]
        {
            new AiCommandResultV1(AiCommandOutcomeV1.Applied, "result-1"),
            new AiCommandResultV1(AiCommandOutcomeV1.Applied, "result-1", isReplay: true),
            new AiCommandResultV1(AiCommandOutcomeV1.Refused, refusalCode: "synthetic-refusal"),
        };
        foreach (var result in results)
        {
            var envelope = new AiOwnerRecordedResultV1(SyntheticBinding(request), result);
            var lookup = new AiOwnerLookupResultV1(request, envelope);
            Assert.Equal(result.Outcome, lookup.Outcome);
            Assert.Same(envelope, lookup.RecordedResult);
            var captured = Assert.IsType<AiOwnerRecordedResultV1>(lookup.RecordedResult);
            Assert.Equal(result.ResultVersion, captured.Result.ResultVersion);
            Assert.Equal(result.RefusalCode, captured.Result.RefusalCode);
            Assert.Equal(result.IsReplay, captured.Result.IsReplay);
            Assert.Equal(request.RequestIdentityJson, envelope.Binding.RequestIdentityJson);
            Assert.Equal(request.CommandContext.IdempotencyKey, envelope.Binding.IdempotencyKey);
        }
    }

    [Fact]
    public void A_request_cannot_consume_another_request_or_contexts_recorded_result()
    {
        var requestA = Request();
        var variants = new[]
        {
            Request(context: Context() with { ActorId = "actor-B" }),
            Request(context: Context() with { CompanyId = Target }),
            Request(context: Context() with { SessionId = "session-B" }),
            Request(context: Context() with { ScopeRevision = "scope-B" }),
            Request(preview: Preview(command: "{\"quantity\":2}")),
            Request(preview: Preview(id: ApprovalId)),
            Request(approvalId: Target),
        };
        foreach (var requestB in variants)
        {
            var resultB = new AiOwnerRecordedResultV1(SyntheticBinding(requestB),
                new AiCommandResultV1(AiCommandOutcomeV1.Applied, "synthetic-B-version"));
            Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(requestA, resultB));
        }
        // Same preview/key/body but another approval is still another frozen request.
        var anotherApproval = Request(approvalId: Target);
        Assert.Equal(requestA.PreviewIdentityJson, anotherApproval.PreviewIdentityJson);
        Assert.Equal(requestA.CommandContext.IdempotencyKey, anotherApproval.CommandContext.IdempotencyKey);
        Assert.NotEqual(requestA.RequestIdentityJson, anotherApproval.RequestIdentityJson);
    }

    [Fact]
    public void Key_owner_command_source_contract_and_partial_evidence_mismatches_are_rejected()
    {
        var request = Request();
        var bindings = new[]
        {
            SyntheticBinding(request, key: "synthetic-another-key"),
            SyntheticBinding(request, owner: "OtherModule"),
            SyntheticBinding(request, command: "OtherCommand"),
            SyntheticBinding(request, sourceContract: "other-source-contract"),
            SyntheticBinding(request, identity: request.PreviewIdentityJson), // No approval/key envelope.
            SyntheticBinding(request, observed: Created), // Evidence predates approval.
        };
        foreach (var binding in bindings)
        {
            var recorded = new AiOwnerRecordedResultV1(binding, new AiCommandResultV1(AiCommandOutcomeV1.Applied, "v1"));
            Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(request, recorded));
            Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(request,
                observation: new AiOwnerLookupObservationV1(binding, AiOwnerObservationStateV1.Absent)));
            Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(request,
                definitiveNoEffect: SyntheticDefinitiveNoEffect(binding)));
        }
        Assert.Throws<ArgumentException>(() => SyntheticBinding(request, identity: ""));
        Assert.Throws<ArgumentException>(() => SyntheticBinding(request, key: ""));
        Assert.Throws<ArgumentException>(() => SyntheticBinding(request, owner: ""));
        Assert.Throws<ArgumentException>(() => SyntheticBinding(request, command: ""));
        Assert.Throws<ArgumentException>(() => SyntheticBinding(request, sourceContract: ""));
        Assert.Throws<ArgumentException>(() => new AiOwnerEvidenceBindingV1(request.RequestIdentityJson,
            request.CommandContext.IdempotencyKey, "Sample", "SampleCommand", "source-1", "", Created.AddMinutes(2)));
    }

    [Theory]
    [InlineData(AiOwnerObservationStateV1.Absent)]
    [InlineData(AiOwnerObservationStateV1.Pending)]
    [InlineData(AiOwnerObservationStateV1.InFlight)]
    [InlineData(AiOwnerObservationStateV1.Uncertain)]
    public void Even_late_bound_owner_observations_cannot_prove_no_future_effect(AiOwnerObservationStateV1 state)
    {
        var request = Request();
        var observation = new AiOwnerLookupObservationV1(SyntheticBinding(request, observed: Created.AddDays(7)), state);
        var lookup = new AiOwnerLookupResultV1(request, observation: observation);
        Assert.Equal(AiCommandOutcomeV1.Unknown, lookup.Outcome);
        Assert.Null(lookup.RecordedResult);
        Assert.Null(lookup.DefinitiveNoEffect);
        Assert.Same(observation, lookup.Observation);
        // No finality producer or owner method is invoked; no-row/time/reference are insufficient.
    }

    [Fact]
    public void Only_explicit_exact_bound_definitive_owner_no_effect_conclusion_can_be_not_recorded()
    {
        var request = Request();
        // SENTETIK: a typed owner conclusion, not fabricated evidence that a real command was queried.
        var finality = SyntheticDefinitiveNoEffect(SyntheticBinding(request));
        var lookup = new AiOwnerLookupResultV1(request, definitiveNoEffect: finality);
        Assert.Equal(AiCommandOutcomeV1.NotRecorded, lookup.Outcome);
        Assert.Same(finality, lookup.DefinitiveNoEffect);
        Assert.Null(lookup.RecordedResult); Assert.Null(lookup.Observation);
        Assert.Equal("ai:" + PreviewId.ToString("D"), request.CommandContext.IdempotencyKey);
        Assert.Throws<ArgumentException>(() => new AiOwnerDefinitiveNoEffectEvidenceV1(SyntheticBinding(request),
            (AiOwnerNoEffectConclusionV1)0, "SENTETIK-finality"));
        Assert.Throws<ArgumentException>(() => new AiOwnerDefinitiveNoEffectEvidenceV1(SyntheticBinding(request),
            AiOwnerNoEffectConclusionV1.DefinitivelyNotAdmittedAndCannotTakeEffect, ""));
        var anotherRequest = Request(approvalId: Target);
        Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(anotherRequest, definitiveNoEffect: finality));
        var otherBody = Request(preview: Preview(command: "{\"quantity\":2}"));
        Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(otherBody, definitiveNoEffect: finality));
    }

    [Fact]
    public void An_absent_observation_can_be_followed_by_a_later_commit_for_the_same_request()
    {
        var request = Request();
        var absent = new AiOwnerLookupObservationV1(SyntheticBinding(request), AiOwnerObservationStateV1.Absent);
        Assert.Equal(AiCommandOutcomeV1.Unknown, new AiOwnerLookupResultV1(request, observation: absent).Outcome);
        var laterRecorded = new AiOwnerRecordedResultV1(SyntheticBinding(request, observed: Created.AddMinutes(3)),
            new AiCommandResultV1(AiCommandOutcomeV1.Applied, "SENTETIK-later-commit"));
        Assert.Equal(AiCommandOutcomeV1.Applied, new AiOwnerLookupResultV1(request, laterRecorded).Outcome);
        Assert.Equal(absent.Binding.IdempotencyKey, laterRecorded.Binding.IdempotencyKey);
        Assert.Equal(absent.Binding.RequestIdentityJson, laterRecorded.Binding.RequestIdentityJson);
    }

    [Fact]
    public void Conflicting_record_observation_or_finality_evidence_is_not_accepted()
    {
        var request = Request(); var binding = SyntheticBinding(request);
        var recorded = new AiOwnerRecordedResultV1(binding, new AiCommandResultV1(AiCommandOutcomeV1.Applied, "v1"));
        var observation = new AiOwnerLookupObservationV1(binding, AiOwnerObservationStateV1.InFlight);
        var finality = SyntheticDefinitiveNoEffect(binding);
        Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(request, recorded, observation));
        Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(request, recorded, definitiveNoEffect: finality));
        Assert.Throws<ArgumentException>(() => new AiOwnerLookupResultV1(request, observation: observation, definitiveNoEffect: finality));
        Assert.Throws<ArgumentException>(() => new AiOwnerLookupObservationV1(binding, (AiOwnerObservationStateV1)0));
    }

    [Fact]
    public void Owner_request_requires_the_full_approved_snapshot_and_matching_audit()
    {
        var request = Request();
        var context = request.ServerContext; var tool = request.Tool; var preview = request.Preview;
        Assert.Throws<ArgumentException>(() => new AiOwnerCommandRequestV1(tool, context, preview,
            request.CommandContext, request.CommandContext.Audit.PreviewHash));
        Assert.Throws<ArgumentException>(() => new AiOwnerCommandRequestV1(tool, context, preview,
            request.CommandContext, "{}"));
        Assert.Throws<ArgumentException>(() => new AiOwnerCommandRequestV1(tool, context with { ActorId = "other" }, preview,
            request.CommandContext, request.PreviewIdentityJson));
        var changedContext = context with { ActorId = "other" };
        Assert.Throws<ArgumentException>(() => new AiOwnerCommandRequestV1(tool, changedContext, preview,
            request.CommandContext, AiCanonicalJson.PreviewIdentity(changedContext, tool, preview)));
        Assert.Throws<ArgumentException>(() => new AiOwnerCommandRequestV1(Tool(revision: Revision() with { LimitPolicy = "other" }),
            context, preview, request.CommandContext, request.PreviewIdentityJson));
        Assert.Throws<ArgumentException>(() => new AiOwnerCommandRequestV1(tool, context, Preview(targetId: Company),
            request.CommandContext, request.PreviewIdentityJson));
        var readTool = Tool(risk: AiToolRiskClassV1.Read);
        var readAudit = new AiAuditContextV1(context, readTool, preview, ApprovalId, "human-1", Created.AddMinutes(1));
        Assert.Throws<ArgumentException>(() => new AiOwnerCommandRequestV1(readTool, context, preview,
            new AiCommandContextV1(preview.Id, readAudit), AiCanonicalJson.PreviewIdentity(context, readTool, preview)));
        Assert.Throws<ArgumentException>(() => new AiCommandContextV1(Target, request.CommandContext.Audit));
    }

    [Fact]
    public void Audit_serializes_only_identity_and_version_evidence_without_raw_payload_or_fabricated_deployment()
    {
        var preview = Preview(command: "{\"note\":\"SENTETIK_SECRET_PAYLOAD\"}",
            difference: "{\"note\":\"SENTETIK_DIFFERENCE\"}",
            sources: [Source() with { Title = "SENTETIK_SOURCE_TITLE" }]);
        var audit = new AiAuditContextV1(Context(), Tool(), preview, ApprovalId, "human-1", Created.AddMinutes(1));
        var json = audit.ToJson();
        Assert.DoesNotContain("SENTETIK_SECRET_PAYLOAD", json);
        Assert.DoesNotContain("SENTETIK_DIFFERENCE", json);
        Assert.DoesNotContain("SENTETIK_SOURCE_TITLE", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("modelDeployment").ValueKind);
        Assert.Equal("scope-1", document.RootElement.GetProperty("scopeRevision").GetString());
        Assert.Equal("input-1", document.RootElement.GetProperty("contract").GetProperty("inputSchema").GetString());
        Assert.Equal(AiCanonicalJson.PreviewHash(Context(), Tool(), preview), audit.PreviewHash);
    }

    [Fact]
    public void Empty_identity_non_utc_and_out_of_interval_approval_evidence_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => AiCanonicalJson.PreviewIdentity(Context() with { CompanyId = Guid.Empty }, Tool(), Preview()));
        Assert.Throws<ArgumentException>(() => Preview(targetId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Preview(created: Created.ToOffset(TimeSpan.FromHours(3))));
        Assert.Throws<ArgumentException>(() => Preview(expires: Created));
        Assert.Throws<ArgumentException>(() => new AiAuditContextV1(Context(), Tool(), Preview(), ApprovalId, "human-1", Created.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(() => new AiAuditContextV1(Context(), Tool(), Preview(), ApprovalId, "human-1", Created.AddMinutes(15)));
    }

    [Fact]
    public void Tool_id_syntax_is_not_registry_admission_or_a_database_capability()
    {
        // The pure shape checker must not be mistaken for a registry allowlist or permission check.
        Assert.True(AiToolDescriptorV1.IsWellFormedToolId("sql.raw.v1"));
        Assert.False(AiToolDescriptorV1.IsWellFormedToolId("https://example.invalid/tool"));
        Assert.False(AiToolDescriptorV1.IsWellFormedToolId("sample.read.v0"));
        Assert.False(AiToolDescriptorV1.IsWellFormedToolId("sample.read.v1\n"));
        Assert.False(AiToolDescriptorV1.IsWellFormedToolId(null));
    }

    private static AiServerContextV1 Context() => new(Company, "actor-1", "session-1", "scope-1", "authority-1",
        "correlation-1", null, null, "prompt-1");
    private static AiContractRevisionV1 Revision() => new("input-1", "output-1", "field-1", "limit-1",
        "catalogue-1", "auth-1", "source-1", "redaction-1");
    private static AiToolDescriptorV1 Tool(AiContractRevisionV1? revision = null, string version = "1",
        string toolId = "sample.preview.v1", string owner = "Sample", string command = "SampleCommand",
        IEnumerable<string>? permissions = null, IEnumerable<string>? fields = null,
        AiDataClassV1 dataClass = AiDataClassV1.Internal, AiToolRiskClassV1 risk = AiToolRiskClassV1.Write) =>
        new(toolId, version, owner, risk, dataClass, permissions ?? ["sample.read"], fields ?? ["quantity"],
            risk == AiToolRiskClassV1.Write ? command : null, revision ?? Revision());
    private static AiSourceV1 Source() => new("sample.read.v1", "1", "sample", Target,
        "Synthetic source", "source-1", Created, AiDataClassV1.Internal);
    private static AiPreviewV1 Preview(Guid? id = null, Guid? targetId = null, string targetKind = "sample",
        string targetVersion = "target-1", string command = "{\"quantity\":1}",
        string difference = "{\"quantity\":{\"from\":0,\"to\":1}}", IEnumerable<AiSourceV1>? sources = null,
        DateTimeOffset? created = null, DateTimeOffset? expires = null) =>
        new(id ?? PreviewId, targetKind, targetId ?? Target, targetVersion, command, difference,
            sources ?? [Source()], created ?? Created, expires ?? Created.AddMinutes(15));
    private static AiOwnerEvidenceBindingV1 SyntheticBinding(AiOwnerCommandRequestV1 request, string? identity = null,
        string? key = null, string? owner = null, string? command = null, string? sourceContract = null,
        DateTimeOffset? observed = null) => new(identity ?? request.RequestIdentityJson,
            key ?? request.CommandContext.IdempotencyKey, owner ?? request.Tool.OwnerModule,
            command ?? request.Tool.OwnerCommandName!, sourceContract ?? request.Tool.Contract.SourceContract,
            "SENTETIK-owner-result-reference", observed ?? Created.AddMinutes(2));
    private static AiOwnerDefinitiveNoEffectEvidenceV1 SyntheticDefinitiveNoEffect(AiOwnerEvidenceBindingV1 binding) =>
        new(binding, AiOwnerNoEffectConclusionV1.DefinitivelyNotAdmittedAndCannotTakeEffect, "SENTETIK-definitive-no-effect");
    private static AiOwnerCommandRequestV1 Request(AiServerContextV1? context = null,
        AiPreviewV1? preview = null, Guid? approvalId = null)
    {
        context ??= Context(); var tool = Tool(); preview ??= Preview();
        var audit = new AiAuditContextV1(context, tool, preview, approvalId ?? ApprovalId, "human-1", Created.AddMinutes(1));
        return new(tool, context, preview, new AiCommandContextV1(preview.Id, audit),
            AiCanonicalJson.PreviewIdentity(context, tool, preview));
    }
}
