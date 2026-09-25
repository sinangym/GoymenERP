using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Shell;

/// <summary>
/// ACC-V0: Accounting's screens in the shell (UI catalog / Master §27): M-07 Dönem (the periods), M-04
/// Muhasebe Fişleri (the journals, read-only), M-06 Mizan and M-01 Muhasebe Kontrol Merkezi
/// (the posting queues, read-only). The ids and Turkish paths follow the Cari / Finance convention.
/// <para>
/// <b>Screen ids:</b> one per route. The catalog ids name the lists; the other routes carry technical
/// suffixes of the same screen (the Finance precedent): M-07-NEW, M-04-DETAIL. The approval and the close
/// are controls of the M-07 list and keep its screen.
/// </para>
/// <para>
/// <b>Published (ACC-V0c/ACC-V1), except the journal detail and M-06:</b> the declarations acceptance actually walked on the
/// normal profile are <c>ImplementationAvailable: true</c> — the period list and its propose screen, the approve
/// and close controls, the M-04 journal list and the M-01 queues. The journal DETAIL stays staged: acc01 opened
/// no journal (its seed writes nothing and no flow produces one before ACC-V1), so nothing accepted that route.
/// M-06 stays staged until its own real browser acceptance.
/// The staged-then-flip order is the FIN-UI-1c / MFG-UI-F pattern (card FIN-UI-1 §6). The approval
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
    public const string OwnerContractVersion = "1.2";

    public const string PeriodListScreenId = "M-07";
    public const string PeriodProposeScreenId = "M-07-NEW";
    public const string JournalListScreenId = "M-04";
    public const string JournalDetailScreenId = "M-04-DETAIL";
    public const string PostingQueuesScreenId = "M-01";
    public const string TrialBalanceScreenId = "M-06";

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

    public const string TrialBalanceCapabilityId = "accounting.trial-balance";
    public const string TrialBalanceRouteId = "accounting.trial-balance";
    public const string TrialBalanceRoutePath = "/muhasebe/mizan";

    public const string NormalDraftCreateScreenId = "M-04-NEW";
    public const string NormalDraftDetailScreenId = "M-04-DRAFT";
    public const string NormalDraftApproveScreenId = "M-04-APPROVE";
    public const string NormalDraftCreateCapabilityId = "accounting.normal-journal-drafts.create";
    public const string NormalDraftCreateRouteId = "accounting.normal-journal-drafts.new";
    public const string NormalDraftCreateRoutePath = "/muhasebe/normal-fisler/yeni";
    public const string NormalDraftDetailCapabilityId = "accounting.normal-journal-drafts.detail";
    public const string NormalDraftDetailRouteId = "accounting.normal-journal-drafts.detail";
    public const string NormalDraftDetailRoutePath = "/muhasebe/normal-fisler/:draftId";
    public const string NormalDraftApproveCapabilityId = "accounting.normal-journal-drafts.approve";
    public const string NormalDraftApproveRouteId = "accounting.normal-journal-drafts.approve";
    public const string NormalDraftApproveRoutePath = "/muhasebe/normal-fis-onay/:draftId";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var periods = Declaration(PeriodListCapabilityId, PeriodListRouteId, PeriodListRoutePath, PeriodListScreenId,
            ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PeriodsRead], available: true);
        return
        [
            periods,
            Declaration(PeriodProposeCapabilityId, PeriodProposeRouteId, PeriodProposeRoutePath, PeriodProposeScreenId,
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PeriodsPropose], available: true),
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
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.JournalsRead], available: true),
            // STILL STAGED (ACC-V0c): acc01 never opened a journal, because its seed writes nothing and no flow
            // produces a journal before ACC-V1 — so this route has no acceptance to stand on. Publishing it would
            // claim a screen nobody walked. It flips with the slice that first produces a journal, and the M-04
            // list does not link into it while it is closed.
            Declaration(JournalDetailCapabilityId, JournalDetailRouteId, JournalDetailRoutePath, JournalDetailScreenId,
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [AccountingPermissionManifestV1.JournalsRead],
                available: false),
            Declaration(NormalDraftCreateCapabilityId, NormalDraftCreateRouteId, NormalDraftCreateRoutePath,
                NormalDraftCreateScreenId, ShellCapabilityRoutePrecedence.Static,
                [NormalJournalPermissionNamesV1.DraftCreate], available: true),
            Declaration(NormalDraftDetailCapabilityId, NormalDraftDetailRouteId, NormalDraftDetailRoutePath,
                NormalDraftDetailScreenId, ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [NormalJournalPermissionNamesV1.DraftCreate], available: true),
            Declaration(NormalDraftApproveCapabilityId, NormalDraftApproveRouteId, NormalDraftApproveRoutePath,
                NormalDraftApproveScreenId, ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [NormalJournalPermissionNamesV1.DraftApprove], available: true),
            Declaration(PostingQueuesCapabilityId, PostingQueuesRouteId, PostingQueuesRoutePath, PostingQueuesScreenId,
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PostingQueuesRead], available: true),
            Declaration(TrialBalanceCapabilityId, TrialBalanceRouteId, TrialBalanceRoutePath, TrialBalanceScreenId,
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.JournalsRead], available: false),
        ];
    }

    // Accounting records are one company's and have no site dimension: a single company, company-wide.
    private static ShellCapabilityDeclaration Declaration(string capabilityId, string routeId, string routePath,
        string screenId, ShellCapabilityRoutePrecedence precedence, IReadOnlyCollection<string> permissions,
        bool available) =>
        new(capabilityId, routeId, routePath, screenId, precedence,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide, permissions,
            ImplementationAvailable: available);
}
