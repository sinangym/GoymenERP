using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// One draft line's account as the draft's own frozen account plan names it. The code and name are
/// null when that exact plan version is not stored for the company or does not hold the account:
/// the reader then shows "not found" rather than guessing from another plan version.
/// </summary>
public sealed record NormalJournalDraftAccountV1(int LineNumber, Guid AccountId, string? AccountCode, string? AccountName);

/// <summary>
/// BLG-05b: the accounts of a normal journal draft, named by the account plan version the draft was
/// prepared with (<see cref="AccountPlanSourceVersion"/> + <see cref="AccountPlanSourceSha256"/>),
/// never by whichever plan is active now. Read-only; it changes no draft and grants nothing.
/// </summary>
public sealed record NormalJournalDraftAccountsViewV1(
    Guid DraftId,
    long Version,
    string AccountPlanSourceVersion,
    string AccountPlanSourceSha256,
    IReadOnlyList<NormalJournalDraftAccountV1> Accounts);

/// <summary>The company's stored account plan mirror for one exact source version and SHA-256, or null.</summary>
public interface IAccountPlanMirrorReaderV1
{
    Task<AccountPlanMirrorV1?> ReadExactAsync(
        string sourceVersion,
        string sourceSha256,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads a draft's account names under exactly the draft read's own authority. The draft is read
/// through <see cref="INormalJournalDraftLifecycleV1.ReadAsync"/> (own preparer or approver, measured
/// fresh), the frozen mirror is read, and then the draft is read once more: that second read is the
/// fence. Nothing is released unless the same authority still stands after the mirror read and the
/// draft is still the same version.
/// </summary>
public sealed class NormalJournalDraftAccountReadServiceV1(
    INormalJournalDraftLifecycleV1 lifecycle,
    IAccountPlanMirrorReaderV1 mirrors)
{
    public async Task<AccountingApiResult<NormalJournalDraftAccountsViewV1>> ReadAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        var first = await lifecycle.ReadAsync(draftId, cancellationToken);
        if (first.Status != AccountingApiStatus.Ok || first.Resource is not { } draft)
        {
            return Refused(first);
        }

        var mirror = await mirrors.ReadExactAsync(draft.AccountPlanSourceVersion, draft.AccountPlanSourceSha256,
            cancellationToken);

        var fence = await lifecycle.ReadAsync(draftId, cancellationToken);
        if (fence.Status != AccountingApiStatus.Ok || fence.Resource is not { } current)
        {
            return Refused(fence);
        }

        if (current.Version != draft.Version
            || current.CompanyId != draft.CompanyId
            || !string.Equals(current.AccountPlanSourceVersion, draft.AccountPlanSourceVersion, StringComparison.Ordinal)
            || !string.Equals(current.AccountPlanSourceSha256, draft.AccountPlanSourceSha256, StringComparison.Ordinal))
        {
            // The draft moved between the two reads: answer for neither, the caller reads again.
            return new AccountingApiResult<NormalJournalDraftAccountsViewV1>(
                AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict,
                ExpectedVersion: draft.Version, CurrentVersion: current.Version);
        }

        var exact = mirror is not null
            && mirror.CompanyId == current.CompanyId
            && string.Equals(mirror.SourceVersion, current.AccountPlanSourceVersion, StringComparison.Ordinal)
            && string.Equals(mirror.SourceSha256, current.AccountPlanSourceSha256, StringComparison.OrdinalIgnoreCase);
        var accounts = current.Lines
            .Select(line =>
            {
                var row = exact ? mirror!.Lookup(new AccountRef(current.CompanyId, line.AccountId)) : null;
                return new NormalJournalDraftAccountV1(line.LineNumber, line.AccountId, row?.Code, row?.Name);
            })
            .ToArray();
        return new AccountingApiResult<NormalJournalDraftAccountsViewV1>(
            AccountingApiStatus.Ok,
            new NormalJournalDraftAccountsViewV1(current.DraftId, current.Version, current.AccountPlanSourceVersion,
                current.AccountPlanSourceSha256, accounts),
            false,
            null);
    }

    private static AccountingApiResult<NormalJournalDraftAccountsViewV1> Refused(
        AccountingApiResult<NormalJournalDraftViewV1> result) =>
        new(result.Status == AccountingApiStatus.Ok ? AccountingApiStatus.NotFound : result.Status,
            null, false, result.Code, result.Field);
}
