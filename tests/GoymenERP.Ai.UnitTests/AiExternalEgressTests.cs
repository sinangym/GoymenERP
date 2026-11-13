using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Ai.UnitTests;

// SINAN_KARARLARI_2026-11-03_AI AI-D3/AI-D4: what may leave for the external AI. Synthetic values only (SENTETIK).
public sealed class AiExternalEgressTests
{
    private const string Policy = "SENTETIK-ai-policy-v1";
    private static readonly AiSensitiveDataTypeV1[] AllTypes =
    [
        AiSensitiveDataTypeV1.Price, AiSensitiveDataTypeV1.Balance, AiSensitiveDataTypeV1.Iban,
        AiSensitiveDataTypeV1.FinanceAmount, AiSensitiveDataTypeV1.CompanyDocument,
    ];

    private static AiExternalEgressPolicyV1 Open(bool ibanReview = true, params AiSensitiveDataTypeV1[] allowed) =>
        new(true, allowed.Length == 0 ? AllTypes : allowed, ibanReview, Policy);

    private static AiFieldTagV1 Tag(AiDataClassV1 dataClass, AiSensitiveDataTypeV1 type = AiSensitiveDataTypeV1.None,
        bool never = false, string name = "field") => new(name, dataClass, type, never);

    [Theory]
    [InlineData(AiDataClassV1.Public)]
    [InlineData(AiDataClassV1.Internal)]
    [InlineData(AiDataClassV1.Confidential)]
    [InlineData(AiDataClassV1.StrictlyConfidential)]
    public void A_disabled_or_unconfigured_policy_sends_nothing(AiDataClassV1 dataClass)
    {
        var disabled = AiExternalEgressPolicyV1.Disabled(Policy);
        Assert.Equal(AiEgressDecisionV1.Deny, AiEgressRulesV1.Decide(Tag(dataClass), disabled));
        foreach (var type in AllTypes)
            Assert.Equal(AiEgressDecisionV1.Deny, AiEgressRulesV1.Decide(Tag(dataClass, type), disabled));
        // Even a policy that lists every type but is switched off sends nothing.
        var off = new AiExternalEgressPolicyV1(false, AllTypes, true, Policy);
        Assert.Equal(AiEgressDecisionV1.Deny, AiEgressRulesV1.Decide(Tag(AiDataClassV1.Public), off));
    }

    [Theory]
    [InlineData(AiDataClassV1.Public)]
    [InlineData(AiDataClassV1.Internal)]
    public void Non_sensitive_general_and_company_internal_fields_go_as_they_are(AiDataClassV1 dataClass) =>
        Assert.Equal(AiEgressDecisionV1.SendAsIs, AiEgressRulesV1.Decide(Tag(dataClass), Open()));

    [Theory]
    [InlineData(AiDataClassV1.Confidential)]
    [InlineData(AiDataClassV1.StrictlyConfidential)]
    public void A_confidential_field_without_a_named_sensitive_type_is_unmapped_and_denied(AiDataClassV1 dataClass) =>
        Assert.Equal(AiEgressDecisionV1.Deny, AiEgressRulesV1.Decide(Tag(dataClass), Open()));

    [Theory]
    [InlineData(AiSensitiveDataTypeV1.Price)]
    [InlineData(AiSensitiveDataTypeV1.Balance)]
    [InlineData(AiSensitiveDataTypeV1.FinanceAmount)]
    [InlineData(AiSensitiveDataTypeV1.CompanyDocument)]
    public void A_named_sensitive_type_goes_only_pseudonymized_and_only_when_the_admin_allowed_it(AiSensitiveDataTypeV1 type)
    {
        Assert.Equal(AiEgressDecisionV1.SendPseudonymized,
            AiEgressRulesV1.Decide(Tag(AiDataClassV1.Confidential, type), Open(true, type)));
        var others = AllTypes.Where(candidate => candidate != type).ToArray();
        Assert.Equal(AiEgressDecisionV1.Deny,
            AiEgressRulesV1.Decide(Tag(AiDataClassV1.Confidential, type), Open(true, others)));
        // A sensitive type tagged on a lower class is still sensitive: never sent as it is.
        Assert.Equal(AiEgressDecisionV1.SendPseudonymized,
            AiEgressRulesV1.Decide(Tag(AiDataClassV1.Public, type), Open(true, type)));
        Assert.Equal(AiEgressDecisionV1.Deny,
            AiEgressRulesV1.Decide(Tag(AiDataClassV1.Public, type), Open(true, others)));
    }

    [Fact]
    public void Iban_needs_the_type_permission_and_a_recorded_kvkk_review_and_other_strictly_confidential_fields_never_leave()
    {
        var iban = Tag(AiDataClassV1.StrictlyConfidential, AiSensitiveDataTypeV1.Iban);
        Assert.Equal(AiEgressDecisionV1.SendPseudonymized, AiEgressRulesV1.Decide(iban, Open(true, AiSensitiveDataTypeV1.Iban)));
        Assert.Equal(AiEgressDecisionV1.Deny, AiEgressRulesV1.Decide(iban, Open(false, AiSensitiveDataTypeV1.Iban)));
        Assert.Equal(AiEgressDecisionV1.Deny, AiEgressRulesV1.Decide(iban, Open(true, AiSensitiveDataTypeV1.Price)));
        foreach (var type in AllTypes.Where(type => type != AiSensitiveDataTypeV1.Iban))
            Assert.Equal(AiEgressDecisionV1.Deny,
                AiEgressRulesV1.Decide(Tag(AiDataClassV1.StrictlyConfidential, type), Open()));
    }

    [Theory]
    [InlineData(AiDataClassV1.Public, AiSensitiveDataTypeV1.None)]
    [InlineData(AiDataClassV1.Internal, AiSensitiveDataTypeV1.None)]
    [InlineData(AiDataClassV1.Confidential, AiSensitiveDataTypeV1.Price)]
    [InlineData(AiDataClassV1.StrictlyConfidential, AiSensitiveDataTypeV1.Iban)]
    public void A_never_egress_field_is_denied_under_any_policy(AiDataClassV1 dataClass, AiSensitiveDataTypeV1 type) =>
        Assert.Equal(AiEgressDecisionV1.Deny, AiEgressRulesV1.Decide(Tag(dataClass, type, never: true), Open()));

    [Fact]
    public void The_filter_drops_denied_fields_pseudonymizes_allowed_sensitive_ones_and_never_returns_a_raw_sensitive_value()
    {
        const string price = "SENTETIK-1234.50";
        const string iban = "SENTETIK-TR000000000000000000000000";
        const string phone = "SENTETIK-0555";
        var fields = new[]
        {
            new AiOutboundFieldV1(Tag(AiDataClassV1.Public, name: "status"), "Onay bekliyor"),
            new AiOutboundFieldV1(Tag(AiDataClassV1.Confidential, AiSensitiveDataTypeV1.Price, name: "unitPrice"), price),
            new AiOutboundFieldV1(Tag(AiDataClassV1.StrictlyConfidential, AiSensitiveDataTypeV1.Iban, name: "iban"), iban),
            new AiOutboundFieldV1(Tag(AiDataClassV1.Confidential, never: true, name: "phone"), phone),
            new AiOutboundFieldV1(Tag(AiDataClassV1.Confidential, name: "margin"), "SENTETIK-12%"),
        };
        using var pseudonymizer = new AiPseudonymizerV1();
        var payload = AiEgressFilterV1.Apply(fields, Open(false, AiSensitiveDataTypeV1.Price), pseudonymizer);

        Assert.Equal(["status", "unitPrice"], payload.Fields.Keys.ToArray());
        Assert.Equal("Onay bekliyor", payload.Fields["status"]);
        Assert.Equal("[[ai:price:1]]", payload.Fields["unitPrice"]);
        Assert.Equal(["iban", "margin", "phone"], payload.DeniedFieldNames.ToArray());
        Assert.Equal(Policy, payload.PolicyReference);
        foreach (var value in payload.Fields.Values)
        {
            Assert.DoesNotContain(price, value, StringComparison.Ordinal);
            Assert.DoesNotContain(iban, value, StringComparison.Ordinal);
            Assert.DoesNotContain(phone, value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_filter_rejects_duplicate_or_null_fields()
    {
        using var pseudonymizer = new AiPseudonymizerV1();
        var duplicate = new[]
        {
            new AiOutboundFieldV1(Tag(AiDataClassV1.Public, name: "a"), "1"),
            new AiOutboundFieldV1(Tag(AiDataClassV1.Public, name: "a"), "2"),
        };
        Assert.Throws<ArgumentException>(() => AiEgressFilterV1.Apply(duplicate, Open(), pseudonymizer));
        Assert.Throws<ArgumentException>(() => AiEgressFilterV1.Apply([null!], Open(), pseudonymizer));
        Assert.Throws<ArgumentException>(() =>
            AiEgressFilterV1.Apply([new AiOutboundFieldV1(Tag(AiDataClassV1.Public), null!)], Open(), pseudonymizer));
    }

    [Fact]
    public void Pseudonyms_are_stable_per_request_restore_only_issued_tokens_and_vanish_on_dispose()
    {
        var pseudonymizer = new AiPseudonymizerV1();
        var first = pseudonymizer.Pseudonymize(AiSensitiveDataTypeV1.Balance, "SENTETIK-100");
        Assert.Equal(first, pseudonymizer.Pseudonymize(AiSensitiveDataTypeV1.Balance, "SENTETIK-100"));
        var second = pseudonymizer.Pseudonymize(AiSensitiveDataTypeV1.Balance, "SENTETIK-200");
        Assert.NotEqual(first, second);
        // The same raw value under another type is another token.
        Assert.NotEqual(first, pseudonymizer.Pseudonymize(AiSensitiveDataTypeV1.Price, "SENTETIK-100"));
        Assert.Equal(3, pseudonymizer.IssuedCount);

        var restored = pseudonymizer.Restore($"Bakiye {first}, diğer {second}, uydurma [[ai:iban:7]].");
        Assert.Equal("Bakiye SENTETIK-100, diğer SENTETIK-200, uydurma [[ai:iban:7]].", restored.Text);
        Assert.Equal(1, restored.UnknownTokenCount);

        Assert.DoesNotContain("SENTETIK", pseudonymizer.ToString(), StringComparison.Ordinal);
        pseudonymizer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pseudonymizer.Restore(first));
        Assert.Throws<ObjectDisposedException>(() => pseudonymizer.Pseudonymize(AiSensitiveDataTypeV1.Price, "x"));
    }

    [Fact]
    public void Another_request_cannot_restore_tokens_it_did_not_issue()
    {
        using var issuing = new AiPseudonymizerV1();
        using var other = new AiPseudonymizerV1();
        var token = issuing.Pseudonymize(AiSensitiveDataTypeV1.Iban, "SENTETIK-TR00");
        var restored = other.Restore(token);
        Assert.Equal(token, restored.Text);
        Assert.Equal(1, restored.UnknownTokenCount);
    }

    [Fact]
    public void Invalid_tags_types_and_policies_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new AiFieldTagV1("", AiDataClassV1.Public, AiSensitiveDataTypeV1.None));
        Assert.Throws<ArgumentException>(() => new AiFieldTagV1("bad name", AiDataClassV1.Public, AiSensitiveDataTypeV1.None));
        Assert.Throws<ArgumentException>(() => new AiFieldTagV1("ok", (AiDataClassV1)99, AiSensitiveDataTypeV1.None));
        Assert.Throws<ArgumentException>(() => new AiFieldTagV1("ok", AiDataClassV1.Public, (AiSensitiveDataTypeV1)99));
        Assert.Throws<ArgumentException>(() => new AiExternalEgressPolicyV1(true, [AiSensitiveDataTypeV1.None], false, Policy));
        Assert.Throws<ArgumentException>(() => new AiExternalEgressPolicyV1(true, [(AiSensitiveDataTypeV1)99], false, Policy));
        Assert.Throws<ArgumentException>(() => new AiExternalEgressPolicyV1(true, [], false, " "));
        using var pseudonymizer = new AiPseudonymizerV1();
        Assert.Throws<ArgumentException>(() => pseudonymizer.Pseudonymize(AiSensitiveDataTypeV1.None, "x"));
    }

    [Fact]
    public void Record_text_never_renders_a_raw_outbound_or_restored_value()
    {
        const string raw = "SENTETIK-TR00-0000-1111";
        var field = new AiOutboundFieldV1(Tag(AiDataClassV1.StrictlyConfidential, AiSensitiveDataTypeV1.Iban, name: "iban"), raw);
        Assert.DoesNotContain(raw, field.ToString(), StringComparison.Ordinal);
        Assert.Contains("iban", field.ToString(), StringComparison.Ordinal);

        using var pseudonymizer = new AiPseudonymizerV1();
        var token = pseudonymizer.Pseudonymize(AiSensitiveDataTypeV1.Iban, raw);
        var restored = pseudonymizer.Restore($"Hesap {token}");
        Assert.Contains(raw, restored.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(raw, restored.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(raw, $"{restored}", StringComparison.Ordinal);
    }

    [Fact]
    public void The_allowed_type_set_cannot_be_widened_after_construction()
    {
        var source = new List<AiSensitiveDataTypeV1> { AiSensitiveDataTypeV1.Price };
        var policy = new AiExternalEgressPolicyV1(true, source, false, Policy);
        source.Add(AiSensitiveDataTypeV1.Iban);
        Assert.False(policy.AllowedSensitiveTypes.Contains(AiSensitiveDataTypeV1.Iban));
        Assert.False(policy.AllowedSensitiveTypes is ISet<AiSensitiveDataTypeV1> { IsReadOnly: false });
        if (policy.AllowedSensitiveTypes is ICollection<AiSensitiveDataTypeV1> collection)
            Assert.Throws<NotSupportedException>(() => collection.Add(AiSensitiveDataTypeV1.Balance));
        Assert.Equal(AiEgressDecisionV1.Deny,
            AiEgressRulesV1.Decide(Tag(AiDataClassV1.Confidential, AiSensitiveDataTypeV1.Balance), policy));
    }

    [Fact]
    public void The_disabled_policy_is_fail_closed_on_its_own()
    {
        var disabled = AiExternalEgressPolicyV1.Disabled(Policy);
        Assert.False(disabled.ExternalEnabled);
        Assert.Empty(disabled.AllowedSensitiveTypes);
        Assert.False(disabled.IbanKvkkReviewRecorded);
        Assert.Equal(Policy, disabled.PolicyReference);
        using var pseudonymizer = new AiPseudonymizerV1();
        var payload = AiEgressFilterV1.Apply(
            [new AiOutboundFieldV1(Tag(AiDataClassV1.Public, name: "status"), "Onaylandi")], disabled, pseudonymizer);
        Assert.Empty(payload.Fields);
        Assert.Equal(["status"], payload.DeniedFieldNames);
        Assert.Equal(0, pseudonymizer.IssuedCount);
    }
}
