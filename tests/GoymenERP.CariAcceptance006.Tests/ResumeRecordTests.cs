using System.Text.Json;
using GoymenERP.CariAcceptance006;

public sealed class ResumeRecordTests
{
    private static Dictionary<string, string> Valid() => new()
    {
        ["task_id"] = "C-DESKTOP-012", ["status"] = "in_progress",
        ["resourceRunId"] = RunSettings.ExpectedRun,
        ["instanceId"] = RunSettings.ExpectedInstance,
        ["executionRunId"] = "c012-778b7cbad73c"
    };

    [Fact]
    public void ExactResumeRecordReturnsNewExecutionIdentity()
    {
        Assert.Equal("c012-778b7cbad73c", Evidence.ValidateResumeRecord(JsonSerializer.Serialize(Valid())));
        Assert.NotEqual(RunSettings.ApprovedEvidence, Evidence.ResumeEvidence);
    }

    [Theory]
    [InlineData("task_id", "C-DESKTOP-006")]
    [InlineData("status", "completed")]
    [InlineData("resourceRunId", "another-run")]
    [InlineData("instanceId", "another-instance")]
    [InlineData("executionRunId", "c006-778b7cbad73c")]
    [InlineData("executionRunId", "c012-778b7cbad73c\n")]
    [InlineData("executionRunId", "../C-DESKTOP-006")]
    [InlineData("executionRunId", "c012-778B7CBAD73C")]
    [InlineData("executionRunId", "")]
    public void WrongResumeIdentityRejectedBeforeAnyLifecycleEffect(string field, string value)
    {
        var record = Valid(); record[field] = value;
        Assert.Throws<InvalidOperationException>(() => Evidence.ValidateResumeRecord(JsonSerializer.Serialize(record)));
    }

    [Theory]
    [InlineData("task_id")]
    [InlineData("status")]
    [InlineData("resourceRunId")]
    [InlineData("instanceId")]
    [InlineData("executionRunId")]
    public void MissingFieldRejected(string field)
    {
        var record = Valid(); record.Remove(field);
        Assert.Throws<KeyNotFoundException>(() => Evidence.ValidateResumeRecord(JsonSerializer.Serialize(record)));
    }

    [Fact]
    public void MalformedJsonRejected() => Assert.ThrowsAny<JsonException>(() => Evidence.ValidateResumeRecord("{"));
}
