using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinancePartyCollateralService
{
    public async Task<ReadResult<PartyCollateralBankCandidates>> BankCandidatesAsync(FinanceContext context, CancellationToken token)
    {
        if (await RecordReadFenceAsync(context, token) is { } refusal) return new(refusal, null);
        if (collateralPolicies is null || ReadDate(timeProvider) is not { } today) return new(ReadOutcome.Unavailable, null);
        FinanceCollateralBankCatalogV1 answer;
        try { answer = await collateralPolicies.GetBankInstitutionsAsync(context.CompanyId, today, token); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await RecordReadFenceAsync(context, token) is { } late) return new(late, null);
            return new(exception is UnauthorizedAccessException ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
        if (await RecordReadFenceAsync(context, token) is { } after) return new(after, null);
        return BankCatalogValid(answer, context.CompanyId)
            ? new(ReadOutcome.Found, new(context.CompanyId, answer.Entries)) : new(ReadOutcome.Unavailable, null);
    }

    private async Task<(FinanceCollateralBankProofV1? Proof, PartyCollateralCommandResult? Refusal)> BankAsync(
        FinanceContext context, Guid? bankId, string? sourceVersion, CancellationToken token)
    {
        if (bankId is null && sourceVersion is null) return (null, null);
        if (bankId is null || bankId == Guid.Empty || !IsCanonical(sourceVersion) || sourceVersion!.Length > 256)
            return (null, Refused(context, CommandOutcome.ValidationFailed, Issue("bankInstitutionId", "collateral_bank_invalid")));
        var candidates = await BankCandidatesAsync(context, token);
        if (candidates.Outcome != ReadOutcome.Found || candidates.Resource is null)
            return (null, Refused(context, candidates.Outcome == ReadOutcome.Denied ? CommandOutcome.Denied : CommandOutcome.Unavailable,
                Issue("bankInstitutionId", "collateral_bank_unavailable")));
        var match = candidates.Resource.Items.Where(x => x.BankInstitutionId == bankId && x.SourceVersion == sourceVersion).ToArray();
        return match.Length == 1 ? (match[0], null)
            : (null, Refused(context, CommandOutcome.ValidationFailed, Issue("bankInstitutionId", "collateral_bank_source_changed")));
    }

    private static bool BankCatalogValid(FinanceCollateralBankCatalogV1? answer, Guid companyId) =>
        answer is { IsAvailable: true, Entries: not null } && answer.CompanyId == companyId
        && answer.Entries.All(x => x is not null && x.CompanyId == companyId && x.BankInstitutionId != Guid.Empty
            && IsCanonical(x.Name) && IsCanonical(x.EftCode) && IsCanonical(x.SourceVersion) && x.SourceVersion.Length <= 256)
        && answer.Entries.Select(x => x.BankInstitutionId).Distinct().Count() == answer.Entries.Count;

    private static void SetBank(PartyCollateralRow row, FinanceCollateralBankProofV1? proof)
    {
        row.BankInstitutionId = proof?.BankInstitutionId; row.BankSourceVersion = proof?.SourceVersion;
        row.BankProofJson = proof is null ? null : JsonSerializer.Serialize(proof, Json);
    }
    private static FinanceCollateralBankProofV1? BankProof(PartyCollateralRow row)
    {
        if (row.BankInstitutionId is null && row.BankSourceVersion is null && row.BankProofJson is null) return null;
        var proof = row.BankProofJson is null ? null : JsonSerializer.Deserialize<FinanceCollateralBankProofV1>(row.BankProofJson, Json);
        if (proof is null || proof.CompanyId != row.CompanyId || proof.BankInstitutionId != row.BankInstitutionId
            || proof.SourceVersion != row.BankSourceVersion || !IsCanonical(proof.Name) || !IsCanonical(proof.EftCode))
            throw new InvalidOperationException("Teminatın açık banka kaynağı okunamıyor.");
        return proof;
    }

    private async Task<PartyCollateralCommandResult?> RevalidateBanksAsync(FinanceContext context, CancellationToken token)
    {
        foreach (var row in dbContext.ChangeTracker.Entries<PartyCollateralRow>().Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity).Where(x => x.Kind != ReleaseKind && x.BankInstitutionId is not null))
        {
            var result = await BankAsync(context, row.BankInstitutionId, row.BankSourceVersion, token);
            if (result.Refusal is not null) return result.Refusal;
            if (result.Proof != BankProof(row)) return Refused(context, CommandOutcome.Unavailable,
                Issue("bankInstitutionId", "collateral_bank_source_changed"));
        }
        return null;
    }
}
