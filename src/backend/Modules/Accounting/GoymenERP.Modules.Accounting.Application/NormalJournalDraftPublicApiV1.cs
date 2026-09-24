using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Application;

public sealed record CreateNormalJournalDraftLineRequestV1(Guid AccountId, string Side, string Amount);

public sealed record CreateNormalJournalDraftRequestV1(
    Guid DraftId,
    Guid SourceId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    IReadOnlyList<CreateNormalJournalDraftLineRequestV1> Lines);

public sealed record ApproveNormalJournalDraftRequestV1(long ExpectedVersion, Guid PeriodId);

/// <summary>
/// Exact server-owned preparation inputs. A public request cannot provide the company, actor,
/// currency, account-plan mirror, posting purpose, rule version, mapping keys or approval provenance.
/// </summary>
public sealed record NormalJournalDraftServerSourceV1(NormalJournalPostingRuleV1 Rule, Guid CurrencyId);

public interface INormalJournalDraftServerSourceProviderV1
{
    Task<NormalJournalDraftServerSourceV1?> ResolveAsync(
        Guid companyId, Guid sourceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe default until the real versioned Mikro mirror and approved posting mapping are supplied.
/// It creates no synthetic rule and causes the public create operation to fail closed with 503.
/// </summary>
public sealed class UnavailableNormalJournalDraftServerSourceProviderV1
    : INormalJournalDraftServerSourceProviderV1
{
    public Task<NormalJournalDraftServerSourceV1?> ResolveAsync(
        Guid companyId, Guid sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<NormalJournalDraftServerSourceV1?>(null);
}

/// <summary>
/// Public-contract adapter for ACC-V1. It accepts only user-entered journal facts and binds every
/// authority, company, version and posting decision from server-owned dependencies.
/// </summary>
public sealed class NormalJournalDraftPublicServiceV1(
    INormalJournalDraftLifecycleV1 lifecycle,
    INormalJournalDraftServerSourceProviderV1 sources,
    NormalJournalPermissionBindingV1 permissions,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private const string Consumer = "accounting.public-normal-journal.v1";

    public async Task<AccountingApiResult<NormalJournalDraftViewV1>> CreateAsync(
        CreateNormalJournalDraftRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.DraftId == Guid.Empty || request.SourceId == Guid.Empty)
        {
            return Invalid("draftId");
        }

        if (request.TransactionDate == default || request.AccountingDate == default
            || request.Lines is null || request.Lines.Count < 2)
        {
            return Invalid("lines");
        }

        if (!TryContext(out var company, out var actor)
            || !await HasCreateAuthorityAsync(company, actor, cancellationToken))
        {
            return Denied();
        }

        var parsedLines = new List<(Guid AccountId, PostingSide Side, decimal Amount)>(request.Lines.Count);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line is null || line.AccountId == Guid.Empty || !TrySide(line.Side, out var side)
                || !TryAmount(line.Amount, out var amount))
            {
                return Invalid($"lines[{index}]");
            }

            parsedLines.Add((line.AccountId, side, amount));
        }

        var source = await sources.ResolveAsync(company, request.SourceId, cancellationToken);
        if (source is null)
        {
            return new AccountingApiResult<NormalJournalDraftViewV1>(
                AccountingApiStatus.Unavailable, null, false, null);
        }

        if (source.CurrencyId == Guid.Empty || source.Rule.Mirror.CompanyId != company)
        {
            return Unprocessable(AccountingErrorCodes.InvalidReference);
        }

        var proposed = new List<ProposedLine>(request.Lines.Count);
        foreach (var line in parsedLines)
        {
            var account = new AccountRef(company, line.AccountId);
            var mappings = source.Rule.Mappings
                .Where(candidate => candidate.Side == line.Side && candidate.Account == account)
                .ToArray();
            if (mappings.Length != 1)
            {
                return Unprocessable(AccountingErrorCodes.InvalidReference);
            }

            proposed.Add(new ProposedLine(mappings[0].MappingKey, account, line.Side, line.Amount));
        }

        try
        {
            var posting = new PostingRequest(
                Consumer,
                request.DraftId.ToString("D", CultureInfo.InvariantCulture),
                request.DraftId,
                new SourceReference(company, request.SourceId, source.Rule.PostingPurpose),
                source.Rule.Version,
                source.CurrencyId,
                new PostingDates(
                    request.TransactionDate,
                    request.AccountingDate,
                    request.ValueDate,
                    timeProvider.GetUtcNow()),
                proposed);
            var prepared = NormalJournalDraftV1.Prepare(
                JournalKind.Original, posting, version: 1, actor, source.Rule.Mirror, source.Rule);
            return prepared.Draft is { } draft
                ? await lifecycle.CreateAsync(draft, idempotencyKey, cancellationToken)
                : Unprocessable(AccountingErrorCodes.InvalidReference);
        }
        catch (AccountingDomainException exception)
        {
            return Unprocessable(exception.Code);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.ParamName ?? "request");
        }
    }

    public Task<AccountingApiResult<NormalJournalDraftViewV1>> ReadAsync(
        Guid draftId, CancellationToken cancellationToken = default) =>
        lifecycle.ReadAsync(draftId, cancellationToken);

    public Task<AccountingApiResult<NormalJournalDraftViewV1>> ApproveAsync(
        Guid draftId,
        ApproveNormalJournalDraftRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        request is null
            ? Task.FromResult(Invalid("request"))
            : lifecycle.ApproveAsync(draftId, request.ExpectedVersion, request.PeriodId,
                idempotencyKey, cancellationToken);

    private bool TryContext(out Guid company, out Guid actor)
    {
        company = companyContext.CompanyId;
        actor = Guid.Empty;
        return companyContext.IsResolved && company != Guid.Empty && currentUser.IsAuthenticated
            && Guid.TryParse(currentUser.UserId, out actor) && actor != Guid.Empty
            && currentUser.CanAccessCompany(company);
    }

    private async Task<bool> HasCreateAuthorityAsync(
        Guid company, Guid actor, CancellationToken cancellationToken)
    {
        var scope = await authority.RevalidatePermissionScopeAsync(
            permissions.CreatePermission, company, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && TryContext(out var currentCompany, out var currentActor)
            && currentCompany == company && currentActor == actor;
    }

    private static bool TrySide(string? value, out PostingSide side)
    {
        side = value switch
        {
            "Debit" => PostingSide.Debit,
            "Credit" => PostingSide.Credit,
            _ => default,
        };
        return value is "Debit" or "Credit";
    }

    private static bool TryAmount(string? value, out decimal amount)
    {
        amount = default;
        return value is { Length: > 0 } && value.All(character => char.IsAsciiDigit(character) || character == '.')
            && value[0] != '.' && value[^1] != '.' && value.Count(character => character == '.') <= 1
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
            && amount > 0m;
    }

    private static AccountingApiResult<NormalJournalDraftViewV1> Invalid(string field) =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);

    private static AccountingApiResult<NormalJournalDraftViewV1> Denied() =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);

    private static AccountingApiResult<NormalJournalDraftViewV1> Unprocessable(string code) =>
        new(AccountingApiStatus.Unprocessable, null, false, code);
}
