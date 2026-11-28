using System.Text.Json;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Organization.UnitTests;

public sealed partial class CompanyTaxIdApprovalSafetyTests
{
    [Fact]
    public async Task Genuine_private_receipt_carries_caption_and_first_flag_but_never_serializes_them()
    {
        await using var run = await PurposeRun.Start(true);
        var native = new CommandExecutionResult(CommandResponses.For(CommandResult.Succeeded), true, "SENTETIK-original");
        var receipt = Assert.IsType<OrganizationCompanyTaxIdDecisionReceiptV1>(await run.Reader.ReadDecisionReceiptAsync(RequestId,
            new(1, "approve", null), "SENTETIK-proof-key", native, _ => Task.CompletedTask));
        Assert.Equal("9000000033", receipt.ProposedVkn); Assert.False(receipt.FirstVkn);
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("proposedVkn", json); Assert.DoesNotContain("firstVkn", json);
        Assert.Equal(native, new CommandExecutionResult(CommandResponses.For(CommandResult.Succeeded), true, "SENTETIK-original"));
    }
}
