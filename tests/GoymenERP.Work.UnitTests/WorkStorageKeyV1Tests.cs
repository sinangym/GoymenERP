using System.Security.Cryptography;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkStorageKeyV1Tests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Evidence = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Payload = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [MemberData(nameof(LosslessValues))]
    public void Explicit_utf16be_codec_round_trips_every_code_unit(string caseName, string value)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        var key = WorkStorageKeyV1.Create(value);

        var decoded = key.DecodeAndVerify();

        Assert.True(value.AsSpan().SequenceEqual(decoded.AsSpan()));
        Assert.Equal(value, decoded, StringComparer.Ordinal);
    }

    [Fact]
    public void Case_is_distinct_and_internal_whitespace_is_not_normalized()
    {
        var upper = WorkStorageKeyV1.Create("Source A");
        var lower = WorkStorageKeyV1.Create("source A");

        Assert.False(upper.Digest.AsSpan().SequenceEqual(lower.Digest));
        Assert.Equal("Source A", upper.DecodeAndVerify());
    }

    [Fact]
    public void Existing_domain_refuses_empty_outer_whitespace_and_all_whitespace_before_storage()
    {
        foreach (var source in new[] { "", " ", "source ", " source" })
        {
            var observation = Observation(source);
            Assert.Throws<ArgumentException>(() =>
                WorkDesiredStateProtocolV2.Reconcile(null, observation));
        }
    }

    [Fact]
    public void Corrupt_odd_bytes_and_hash_mismatch_fail_closed()
    {
        Assert.Throws<WorkStorageCorruptionException>(() =>
            new WorkStorageKeyV1([0x00], new byte[32]).DecodeAndVerify());

        var valid = WorkStorageKeyV1.Create("source");
        var wrong = valid.Digest.ToArray();
        wrong[0] ^= 0xff;
        Assert.Throws<WorkStorageCorruptionException>(() =>
            new WorkStorageKeyV1(valid.Utf16Be, wrong).DecodeAndVerify());
    }

    [Fact]
    public void Outbox_uses_base64_bytes_and_replays_unpaired_surrogate_exactly()
    {
        var sourceText = new string(['s', '\ud800', 'x']);
        var scopeText = new string(['w', '\udc00', 'y']);
        var source = WorkStorageKeyV1.Create(sourceText);
        var scope = WorkStorageKeyV1.Create(scopeText);
        var observation = new WorkDesiredStateObservationV2(
            Company, sourceText, scopeText, 1, WorkSourceAvailabilityV2.Available,
            WorkDesiredStateV2.NeedsAction, Evidence, Payload);
        var snapshot = WorkDesiredStateProtocolV2.Reconcile(null, observation).State!;
        var row = new WorkOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = Company,
            SourceKeyUtf16Be = source.Utf16Be,
            SourceKeyDigest = source.Digest,
            ScopeKeyUtf16Be = scope.Utf16Be,
            ScopeKeyDigest = scope.Digest,
            ObservationOrdinal = 1,
            AggregateVersion = 1,
            EventType = WorkDesiredStateStoreV2.AppliedEventType,
            SchemaVersion = 2,
            CorrelationId = "correlation",
            PayloadJson = WorkDesiredStateStoreV2.CreateOutboxPayloadJson(snapshot, source, scope, 1)
        };

        var restored = WorkDesiredStateStoreV2.DecodeOutboxPayload(row);

        Assert.True(sourceText.AsSpan().SequenceEqual(restored.SourceKey.AsSpan()));
        Assert.True(scopeText.AsSpan().SequenceEqual(restored.ScopeKey.AsSpan()));
        Assert.Contains(Convert.ToBase64String(source.Utf16Be), row.PayloadJson, StringComparison.Ordinal);

        row.EventType = "work.desired-state.wrong.v2";
        Assert.Throws<WorkStorageCorruptionException>(() => WorkDesiredStateStoreV2.DecodeOutboxPayload(row));
        row.EventType = WorkDesiredStateStoreV2.AppliedEventType;

        row.SchemaVersion = 1;
        Assert.Throws<WorkStorageCorruptionException>(() => WorkDesiredStateStoreV2.DecodeOutboxPayload(row));
        row.SchemaVersion = 2;

        row.AggregateVersion = 2;
        Assert.Throws<WorkStorageCorruptionException>(() => WorkDesiredStateStoreV2.DecodeOutboxPayload(row));
        row.AggregateVersion = 1;

        var payload = JsonNode.Parse(row.PayloadJson)!.AsObject();
        var effective = payload.Single(x => string.Equals(
            x.Key, "effectiveState", StringComparison.OrdinalIgnoreCase)).Value!.AsObject();
        var ordinal = effective.Single(x => string.Equals(
            x.Key, "observationOrdinal", StringComparison.OrdinalIgnoreCase)).Key;
        effective[ordinal] = null;
        row.PayloadJson = payload.ToJsonString();
        Assert.Throws<WorkStorageCorruptionException>(() => WorkDesiredStateStoreV2.DecodeOutboxPayload(row));
    }

    public static TheoryData<string, string> LosslessValues() => new()
    {
        { "ascii", "source-a" },
        { "internal-space", "source internal space" },
        { "paired-surrogate", "x\ud83d\ude80y" },
        { "isolated-high-surrogate", new string(['x', '\ud800', 'y']) },
        { "isolated-low-surrogate", new string(['x', '\udc00', 'y']) }
    };

    private static WorkDesiredStateObservationV2 Observation(string source) => new(
        Company,
        source,
        "scope",
        1,
        WorkSourceAvailabilityV2.Available,
        WorkDesiredStateV2.NeedsAction,
        Evidence,
        Payload);
}
