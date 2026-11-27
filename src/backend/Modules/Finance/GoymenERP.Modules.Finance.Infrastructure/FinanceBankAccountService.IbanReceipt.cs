using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceBankAccountService
{
    internal const string IbanReceiptEnvelopePrefix = "{\"schemaVersion\":";

    private static BankAccountIbanApprovalReceiptV1? IbanApprovalStamp(IbanChangeReceiptProof? proof, string actor, DateTimeOffset recordedAt, string keyHash) =>
        proof is { CommandName: ApproveIbanChangeCommand, ApprovalVersion: { } approved }
            ? new(proof.CompanyId, proof.AccountId, proof.CommandName, actor, recordedAt.ToUniversalTime(),
                VersionText(proof.ProposalVersion), VersionText(approved), proof.ChangeProposedBy, proof.ChangeProposedAtUtc, proof.FactsDigest, keyHash)
            : null;
    // Finance persistence only. Never a public command/result field or a replacement for native authorization.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record IbanChangeReceiptProof([property: JsonRequired] string CommandName,
        [property: JsonRequired] Guid CompanyId, [property: JsonRequired] Guid AccountId,
        [property: JsonRequired] string ChangeProposedBy, [property: JsonRequired] DateTimeOffset ChangeProposedAtUtc,
        [property: JsonRequired] long ProposalVersion, [property: JsonRequired] long? ApprovalVersion,
        [property: JsonRequired] string PayloadHash, [property: JsonRequired] string FactsDigest);

    private sealed record IbanChangeReceiptEnvelope([property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] BankAccountCommandResult Result, [property: JsonRequired] IbanChangeReceiptProof IbanChangeProof);

    internal static string IbanChangeFactsDigest(string? oldIban, string newIban, string reason) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            OldIban = oldIban is null ? null : FinanceIban.Normalize(oldIban),
            NewIban = FinanceIban.Normalize(newIban), Reason = reason,
        }, Json)))).ToLowerInvariant();

    internal static string SerializeIbanChangeReceipt(BankAccountCommandResult result, IbanChangeReceiptProof proof)
    {
        RequireIbanChangeReceipt(result, proof);
        return JsonSerializer.Serialize(new IbanChangeReceiptEnvelope(2, result, proof), Json);
    }

    // Bare native results predate this format. They remain byte-for-byte public/replay compatible and have no new proof.
    internal static BankAccountCommandResult RestoreBankCommandResult(FinanceCommandRow command) =>
        DecodeBankCommandReceipt(command).Result;

    internal static IbanChangeReceiptProof? ReadIbanChangeReceiptProof(FinanceCommandRow command) =>
        DecodeBankCommandReceipt(command).Proof;

    private static (BankAccountCommandResult Result, IbanChangeReceiptProof? Proof) DecodeBankCommandReceipt(FinanceCommandRow command)
    {
        using var document = JsonDocument.Parse(command.ResultJson);
        if (!document.RootElement.TryGetProperty("schemaVersion", out _))
        {
            if (document.RootElement.TryGetProperty("result", out _) || document.RootElement.TryGetProperty("ibanChangeProof", out _))
                throw new InvalidOperationException("Missing private IBAN receipt schema.");
            return (JsonSerializer.Deserialize<BankAccountCommandResult>(command.ResultJson, Json)
                ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı."), null);
        }
        IbanChangeReceiptEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IbanChangeReceiptEnvelope>(command.ResultJson,
                new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidOperationException("Missing private IBAN receipt.");
        }
        catch (JsonException error) { throw new InvalidOperationException("Malformed private IBAN receipt.", error); }
        if (envelope.SchemaVersion != 2 || envelope.Result is null || envelope.IbanChangeProof is null)
            throw new InvalidOperationException("Unknown private IBAN receipt schema or missing proof.");
        var proof = envelope.IbanChangeProof;
        RequireIbanChangeReceipt(envelope.Result, proof);
        if (proof.CommandName != command.CommandName || proof.CompanyId != command.CompanyId
            || proof.AccountId != command.BankAccountId || proof.PayloadHash != command.PayloadHash
            || envelope.Result.OriginalCorrelationId != command.CorrelationId
            || proof.CommandName == ApproveIbanChangeCommand && proof.ChangeProposedBy == command.ActorId
            || proof.CommandName == ProposeIbanChangeCommand
                && (proof.ChangeProposedBy != command.ActorId || proof.ChangeProposedAtUtc != command.RecordedAtUtc))
            throw new InvalidOperationException("Private IBAN receipt is not bound to its original native command.");
        return (envelope.Result, proof);
    }

    private static void RequireIbanChangeReceipt(BankAccountCommandResult result, IbanChangeReceiptProof proof)
    {
        if (proof is null || result.Outcome != CommandOutcome.Succeeded || result.IsReplay || result.Resource is not { } resource
            || result.Issues is null || result.Issues.Count != 0 || proof.CompanyId == Guid.Empty || proof.AccountId == Guid.Empty
            || resource.CompanyId != proof.CompanyId || resource.Id != proof.AccountId
            || string.IsNullOrWhiteSpace(proof.ChangeProposedBy) || proof.ChangeProposedAtUtc.Offset != TimeSpan.Zero
            || proof.ProposalVersion <= 1 || !IsDigest(proof.PayloadHash) || !IsDigest(proof.FactsDigest)
            || (proof.CommandName == ProposeIbanChangeCommand
                ? proof.ApprovalVersion is not null || resource.Version != VersionText(proof.ProposalVersion)
                : proof.CommandName != ApproveIbanChangeCommand || proof.ApprovalVersion is not { } approved
                    || proof.ProposalVersion == long.MaxValue || approved != proof.ProposalVersion + 1 || resource.Version != VersionText(approved)))
            throw new InvalidOperationException("Invalid private IBAN receipt facts.");
    }

    private static bool IsDigest(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
