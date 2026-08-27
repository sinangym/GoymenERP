using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Idempotency.UnitTests;

public sealed class CommandExecutionTests
{
    [Fact]
    public void KeyHash_UsesCanonicalValueAndNeverReturnsRawKey()
    {
        const string rawKey = "network-retry-key";

        var hash = IdempotencyHash.ComputeKey($"  {rawKey}  ");

        Assert.Equal(64, hash.Length);
        Assert.Equal(IdempotencyHash.ComputeKey(rawKey), hash);
        Assert.DoesNotContain(rawKey, hash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PayloadHash_IsDeterministicAndPayloadSensitive()
    {
        var first = IdempotencyHash.ComputePayload(new RequestFingerprint(true, 1, "gerekçe"));
        var replay = IdempotencyHash.ComputePayload(new RequestFingerprint(true, 1, "gerekçe"));
        var changed = IdempotencyHash.ComputePayload(new RequestFingerprint(false, 1, "gerekçe"));

        Assert.Equal(first, replay);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void CompletedExecution_RequiresValidHashesAndResponseJson()
    {
        var data = CreateData() with { RequestHash = "not-a-hash" };

        var hashException = Assert.Throws<ArgumentException>(() =>
            CommandExecution.Complete(data));
        var jsonException = Assert.Throws<ArgumentException>(() =>
            CommandExecution.Complete(CreateData() with { ResponseJson = "not-json" }));

        Assert.Contains("SHA-256", hashException.Message);
        Assert.Contains("JSON", jsonException.Message);
    }

    private static CommandExecutionData CreateData()
    {
        return new CommandExecutionData(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test.command.v1",
            new string('A', 64),
            new string('B', 64),
            Guid.NewGuid().ToString("D"),
            "Succeeded",
            204,
            "{\"result\":\"Succeeded\"}",
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            1);
    }

    private sealed record RequestFingerprint(bool IsActive, long Version, string Reason);
}
