using System.Text.Json;
using GoymenERP.Api.Contracts;

namespace GoymenERP.BrowserSuiteFixtureTool.Shared;

/// <summary>
/// The shared Cari seed (CARİ): a synthetic party and its company account in the context company,
/// opened through the real counterparty endpoint as a declared actor, so authorization, the negotiated
/// client contract, idempotency, audit and the party/account separation apply exactly as in the
/// product. It opens no account in another company (no unapproved automatic account B). Suites that
/// need a customer (c03, s03) call it from their own fixture; the roles are the caller's choice
/// (0 is valid).
/// </summary>
public static class CariBrowserSeed
{
    public const string CounterpartiesPath = "/api/v1/counterparties";
    // The Cari client contract is the owner's, so these ALIAS it instead of copying it: the fixture
    // tool already references GoymenERP.Api. A copy drifts silently and the drift does not show up
    // as a red unit test — it kills a whole browser run at seed time with a 409 "Uyumlu istemci
    // gerekli", which is what s04 paid for once.
    // `static readonly`, not `const`: a C# const is baked into the CONSUMING assembly's IL at compile
    // time, so a fixture tool built against an older Api could still carry the previous value even
    // after the contract moved — one layer below the copy this alias removes. These resolve at run
    // time. Neither usage (a dictionary value and a Headers.Add) needs a compile-time constant.
    public static readonly string ClientContractHeader = CounterpartyClientContract.HeaderName;
    public static readonly string ClientContractVersion = CounterpartyClientContract.Version;
    public const int LegalEntity = 1;

    public static async Task<CariBrowserAccount> OpenPartyWithAccountAsync(
        BrowserSuiteFixtureContext context,
        string actorUserId,
        Guid companyId,
        string step,
        string legalName,
        int roles,
        CancellationToken cancellationToken)
    {
        if (roles is < 0 or > 15)
        {
            throw new InvalidOperationException("Party roles are the 1|2|4|8 flags (0..15).");
        }

        var created = await context.PostJsonAsync(
            actorUserId,
            companyId,
            CounterpartiesPath,
            new
            {
                kind = LegalEntity,
                legalName = context.Label(legalName),
                roles,
            },
            step,
            cancellationToken,
            new Dictionary<string, string> { [ClientContractHeader] = ClientContractVersion });

        var partyId = created.GetProperty("id").GetGuid();
        var accounts = created.GetProperty("companyAccounts").EnumerateArray()
            .Where(account => account.GetProperty("companyId").GetGuid() == companyId)
            .ToArray();
        if (accounts.Length != 1 || created.GetProperty("companyAccounts").GetArrayLength() != 1)
        {
            throw new InvalidOperationException(
                "The Cari seed expects exactly one company account, in the context company.");
        }

        return new CariBrowserAccount(
            partyId,
            accounts[0].GetProperty("id").GetGuid(),
            companyId,
            accounts[0].GetProperty("version").GetString()
                ?? throw new InvalidOperationException("The account version is missing."));
    }
}

/// <summary>The opened party and its one account; the version is the account's, as the API answers it.</summary>
public sealed record CariBrowserAccount(Guid PartyId, Guid AccountId, Guid CompanyId, string AccountVersion);
