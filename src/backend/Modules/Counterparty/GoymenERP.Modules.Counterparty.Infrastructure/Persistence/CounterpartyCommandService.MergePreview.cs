using System.Globalization;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed partial class CounterpartyCommandService
{
    public Task<CounterpartyPartyMergePreviewResultV1> PreviewMergeAsync(Guid survivorPartyId,
        PreviewCounterpartyPartyMergeV1 command, CancellationToken token) => PreviewMergeCoreAsync(survivorPartyId, command, null, token);

    private async Task<CounterpartyPartyMergePreviewResultV1> PreviewMergeCoreAsync(Guid survivorPartyId,
        PreviewCounterpartyPartyMergeV1 command, Func<CancellationToken, Task>? consumer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(command);
        RequirePartyId(survivorPartyId);
        if (command.MergedPartyId == Guid.Empty || command.MergedPartyId == survivorPartyId)
            throw new ArgumentException("Birleştirme önizlemesi iki farklı cari gerektirir.", nameof(command));
        var survivorVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var mergedVersion = ParseMergedPartyVersion(command.MergedPartyExpectedVersion);
        var captured = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await Fence();
        var accounts = await ReadAccounts();
        var companies = accounts.Select(x => x.CompanyId).Distinct().Order().ToArray();
        if (!accounts.Any(x => x.PartyId == survivorPartyId && x.CompanyId == captured.CompanyId)
            || !accounts.Any(x => x.PartyId == command.MergedPartyId)) return Answer(CounterpartyPartyMergePreviewStatusV1.NotFound);
        await AllCompanyAuthority();
        var parties = await ReadParties();
        var survivor = parties.SingleOrDefault(x => x.Id == survivorPartyId);
        var merged = parties.SingleOrDefault(x => x.Id == command.MergedPartyId);
        if (survivor is null || merged is null) return Answer(CounterpartyPartyMergePreviewStatusV1.NotFound);
        if (survivor.Version != survivorVersion || merged.Version != mergedVersion || survivor.MergedIntoPartyId is not null
            || merged.MergedIntoPartyId is not null || survivor.TaxIdentityNumber is not null && merged.TaxIdentityNumber is not null
                && survivor.TaxIdentityNumber != merged.TaxIdentityNumber) return Answer(CounterpartyPartyMergePreviewStatusV1.Changed);
        if (accounts.Any(x => x.CompanyId == Guid.Empty || x.AccountId == Guid.Empty || !long.TryParse(x.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            || survivor.Version < 1 || merged.Version < 1) return Answer(CounterpartyPartyMergePreviewStatusV1.Unavailable);
        // Recheck the actual published account fields/membership and party snapshots during this READ. This is not
        // an atomic precondition on the later native PUT: its unchanged payload fences only the two party versions.
        if (!accounts.SequenceEqual(await ReadAccounts())) return Answer(CounterpartyPartyMergePreviewStatusV1.Changed);
        var later = await ReadParties();
        if (later.Count != parties.Count || !parties.Select(PartyPreview).SequenceEqual(later.Select(PartyPreview)))
            return Answer(CounterpartyPartyMergePreviewStatusV1.Changed);
        await AllCompanyAuthority(); await Fence();
        if (companies.Any(company => !currentUser.CanAccessCompany(company))) throw new UnauthorizedAccessException("Birleştirme etki önizlemesi yetkisi yok.");
        return new(CounterpartyPartyMergePreviewStatusV1.Found, MergeImpactPreview(captured.CompanyId, survivor, merged, Array.AsReadOnly(accounts.ToArray())));

        CounterpartyPartyMergePreviewResultV1 Answer(CounterpartyPartyMergePreviewStatusV1 status) => new(status, null);
        async Task Fence()
        {
            token.ThrowIfCancellationRequested();
            if (consumer is not null) await consumer(token);
            token.ThrowIfCancellationRequested();
            await captured.RequireStillAsync(currentUser, companyContext, authorityRevalidationService, PartyMergePermissions, token);
            token.ThrowIfCancellationRequested();
        }
        async Task AllCompanyAuthority()
        {
            foreach (var company in companies)
            {
                await Fence();
                if (!currentUser.CanAccessCompany(company)) throw new UnauthorizedAccessException("Birleştirme etki önizlemesi yetkisi yok.");
                foreach (var permission in PartyMergePermissions)
                {
                    await Fence(); await RequireFreshCompanyWidePermissionAsync(permission, company, token); await Fence();
                }
            }
        }
        async Task<List<CounterpartyPartyMergeHoldingAccountV1>> ReadAccounts()
        {
            await Fence();
            var rows = await dbContext.CompanyPartyAccounts.AsNoTracking()
                .Where(x => x.PartyId == survivorPartyId || x.PartyId == command.MergedPartyId)
                .OrderBy(x => x.CompanyId).ThenBy(x => x.PartyId).ThenBy(x => x.Id).ToListAsync(token);
            await Fence();
            return rows.Select(MergeAccountPreview).ToList();
        }
        async Task<List<Party>> ReadParties()
        {
            await Fence();
            var rows = await dbContext.Parties.AsNoTracking().Where(x => x.Id == survivorPartyId || x.Id == command.MergedPartyId)
                .OrderBy(x => x.Id).ToListAsync(token);
            await Fence(); return rows;
        }
    }
    private static CounterpartyPartyMergeImpactPartyV1 PartyPreview(Party p) =>
        new(p.Id, p.Code.Value, p.LegalName, p.Version.ToString(CultureInfo.InvariantCulture), p.IsActive, p.MergedIntoPartyId);
}
