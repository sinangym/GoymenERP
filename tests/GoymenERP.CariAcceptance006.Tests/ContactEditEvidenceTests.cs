using GoymenERP.CariAcceptance006;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class ContactEditEvidenceTests
{
    [Fact]
    public void OneEditHasExactCanonicalStateAndZeroNewPartyOrAccount()
    {
        var (identity, before, after, party, company, information) = Fixture();
        DatabaseEvidence.VerifyContactEdit(identity, before, after, party, company, information);
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.VerifyBrowserJourney(identity, before, after, 1));
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.VerifyContactEdit(identity, before, after,
            party, Guid.Parse(identity.CompanyBId), information));
    }

    [Theory]
    [InlineData("party")]
    [InlineData("account")]
    [InlineData("audit")]
    [InlineData("execution")]
    [InlineData("global")]
    [InlineData("version")]
    [InlineData("profile")]
    [InlineData("otherCompany")]
    public void UnexpectedRecordEffectOrUnboundContentFailsClosed(string mutation)
    {
        var (identity, before, after, party, company, information) = Fixture();
        after = mutation switch
        {
            "party" => after with { Effects = after.Effects with { Parties = 2 } },
            "account" => after with { Effects = after.Effects with { Accounts = 3 } },
            "audit" => after with { Effects = after.Effects with { ContactAudits = 0 } },
            "execution" => after with { Effects = after.Effects with { ContactExecutions = 2 } },
            "global" => after with { PartyStateSha256 = new string('b', 64) },
            "version" => after with { Rows = [after.Rows[0] with { Version = "3" }, after.Rows[1]] },
            "profile" => after with { Rows = [after.Rows[0] with { ContactInformationJson = "{}" }, after.Rows[1]] },
            "otherCompany" => after with { Rows = [after.Rows[0], after.Rows[1] with { Version = "2" }] },
            _ => throw new ArgumentException(nameof(mutation)),
        };
        Assert.Throws<InvalidOperationException>(() => DatabaseEvidence.VerifyContactEdit(
            identity, before, after, party, company, information));
    }

    private static (RunSettings Identity, DatabaseSnapshot Before, DatabaseSnapshot After,
        Guid Party, Guid Company, CounterpartyContactInformation Information) Fixture()
    {
        var identity = new RunSettings();
        var party = Guid.Parse("91000000-0000-0000-0000-000000000001");
        var company = Guid.Parse(identity.CompanyAId);
        var a = new AccountRow(party, "CAR-TEST", "Synthetic", company, 0, "1");
        var b = a with { CompanyId = Guid.Parse(identity.CompanyBId) };
        var before = new DatabaseSnapshot(identity.DatabaseName, true, new Effects(1, 2, 1, 1, 1, 1),
            [a, b], new string('a', 64));
        var information = CounterpartyContactInformationEditor.Empty() with { Phone = "00123" };
        var after = before with
        {
            Effects = before.Effects with { ContactAudits = 1, ContactExecutions = 1 },
            Rows = [a with { Version = "2", ContactInformationJson = CounterpartyContactInformationEditor.Serialize(information) }, b],
        };
        return (identity, before, after, party, company, information);
    }
}
