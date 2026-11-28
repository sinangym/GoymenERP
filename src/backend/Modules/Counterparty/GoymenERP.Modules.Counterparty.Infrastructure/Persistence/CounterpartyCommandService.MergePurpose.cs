using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed partial class CounterpartyCommandService : ICounterpartyPartyMergePurposeV1
{
    public async Task<CounterpartyPartyMergePurposePreviewV1> PreviewPurposeAsync(Guid survivorPartyId,
        PreviewCounterpartyPartyMergeV1 command, Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumerFence);
        var captured = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        await Fence();
        var preview = await PreviewMergeCoreAsync(survivorPartyId, command, consumerFence, cancellationToken);
        await Fence();
        return new(preview, preview.Status == CounterpartyPartyMergePreviewStatusV1.Found && preview.Preview is { } impact ? MergeImpactDigest(impact) : null);
        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            await consumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await captured.RequireStillAsync(currentUser, companyContext, authorityRevalidationService, PartyMergePermissions, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static CounterpartyPartyMergeHoldingAccountV1 MergeAccountPreview(CompanyPartyAccount account) =>
        new(account.CompanyId, account.Id, account.PartyId, account.Version.ToString(CultureInfo.InvariantCulture), account.IsActive);
    private static CounterpartyPartyMergeImpactPreviewV1 MergeImpactPreview(Guid companyId, Party survivor, Party merged,
        IReadOnlyList<CounterpartyPartyMergeHoldingAccountV1> accounts) =>
        // SQL uniqueidentifier ordering differs from Guid.CompareTo. Freeze one detached canonical order
        // for native/purpose preview, the captured audit impact and its strict original-receipt validation.
        new(companyId, PartyPreview(survivor), PartyPreview(merged),
            Array.AsReadOnly(accounts.OrderBy(x => x.CompanyId).ThenBy(x => x.PartyId).ThenBy(x => x.AccountId).ToArray()),
            new(merged.Id, survivor.Id, false));
    private static string MergeImpactDigest(CounterpartyPartyMergeImpactPreviewV1 impact) => IdempotencyHash.ComputePayload(impact);

    private string SerializePurposeMergeSnapshot(Party merged, Guid survivor, PartyMerge merge, IReadOnlyCollection<Guid> companies)
    {
        var native = SerializePartyMergeSnapshot(merged, survivor, merge, companies);
        if (mergePurpose is not { CapturedImpact: { } impact } state) return native;
        var audit = System.Text.Json.Nodes.JsonNode.Parse(native)!.AsObject();
        audit["SeenImpactDigest"] = state.SeenImpactDigest;
        audit["SeenImpact"] = JsonSerializer.SerializeToNode(impact);
        return audit.ToJsonString();
    }

    private static (long Survivor, long Merged, string Reason) MergeShape(Guid survivor, ApproveCounterpartyPartyMerge command)
    {
        RequirePartyId(survivor);
        if (command.MergedPartyId == Guid.Empty) throw new ArgumentException("mergedPartyId: Birleştirilecek cari kimliği boş olamaz.", nameof(command));
        if (command.MergedPartyId == survivor) throw new ArgumentException("mergedPartyId: Cari kendisiyle birleştirilemez.", nameof(command));
        var survivorVersion = CounterpartyContactInformationEditor.ParseVersion(command.ExpectedVersion);
        var mergedVersion = ParseMergedPartyVersion(command.MergedPartyExpectedVersion);
        ValidateReason(command.Reason);
        return (survivorVersion, mergedVersion, command.Reason.Trim());
    }

    private static string MergePayloadHash(Guid survivor, ApproveCounterpartyPartyMerge command,
        (long Survivor, long Merged, string Reason) shape) => IdempotencyHash.ComputePayload(new
        { SurvivorPartyId = survivor, MergedPartyId = command.MergedPartyId, ExpectedVersion = shape.Survivor,
          MergedPartyExpectedVersion = shape.Merged, Reason = shape.Reason });

    private async Task MergePurposeFenceAsync(CancellationToken token)
    {
        if (mergePurpose is { } state) await state.Fence(token);
    }
}
