using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

/// <summary>
/// WP6 (AI-D31, DALGA_PLANI_v2 1.4): the fence every Inventory Work inbox source applies before and after its read. The
/// server-resolved company and the authenticated user must be the context's, the user must still reach the company, and the
/// source's approver permission must be granted company-wide as it stands now — the same grant the owner's decision command
/// measures (<c>InventoryTransferReceiptCommandService</c> refuses a site-scoped grant). Anything else is a refusal without
/// items; an authority read that fails is unavailable, never a grant.
/// </summary>
internal sealed class InventoryWorkInboxFence(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    /// <summary>The transfer excess approval screen (<c>InventoryShellCapabilityProvider.TransferOverageApprovalRoutePath</c>).</summary>
    public const string TransferOverageApprovalPath = Shell.InventoryShellCapabilityProvider.TransferOverageApprovalRoutePath;

    /// <summary>The transfer loss approval screen (<c>InventoryShellCapabilityProvider.TransferLossApprovalRoutePath</c>).</summary>
    public const string TransferLossApprovalPath = Shell.InventoryShellCapabilityProvider.TransferLossApprovalRoutePath;

    /// <summary>The label detail screen (ST-01 → label), where a label's movements are read.</summary>
    public const string LabelDetailPath = "/stok/etiket";

    /// <summary>Null when the caller may read; otherwise the outcome to return.</summary>
    public async Task<WorkInboxSourceResultV1?> CheckAsync(
        WorkInboxSourceContextV1 context,
        string permission,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return WorkInboxSourceResultV1.Denied();
        }

        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
            return scope.IsGranted && scope.IsCompanyWide && SameContext(context) ? null : WorkInboxSourceResultV1.Denied();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }
    }

    /// <summary>A failed read answers the fence's refusal when the caller lost access meanwhile, else unavailable.</summary>
    public async Task<WorkInboxSourceResultV1> UnavailableAsync(
        WorkInboxSourceContextV1 context,
        string permission,
        CancellationToken cancellationToken) =>
        await CheckAsync(context, permission, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();

    public bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    /// <summary>The identity as the Inventory screens carry it (lowercase, hyphenated).</summary>
    public static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    public static string LabelLink(Guid labelId) => LabelDetailPath + "/" + Id(labelId);

    /// <summary>
    /// The inbox item identity of one transfer line's pending decision of <paramref name="kind"/>. A transfer line is keyed by
    /// (transfer, line), so the item id is a name-based UUID (SHA-256, RFC 9562 version 8 layout) of the kind and both ids:
    /// stable across reads, distinct per kind and per line, never a stored id of another record.
    /// </summary>
    public static Guid TransferLineItemId(string kind, Guid transferId, Guid lineId)
    {
        var name = Encoding.UTF8.GetBytes(kind + ":" + Id(transferId) + ":" + Id(lineId));
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(name, hash);
        var bytes = hash[..16].ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
