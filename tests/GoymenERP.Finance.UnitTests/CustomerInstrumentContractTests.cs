using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// DEC-FIN-016 first release, pure: the state machine the decision names (portföy → banka → tahsil; karşılıksız /
/// protesto; iade; the pre-collection reverse flow), the receive checks and the wire shape. SENTETIK / NOT_ENV.
/// </summary>
public sealed class CustomerInstrumentContractTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string Operation = "SENTETIK-operation";

    [Theory]
    [InlineData(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentTransition.DepositForCollection, CustomerInstrumentStatus.AtBankForCollection)]
    [InlineData(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentTransition.WithdrawFromBank, CustomerInstrumentStatus.InPortfolio)]
    [InlineData(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentTransition.Dishonour, CustomerInstrumentStatus.Bounced)]
    [InlineData(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentTransition.Dishonour, CustomerInstrumentStatus.Bounced)]
    [InlineData(CustomerInstrumentKind.PromissoryNote, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentTransition.Dishonour, CustomerInstrumentStatus.Protested)]
    [InlineData(CustomerInstrumentKind.PromissoryNote, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentTransition.Dishonour, CustomerInstrumentStatus.Protested)]
    [InlineData(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentTransition.ReturnToCustomer, CustomerInstrumentStatus.ReturnedToCustomer)]
    [InlineData(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.Bounced, CustomerInstrumentTransition.ReturnToCustomer, CustomerInstrumentStatus.ReturnedToCustomer)]
    [InlineData(CustomerInstrumentKind.PromissoryNote, CustomerInstrumentStatus.Protested, CustomerInstrumentTransition.ReturnToCustomer, CustomerInstrumentStatus.ReturnedToCustomer)]
    public void The_decided_steps_lead_where_DEC_FIN_016_says(CustomerInstrumentKind kind, CustomerInstrumentStatus from,
        CustomerInstrumentTransition transition, CustomerInstrumentStatus to)
    {
        Assert.Equal(to, CustomerInstrumentRules.Target(kind, from, transition));
        Assert.True(CustomerInstrumentRules.IsAllowedStatusChange(kind, from, to));
    }

    [Fact]
    public void Collected_and_returned_are_final_and_nothing_else_is_allowed()
    {
        var allowed = new HashSet<(CustomerInstrumentKind, CustomerInstrumentStatus, CustomerInstrumentStatus)>();
        foreach (var kind in Enum.GetValues<CustomerInstrumentKind>())
        {
            foreach (var from in Enum.GetValues<CustomerInstrumentStatus>())
            {
                foreach (var to in Enum.GetValues<CustomerInstrumentStatus>())
                {
                    if (CustomerInstrumentRules.IsAllowedStatusChange(kind, from, to))
                    {
                        allowed.Add((kind, from, to));
                    }
                }
            }
        }

        var cheque = CustomerInstrumentKind.Cheque;
        var note = CustomerInstrumentKind.PromissoryNote;
        var expected = new HashSet<(CustomerInstrumentKind, CustomerInstrumentStatus, CustomerInstrumentStatus)>
        {
            (cheque, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.AtBankForCollection),
            (cheque, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.Collected),
            (cheque, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.Bounced),
            (cheque, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.ReturnedToCustomer),
            (cheque, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentStatus.InPortfolio),
            (cheque, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentStatus.Collected),
            (cheque, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentStatus.Bounced),
            (cheque, CustomerInstrumentStatus.Bounced, CustomerInstrumentStatus.ReturnedToCustomer),
            (note, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.AtBankForCollection),
            (note, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.Collected),
            (note, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.Protested),
            (note, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.ReturnedToCustomer),
            (note, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentStatus.InPortfolio),
            (note, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentStatus.Collected),
            (note, CustomerInstrumentStatus.AtBankForCollection, CustomerInstrumentStatus.Protested),
            (note, CustomerInstrumentStatus.Protested, CustomerInstrumentStatus.ReturnedToCustomer),
        };
        Assert.Equal(expected.OrderBy(x => x.ToString()), allowed.OrderBy(x => x.ToString()));
        foreach (var kind in Enum.GetValues<CustomerInstrumentKind>())
        {
            Assert.Empty(CustomerInstrumentRules.StateActions(kind, CustomerInstrumentStatus.Collected));
            Assert.Empty(CustomerInstrumentRules.StateActions(kind, CustomerInstrumentStatus.ReturnedToCustomer));
        }

        // Endorsement (ciro) and bank collateral (teminat) are not offered anywhere: they wait for their decisions.
        Assert.DoesNotContain(Enum.GetNames<CustomerInstrumentTransition>(), name =>
            name.Contains("Endorse", StringComparison.Ordinal) || name.Contains("Collateral", StringComparison.Ordinal));
    }

    [Fact]
    public void The_state_actions_are_the_state_machine_in_a_stable_order()
    {
        Assert.Equal(
            [CustomerInstrumentAction.DepositForCollection, CustomerInstrumentAction.Dishonour,
                CustomerInstrumentAction.ReturnToCustomer, CustomerInstrumentAction.Collect],
            CustomerInstrumentRules.StateActions(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.InPortfolio));
        Assert.Equal(
            [CustomerInstrumentAction.WithdrawFromBank, CustomerInstrumentAction.Dishonour, CustomerInstrumentAction.Collect],
            CustomerInstrumentRules.StateActions(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.AtBankForCollection));
        Assert.Equal([CustomerInstrumentAction.ReturnToCustomer],
            CustomerInstrumentRules.StateActions(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.Bounced));
    }

    [Fact]
    public void A_receive_request_is_refused_for_each_missing_or_oversized_fact_and_never_trimmed()
    {
        Assert.Empty(CustomerInstrumentRules.ValidateShape(Receive()));
        Assert.Empty(CustomerInstrumentRules.ValidateShape(Receive() with
        {
            Kind = CustomerInstrumentKind.PromissoryNote, BankName = null, BranchName = null,
        }));

        Assert.Equal(new ValidationIssue("bankName", CustomerInstrumentRules.BankNameRequired),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with { BankName = null })));
        Assert.Equal(new ValidationIssue("serialNumber", CustomerInstrumentRules.SerialNumberRequired),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with { SerialNumber = " " })));
        Assert.Equal(new ValidationIssue("serialNumber", CustomerInstrumentRules.ValueDoesNotFit),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with { SerialNumber = " CEK-1" })));
        Assert.Equal(new ValidationIssue("serialNumber", CustomerInstrumentRules.ValueDoesNotFit),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with { SerialNumber = new string('9', 65) })));
        Assert.Equal(new ValidationIssue("drawer", CustomerInstrumentRules.DrawerRequired),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with { Drawer = "" })));
        Assert.Equal(new ValidationIssue("description", CustomerInstrumentRules.ValueDoesNotFit),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with { Description = new string('a', 1001) })));
        Assert.Equal(new ValidationIssue("kind", CustomerInstrumentRules.KindInvalid),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with
            {
                Kind = (CustomerInstrumentKind)9, BankName = "SENTETIK Banka",
            })));
        Assert.Equal(new ValidationIssue("identity", "identity_required"),
            Assert.Single(CustomerInstrumentRules.ValidateShape(Receive() with { PartyId = Guid.Empty })));
    }

    [Fact]
    public void The_face_value_is_judged_by_the_approved_profile_exactly_and_never_rounded()
    {
        var profile = new MoneyProfileV1(Company, Currency, "TRY", Operation, new ProfileVersionV1(Profile, 3), 2, true,
            new DateOnly(2026, 1, 1), null);
        Assert.Empty(CustomerInstrumentRules.ValidateMoney(Receive(), Company, profile, Operation));
        Assert.Equal(new ValidationIssue("money.amount", "settings_decimal_scale_exceeded"),
            Assert.Single(CustomerInstrumentRules.ValidateMoney(Receive("1000.005"), Company, profile, Operation)));
        Assert.Equal("profile_mismatch", Assert.Single(CustomerInstrumentRules.ValidateMoney(Receive(), Company,
            profile with { EffectiveFrom = new DateOnly(2026, 12, 1) }, Operation)).Code);
        Assert.Equal("profile_mismatch", Assert.Single(CustomerInstrumentRules.ValidateMoney(Receive(), Guid.NewGuid(),
            profile, Operation)).Code);
        Assert.Equal("settings_provider_unavailable",
            Assert.Single(CustomerInstrumentRules.ValidateMoney(Receive(), Company, null, Operation)).Code);
        Assert.Equal(new ProfileReference(Profile.ToString("D"), "3"), CustomerInstrumentRules.Reference(profile));
    }

    [Fact]
    public void The_wire_carries_enum_names_and_refuses_unknown_members()
    {
        var json = JsonSerializer.Serialize(Receive(), FinanceJson.CreateOptions());
        using (var document = JsonDocument.Parse(json))
        {
            Assert.Equal("Cheque", document.RootElement.GetProperty("kind").GetString());
            Assert.Equal("2026-09-20", document.RootElement.GetProperty("receivedOn").GetString());
            Assert.Equal("1000.00", document.RootElement.GetProperty("money").GetProperty("amount").GetString());
        }

        var change = JsonSerializer.Serialize(new CustomerInstrumentStatusChangeRequest(Guid.NewGuid(), "1",
            CustomerInstrumentTransition.DepositForCollection, new DateOnly(2026, 9, 21), Guid.NewGuid(), null),
            FinanceJson.CreateOptions());
        Assert.Contains("\"transition\":\"DepositForCollection\"", change, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CustomerInstrumentCollectRequest>(
            """{"instrumentId":"11111111-1111-4111-8111-111111111111","expectedVersion":"1","treasuryAccountId":"33333333-3333-4333-8333-333333333333","collectedOn":"2026-09-22","valueOn":null,"amount":"1"}""",
            FinanceJson.CreateOptions()));
    }

    [Fact]
    public void The_payload_hash_names_the_command_and_every_field()
    {
        var request = Receive();
        var hash = CustomerInstrumentService.PayloadHash(CustomerInstrumentService.ReceiveCommand, request);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, CustomerInstrumentService.PayloadHash(CustomerInstrumentService.ReceiveCommand, Receive()));
        Assert.NotEqual(hash, CustomerInstrumentService.PayloadHash(CustomerInstrumentService.CollectCommand, request));
        Assert.NotEqual(hash, CustomerInstrumentService.PayloadHash(CustomerInstrumentService.ReceiveCommand,
            request with { DueOn = request.DueOn.AddDays(1) }));
        Assert.NotEqual(hash, CustomerInstrumentService.PayloadHash(CustomerInstrumentService.ReceiveCommand,
            request with { Money = new MoneyText("1000.0", Currency) }));
    }

    [Fact]
    public void Each_portfolio_action_has_its_own_narrow_permission_name()
    {
        Assert.Equal("finance.customer-instrument.read", FinancePermissionPort.NameOf(FinanceAction.ReadCustomerInstruments));
        Assert.Equal("finance.customer-instrument.amount.read",
            FinancePermissionPort.NameOf(FinanceAction.ReadCustomerInstrumentAmount));
        Assert.Equal("finance.customer-instrument.receive", FinancePermissionPort.NameOf(FinanceAction.ReceiveCustomerInstrument));
        Assert.Equal("finance.customer-instrument.status.change",
            FinancePermissionPort.NameOf(FinanceAction.ChangeCustomerInstrumentStatus));
        Assert.Equal("finance.customer-instrument.collect", FinancePermissionPort.NameOf(FinanceAction.CollectCustomerInstrument));
    }

    private static CustomerInstrumentReceiveRequest Receive(string amount = "1000.00") => new(
        CustomerInstrumentKind.Cheque, Guid.Parse("44444444-4444-4444-8444-444444444444"),
        Guid.Parse("55555555-5555-4555-8555-555555555555"), "SENTETIK-CEK-0001", "SENTETIK Keşideci A.Ş.",
        "SENTETIK Banka", "SENTETIK Şube", new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 20),
        new MoneyText(amount, Currency), null);
}
