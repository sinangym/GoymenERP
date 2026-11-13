using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// The sensitive data types Sinan named for the external AI (SINAN_KARARLARI_2026-11-03_AI, AI-D4): price, balance,
/// IBAN, finance amount and company document. A field tag, not a permission. <see cref="None"/> means the field is not
/// one of these types.
/// </summary>
public enum AiSensitiveDataTypeV1 { None = 0, Price = 1, Balance = 2, Iban = 3, FinanceAmount = 4, CompanyDocument = 5 }

public enum AiEgressDecisionV1 { SendAsIs = 1, SendPseudonymized = 2, Deny = 3 }

/// <summary>
/// How one outbound field is classified. <paramref name="NeverEgress"/> marks values that never leave under any policy
/// (AI-D4: national id, payroll, secrets, personal contact data until the KVKK basis is settled, and "Gizli" items Sinan
/// has not mapped yet). The tag is supplied by the owning tool's reviewed field table, never by the model.
/// </summary>
public sealed record AiFieldTagV1
{
    public string FieldName { get; }
    public AiDataClassV1 DataClass { get; }
    public AiSensitiveDataTypeV1 SensitiveType { get; }
    public bool NeverEgress { get; }

    public AiFieldTagV1(string fieldName, AiDataClassV1 dataClass, AiSensitiveDataTypeV1 sensitiveType, bool neverEgress = false)
    {
        if (!AiEgressGuard.IsFieldName(fieldName) || !Enum.IsDefined(dataClass) || !Enum.IsDefined(sensitiveType))
            throw new ArgumentException("Invalid field tag.");
        FieldName = fieldName; DataClass = dataClass; SensitiveType = sensitiveType; NeverEgress = neverEgress;
    }
}

/// <summary>
/// The external-AI data policy in force for one request. Values come from the Settings policy (AI-D6); this type holds
/// no defaults of its own. <see cref="Disabled"/> is what a caller uses when no policy is configured (fail-closed).
/// </summary>
public sealed record AiExternalEgressPolicyV1
{
    public bool ExternalEnabled { get; }
    public IReadOnlySet<AiSensitiveDataTypeV1> AllowedSensitiveTypes { get; }
    /// <summary>ERP-000J §6: the IBAN pseudonym path also needs a recorded security/KVKK review (OPEN-020).</summary>
    public bool IbanKvkkReviewRecorded { get; }
    public string PolicyReference { get; }

    public AiExternalEgressPolicyV1(bool externalEnabled, IEnumerable<AiSensitiveDataTypeV1> allowedSensitiveTypes,
        bool ibanKvkkReviewRecorded, string policyReference)
    {
        ArgumentNullException.ThrowIfNull(allowedSensitiveTypes);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyReference);
        var allowed = allowedSensitiveTypes.ToHashSet();
        if (allowed.Any(type => type == AiSensitiveDataTypeV1.None || !Enum.IsDefined(type)))
            throw new ArgumentException("Invalid sensitive data type.");
        ExternalEnabled = externalEnabled;
        // A read-only view over a private copy: a caller cannot cast it back and widen the policy after review.
        AllowedSensitiveTypes = new System.Collections.ObjectModel.ReadOnlySet<AiSensitiveDataTypeV1>(allowed);
        IbanKvkkReviewRecorded = ibanKvkkReviewRecorded;
        PolicyReference = policyReference;
    }

    public static AiExternalEgressPolicyV1 Disabled(string policyReference) => new(false, [], false, policyReference);
}

/// <summary>Pure decision rules for one field (AI-D3/AI-D4). No I/O, no defaults, deny when in doubt.</summary>
public static class AiEgressRulesV1
{
    public static AiEgressDecisionV1 Decide(AiFieldTagV1 tag, AiExternalEgressPolicyV1 policy)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.ExternalEnabled || tag.NeverEgress) return AiEgressDecisionV1.Deny;
        if (tag.SensitiveType == AiSensitiveDataTypeV1.None)
        {
            // AI-D3: only "Genel" and "Şirket içi" go as they are; any other class without a named type is unmapped.
            return tag.DataClass is AiDataClassV1.Public or AiDataClassV1.Internal
                ? AiEgressDecisionV1.SendAsIs
                : AiEgressDecisionV1.Deny;
        }
        // "Çok gizli" leaves only for IBAN (AI-D4); every other strictly confidential field stays.
        if (tag.DataClass == AiDataClassV1.StrictlyConfidential && tag.SensitiveType != AiSensitiveDataTypeV1.Iban)
            return AiEgressDecisionV1.Deny;
        if (!policy.AllowedSensitiveTypes.Contains(tag.SensitiveType)) return AiEgressDecisionV1.Deny;
        if (tag.SensitiveType == AiSensitiveDataTypeV1.Iban && !policy.IbanKvkkReviewRecorded) return AiEgressDecisionV1.Deny;
        return AiEgressDecisionV1.SendPseudonymized;
    }
}

/// <summary>
/// Per-request pseudonym map (AI-D4). Raw values stay inside this object; tokens are the only thing that leaves.
/// The map is never serialized, logged or written to audit, and is cleared on dispose.
/// </summary>
public sealed partial class AiPseudonymizerV1 : IDisposable
{
    private readonly Dictionary<(AiSensitiveDataTypeV1 Type, string Value), string> tokensByValue = new();
    private readonly Dictionary<string, string> valuesByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<AiSensitiveDataTypeV1, int> counters = new();
    private bool disposed;

    [GeneratedRegex(@"\[\[ai:[a-z-]+:[0-9]{1,6}\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    public string Pseudonymize(AiSensitiveDataTypeV1 type, string rawValue)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (type == AiSensitiveDataTypeV1.None || !Enum.IsDefined(type)) throw new ArgumentException("Invalid sensitive data type.");
        ArgumentNullException.ThrowIfNull(rawValue);
        if (tokensByValue.TryGetValue((type, rawValue), out var existing)) return existing;
        var next = counters.GetValueOrDefault(type) + 1;
        counters[type] = next;
        var token = $"[[ai:{TypeName(type)}:{next.ToString(System.Globalization.CultureInfo.InvariantCulture)}]]";
        tokensByValue[(type, rawValue)] = token;
        valuesByToken[token] = rawValue;
        return token;
    }

    /// <summary>
    /// Puts raw values back into model output on the server. A token this request did not issue is left as it is and
    /// counted, so a model cannot make the server reveal a value it was never given.
    /// </summary>
    public AiRestoredTextV1 Restore(string modelText)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(modelText);
        var unknown = 0;
        var restored = TokenPattern().Replace(modelText, match =>
        {
            if (valuesByToken.TryGetValue(match.Value, out var raw)) return raw;
            unknown++;
            return match.Value;
        });
        return new AiRestoredTextV1(restored, unknown);
    }

    public int IssuedCount { get { ObjectDisposedException.ThrowIf(disposed, this); return valuesByToken.Count; } }

    public void Dispose()
    {
        tokensByValue.Clear();
        valuesByToken.Clear();
        counters.Clear();
        disposed = true;
    }

    /// <summary>The map is never rendered: a log line or exception message carrying this object shows nothing.</summary>
    public override string ToString() => nameof(AiPseudonymizerV1);

    private static string TypeName(AiSensitiveDataTypeV1 type) => type switch
    {
        AiSensitiveDataTypeV1.Price => "price",
        AiSensitiveDataTypeV1.Balance => "balance",
        AiSensitiveDataTypeV1.Iban => "iban",
        AiSensitiveDataTypeV1.FinanceAmount => "finance-amount",
        AiSensitiveDataTypeV1.CompanyDocument => "company-document",
        _ => throw new ArgumentException("Invalid sensitive data type."),
    };
}

/// <summary>Restored model text holds raw values again, so the generated record text never renders it.</summary>
public sealed record AiRestoredTextV1(string Text, int UnknownTokenCount)
{
    public override string ToString() =>
        $"{nameof(AiRestoredTextV1)} {{ Text = <redacted>, {nameof(UnknownTokenCount)} = {UnknownTokenCount} }}";
}

/// <summary>One outbound field before filtering. The value may be sensitive, so the record text shows only the field name.</summary>
public sealed record AiOutboundFieldV1(AiFieldTagV1 Tag, string Value)
{
    public override string ToString() =>
        $"{nameof(AiOutboundFieldV1)} {{ Field = {Tag?.FieldName}, Value = <redacted> }}";
}

/// <summary>
/// The fields that may leave for the external AI. Denied fields are absent; only their names and count are kept for
/// the security-event record (names are schema, not data).
/// </summary>
public sealed record AiEgressPayloadV1
{
    public IReadOnlyDictionary<string, string> Fields { get; }
    public IReadOnlyList<string> DeniedFieldNames { get; }
    public string PolicyReference { get; }

    internal AiEgressPayloadV1(IReadOnlyDictionary<string, string> fields, IReadOnlyList<string> denied, string policyReference)
    {
        Fields = fields; DeniedFieldNames = denied; PolicyReference = policyReference;
    }
}

/// <summary>Applies <see cref="AiEgressRulesV1"/> to a set of outbound fields. Raw sensitive values never appear in the result.</summary>
public static class AiEgressFilterV1
{
    public static AiEgressPayloadV1 Apply(IEnumerable<AiOutboundFieldV1> fields, AiExternalEgressPolicyV1 policy,
        AiPseudonymizerV1 pseudonymizer)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(pseudonymizer);
        var copy = fields.ToArray();
        if (copy.Any(field => field is null || field.Tag is null || field.Value is null))
            throw new ArgumentException("Null outbound field.");
        if (copy.Select(field => field.Tag.FieldName).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Duplicate outbound field.");
        var sent = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var denied = new List<string>();
        foreach (var field in copy)
        {
            switch (AiEgressRulesV1.Decide(field.Tag, policy))
            {
                case AiEgressDecisionV1.SendAsIs:
                    sent[field.Tag.FieldName] = field.Value;
                    break;
                case AiEgressDecisionV1.SendPseudonymized:
                    sent[field.Tag.FieldName] = pseudonymizer.Pseudonymize(field.Tag.SensitiveType, field.Value);
                    break;
                default:
                    denied.Add(field.Tag.FieldName);
                    break;
            }
        }
        denied.Sort(StringComparer.Ordinal);
        return new AiEgressPayloadV1(
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(sent),
            denied.AsReadOnly(),
            policy.PolicyReference);
    }
}

internal static partial class AiEgressGuard
{
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieldNamePattern();

    internal static bool IsFieldName(string? value) => value is not null && FieldNamePattern().IsMatch(value);
}
