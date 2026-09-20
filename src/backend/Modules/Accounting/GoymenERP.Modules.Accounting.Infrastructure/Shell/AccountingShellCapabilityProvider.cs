using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Shell;

/// <summary>
/// ACC-V0: Accounting's screens in the shell (UI catalog / Master §27): M-07 Dönem (the periods), M-04
/// Muhasebe Fişleri (the journals, read-only) and M-01 Muhasebe Kontrol Merkezi (the posting queues,
/// read-only). The ids and Turkish paths follow the Cari / Finance convention.
/// <para>
/// <b>Screen ids:</b> one per route. The catalog ids name the lists; the other routes carry technical
/// suffixes of the same screen (the Finance precedent): M-07-NEW, M-04-DETAIL. The approval and the close
/// are controls of the M-07 list and keep its screen.
/// </para>
/// <para>
/// <b>Staged first:</b> every declaration is <c>ImplementationAvailable: false</c> until the ACC-01 browser
/// acceptance; a separate flip PR makes them available (card FIN-UI-1 §6, the FIN-01 pattern). The approval
/// and the close are controls of the period list (<c>ParentCapabilityId</c>), not routes, and keep the
/// list's route, context and scope. A declaration grants nothing: each route's service measures its own
/// narrow permission afresh (persona rule: every read is its own name).
/// </para>
/// <para>
/// The host registers this provider together with its shell authority (the central writer's); this
/// module does not.
/// </para>
/// </summary>
public sealed class AccountingShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "ACCOUNTING";
    public const string OwnerContractVersion = "1.0";

    public const string PeriodListScreenId = "M-07";
    public const string PeriodProposeScreenId = "M-07-NEW";
    public const string JournalListScreenId = "M-04";
    public const string JournalDetailScreenId = "M-04-DETAIL";
    public const string PostingQueuesScreenId = "M-01";

    public const string PeriodListCapabilityId = "accounting.periods.list";
    public const string PeriodListRouteId = "accounting.periods";
    public const string PeriodListRoutePath = "/muhasebe/donemler";
    public const string PeriodProposeCapabilityId = "accounting.periods.propose";
    public const string PeriodProposeRouteId = "accounting.periods.new";
    public const string PeriodProposeRoutePath = "/muhasebe/donemler/yeni";
    public const string PeriodApproveCapabilityId = "accounting.periods.approve";
    public const string PeriodCloseCapabilityId = "accounting.periods.close";

    public const string JournalListCapabilityId = "accounting.journals.list";
    public const string JournalListRouteId = "accounting.journals";
    public const string JournalListRoutePath = "/muhasebe/fisler";
    public const string JournalDetailCapabilityId = "accounting.journals.detail";
    public const string JournalDetailRouteId = "accounting.journals.detail";
    public const string JournalDetailRoutePath = "/muhasebe/fisler/:journalId";

    public const string PostingQueuesCapabilityId = "accounting.posting-queues";
    public const string PostingQueuesRouteId = "accounting.posting-queues";
    public const string PostingQueuesRoutePath = "/muhasebe/kontrol";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var periods = Declaration(PeriodListCapabilityId, PeriodListRouteId, PeriodListRoutePath, PeriodListScreenId,
            ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PeriodsRead]);
        return
        [
            periods,
            Declaration(PeriodProposeCapabilityId, PeriodProposeRouteId, PeriodProposeRoutePath, PeriodProposeScreenId,
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PeriodsPropose]),
            // Ö4: the approval is a control on the list — the list's route, context and scope, plus the approve name.
            periods with
            {
                CapabilityId = PeriodApproveCapabilityId,
                ParentCapabilityId = PeriodListCapabilityId,
                RequiredPermissions = [AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsApprove],
            },
            // Ö5: the close is a control on the list too.
            periods with
            {
                CapabilityId = PeriodCloseCapabilityId,
                ParentCapabilityId = PeriodListCapabilityId,
                RequiredPermissions = [AccountingPermissionManifestV1.PeriodsRead, AccountingPermissionManifestV1.PeriodsClose],
            },
            Declaration(JournalListCapabilityId, JournalListRouteId, JournalListRoutePath, JournalListScreenId,
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.JournalsRead]),
            Declaration(JournalDetailCapabilityId, JournalDetailRouteId, JournalDetailRoutePath, JournalDetailScreenId,
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [AccountingPermissionManifestV1.JournalsRead]),
            Declaration(PostingQueuesCapabilityId, PostingQueuesRouteId, PostingQueuesRoutePath, PostingQueuesScreenId,
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PostingQueuesRead]),
        ];
    }

    // Accounting records are one company's and have no site dimension: a single company, company-wide.
    private static ShellCapabilityDeclaration Declaration(string capabilityId, string routeId, string routePath,
        string screenId, ShellCapabilityRoutePrecedence precedence, IReadOnlyCollection<string> permissions) =>
        new(capabilityId, routeId, routePath, screenId, precedence,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide, permissions,
            ImplementationAvailable: false);
}
