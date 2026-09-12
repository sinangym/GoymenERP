using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.CariAcceptance006;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class ContactJourneyMeasurementTests
{
    [Fact]
    public void TwoPagesKeepIndependentSequencesAndAConditionalErrorAck()
    {
        var identity = new RunSettings();
        var primary = Attempts(identity, true);
        var secondary = Attempts(identity, false);
        Assert.Equal(6, ContactJourneyMeasurement.ReadAttempts(identity, Element(primary), true).Length);
        Assert.Equal(2, ContactJourneyMeasurement.ReadAttempts(identity, Element(secondary), false).Length);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("sequence")]
    [InlineData("company")]
    [InlineData("declaration")]
    [InlineData("key")]
    [InlineData("status")]
    [InlineData("ack")]
    [InlineData("replay")]
    public void MissingOrMisboundActualExchangeIsRejected(string mutation)
    {
        var identity = new RunSettings();
        var primary = Attempts(identity, true);
        switch (mutation)
        {
            case "count": primary.RemoveAt(5); break;
            case "sequence": primary[2]!["seq"] = 1; break;
            case "company": primary[2]!["companyId"] = identity.CompanyBId; break;
            case "declaration": primary[2]!["contractDeclaration"] = "wrong"; break;
            case "key": primary[2]!["idempotencyKey"] = ""; break;
            case "status": primary[2]!["status"] = 409; break;
            case "ack": primary[2]!["contractAck"] = null; break;
            case "replay": primary[1]!["replayed"] = null; break;
        }
        Assert.Throws<InvalidOperationException>(() => ContactJourneyMeasurement.ReadAttempts(identity, Element(primary), true));
    }

    [Fact]
    public void TypedRequestCannotSilentlyCoerceANumericVersion()
    {
        var identity = new RunSettings();
        var primary = Attempts(identity, true);
        primary[2]!["requestBody"]!["expectedVersion"] = 2;
        Assert.Throws<JsonException>(() => ContactJourneyMeasurement.ReadAttempts(identity, Element(primary), true));
    }

    [Fact]
    public void ExternalContractMatchesActualHeadAndDoesNotReplaceApiDependency()
    {
        var contract = Contract();
        SourceBinding.ValidateExternalContract(Element(contract), new string('a', 40), new string('a', 40));
        contract["codex"]!["baseHead"] = new string('a', 40);
        Assert.Throws<InvalidOperationException>(() => SourceBinding.ValidateExternalContract(Element(contract), new string('a', 40), new string('a', 40)));
    }

    [Fact]
    public void ExternalContractHashComesFromTheSameValidatedByteSnapshot()
    {
        var contract = Contract();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(contract);
        Assert.Equal(SourceBinding.Hash(bytes), SourceBinding.ValidateAndHashExternalContract(bytes,
            new string('a', 40), new string('a', 40)));
        contract["codex"]!["acceptanceHead"] = new string('b', 40);
        var changed = JsonSerializer.SerializeToUtf8Bytes(contract);
        Assert.Throws<InvalidOperationException>(() => SourceBinding.ValidateAndHashExternalContract(changed,
            new string('a', 40), new string('a', 40)));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("compiled")]
    [InlineData("frontend")]
    [InlineData("base")]
    [InlineData("diff")]
    [InlineData("port")]
    [InlineData("cwd")]
    public void StaleExternalPinsCannotBindTheNewRuntime(string mutation)
    {
        var contract = Contract();
        switch (mutation)
        {
            case "head": contract["codex"]!["acceptanceHead"] = new string('b', 40); break;
            case "compiled": contract["codex"]!["compiledHead"] = new string('b', 40); break;
            case "frontend": contract["claude"]!["baseHead"] = new string('b', 40); break;
            case "base": contract["claude"]!["frontendDiffBase"] = new string('b', 40); break;
            case "diff": contract["claude"]!["frontendDiffSha256"] = new string('b', 64); break;
            case "port": contract["suggestedPorts"]!["ui"] = 25174; break;
            case "cwd": contract["codex"]!["cwd"] = "C:\\src\\other"; break;
        }
        Assert.Throws<InvalidOperationException>(() => SourceBinding.ValidateExternalContract(Element(contract), new string('a', 40), new string('a', 40)));
    }

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    [Theory]
    [InlineData("valid")]
    [InlineData("run")]
    [InlineData("instance")]
    [InlineData("notReady")]
    [InlineData("environmentHash")]
    [InlineData("baselineHash")]
    [InlineData("contractHash")]
    [InlineData("source")]
    public void SqlVerificationIsBoundToTheActualPublishedEnvironmentAndBaseline(string mutation)
    {
        var identity = new RunSettings();
        var source = new SourceSnapshot(RunSettings.ApprovedRoot, new string('a', 40), new string('a', 40),
            RunSettings.ExpectedHead, SourceBinding.BackendTree, SourceBinding.OpenApiHash, SourceBinding.MigrationHash,
            SourceBinding.FrontendRoot, SourceBinding.FrontendHead, SourceBinding.FrontendDiff, new string('b', 64),
            identity.RunId, identity.InstanceId, identity.DatabaseName, identity.ApiBaseUrl, identity.SqlEndpoint,
            true, new(StringComparer.Ordinal) { ["GoymenERP.CariAcceptance006.dll"] = new string('c', 64) });
        var environment = new JsonObject
        {
            ["runId"] = identity.RunId, ["instanceId"] = identity.InstanceId, ["readyForBrowser"] = true,
            ["baselineSha256"] = new string('2', 64), ["contractSha256"] = new string('4', 64),
            ["sources"] = JsonSerializer.SerializeToNode(source),
        };
        var ready = new JsonObject
        {
            ["runId"] = identity.RunId, ["instanceId"] = identity.InstanceId, ["readyForBrowser"] = true,
            ["environmentSha256"] = new string('1', 64),
        };
        switch (mutation)
        {
            case "run": ready["runId"] = "old-run"; break;
            case "instance": environment["instanceId"] = "old-instance"; break;
            case "notReady": environment["readyForBrowser"] = false; break;
            case "environmentHash": ready["environmentSha256"] = new string('3', 64); break;
            case "baselineHash": environment["baselineSha256"] = new string('3', 64); break;
            case "contractHash": environment["contractSha256"] = new string('3', 64); break;
            case "source": environment["sources"]!["CompiledHead"] = new string('d', 40); break;
        }
        void Validate() => ContactJourneyMeasurement.ValidatePublication(identity, source, Element(environment),
            Element(ready), new string('1', 64), new string('2', 64), new string('4', 64));
        if (mutation == "valid") Validate();
        else Assert.Throws<InvalidOperationException>(Validate);
    }

    private static JsonObject Contract() => new()
    {
        ["id"] = "CARI-AB-20260905-006-009",
        ["codex"] = new JsonObject
        {
            ["taskId"] = "C-DESKTOP-006", ["cwd"] = RunSettings.ApprovedRoot, ["baseHead"] = RunSettings.ExpectedHead,
            ["acceptanceHead"] = new string('a', 40), ["compiledHead"] = new string('a', 40),
        },
        ["claude"] = new JsonObject
        {
            ["baseHead"] = SourceBinding.FrontendHead, ["frontendDiffBase"] = SourceBinding.FrontendBase,
            ["frontendDiffSha256"] = SourceBinding.FrontendDiff,
        },
        ["suggestedPorts"] = new JsonObject { ["api"] = 25080, ["sql"] = 25433, ["ui"] = 25173 },
    };

    private static JsonArray Attempts(RunSettings identity, bool primary)
    {
        var values = new JsonArray();
        for (var i = 0; i < (primary ? 6 : 2); i++)
            values.Add(new JsonObject
            {
                ["seq"] = i, ["companyId"] = identity.CompanyAId,
                ["contractDeclaration"] = SourceBinding.ClientContractVersion,
                ["idempotencyKey"] = "key-" + i,
                ["status"] = primary && i == 0 ? null : JsonValue.Create(!primary && i == 0 ? 409 : 204),
                ["replayed"] = primary && i == 1 ? "true" : null,
                ["contractAck"] = i == 0 ? null : SourceBinding.ClientContractVersion,
                ["requestBody"] = new JsonObject { ["expectedVersion"] = "1", ["changes"] = new JsonObject() },
            });
        return values;
    }
}
