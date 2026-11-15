namespace GoymenERP.Modules.Identity.Domain.Approvals;

public enum AccessGrantRequestStatus
{
    Pending,
    Approved,
    Rejected,

    /// <summary>The requester took the pending request back (spec section ID-3). No permission changed.</summary>
    Withdrawn,
}
