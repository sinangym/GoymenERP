namespace GoymenERP.Identity.UnitTests;

public sealed class SqlServerRcsiReadinessCliTests
{
    private const string ValidFingerprint =
        "75a99409b81404222ad7347d4dad424dfab7b355844a736531e2105eb621c05f";

    [Fact]
    public async Task InvalidBinding_ReturnsDeterministicExitAndDoesNotEchoInput()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var arguments = ValidArguments();
        arguments[5] = "staging blue-SuperSecret";

        var exitCode = await global::SqlServerRcsiReadinessCli.RunAsync(
            arguments,
            _ => "Server=secret-host;Password=SuperSecret;",
            standardOutput,
            standardError);

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "\"reasonCode\":\"BINDING_INVALID\"",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SuperSecret",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret-host",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task MalformedSecretConnectionString_IsNotEchoed()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var exitCode = await global::SqlServerRcsiReadinessCli.RunAsync(
            ValidArguments(),
            _ => "Server=secret-host;Password=SuperSecret;UnknownKeyword=value;",
            standardOutput,
            standardError);

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "\"reasonCode\":\"BINDING_INVALID\"",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SuperSecret",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret-host",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(standardError.ToString());
    }

    [Fact]
    public async Task EvidenceWriteFailure_ReturnsProbeFailedJsonOnStandardOutput()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var arguments = ValidArguments()
            .Concat(["--output", "bad\0path"])
            .ToArray();

        var exitCode = await global::SqlServerRcsiReadinessCli.RunAsync(
            arguments,
            _ => null,
            standardOutput,
            standardError);

        Assert.Equal(5, exitCode);
        Assert.Contains(
            "\"reasonCode\":\"EVIDENCE_WRITE_FAILED\"",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "evidence could not be written",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    private static string[] ValidArguments() =>
    [
        "--connection-string-env",
        "GOYMEN_ERP_RCSI_PROBE_CONNECTION",
        "--expected-database",
        "GoymenErp",
        "--target-label",
        "staging/blue",
        "--expected-data-source-sha256",
        ValidFingerprint,
    ];
}
