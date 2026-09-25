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
/// <b>Published (ACC-V0c/ACC-V1):</b> the period list and its propose screen, the approve and close controls, the
/// M-04 journal list, the M-01 queues, and — ACC-V1 reversal slice — the M-04 journal DETAIL and M-06 Mizan, whose
/// real read routes (`GET /accounting/journals/{id}`, `GET /accounting/trial-balance`) and screens exist. Their
/// browser walk is the acc01 reversal case (ACC01-09), which needs the synthetic posted-journal fixture; the flip is
/// merge-gated on that hosted acceptance.
/// <b>Staged:</b> the reversal PROPOSE control of the journal detail (the POST exists; not yet browser-walked) and the
/// reversal APPROVAL route, which has no reopen read (`GET /accounting/journal-reversal-proposals/{id}`) yet.
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

    // ACC-V1 journal reversal (#66/5827281417): propose and approve are two actions of two people.
    public const string JournalReversalApproveScreenId = "M-04-REVERSAL-APPROVE";
    public const string JournalReversalProposeCapabilityId = "accounting.journal-reversals.propose";
    public const string JournalReversalApproveCapabilityId = "accounting.journal-reversals.approve";
    public const string JournalReversalApproveRouteId = "accounting.journal-reversals.approve";
    public const string JournalReversalApproveRoutePath = "/muhasebe/ters-kayit-onay/:proposalId";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var periods = Declaration(PeriodListCapabilityId, PeriodListRouteId, PeriodListRoutePath, PeriodListScreenId,
            ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.PeriodsRead], available: true);
        var journalDetail = Declaration(JournalDetailCapabilityId, JournalDetailRouteId, JournalDetailRoutePath,
            JournalDetailScreenId, ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            [AccountingPermissionManifestV1.JournalsRead], available: true);
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
            // ACC-V1 reversal slice: the detail flips with the slice that first produces a journal (the acc01
            // synthetic posted-journal fixture, ACC01-09). Its real read route exists.
            journalDetail,
            // The reversal proposal is a CONTROL of the detail: its route, screen and scope, plus the propose name.
            // Staged until acc01 walks it.
            journalDetail with
            {
                CapabilityId = JournalReversalProposeCapabilityId,
                ParentCapabilityId = JournalDetailCapabilityId,
                RequiredPermissions =
                [
                    AccountingPermissionManifestV1.JournalsRead, AccountingJournalReversalPermissionNamesV1.Propose,
                ],
                ImplementationAvailable = false,
            },
            // Staged: the approval screen reopens a proposal by id, and the owner read it stands on
            // (GET /accounting/journal-reversal-proposals/{id}) does not exist yet.
            Declaration(JournalReversalApproveCapabilityId, JournalReversalApproveRouteId, JournalReversalApproveRoutePath,
                JournalReversalApproveScreenId, ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
                [AccountingJournalReversalPermissionNamesV1.Approve], available: false),
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
            // M-06 Mizan: the real read route and screen exist (#456); walked by acc01 ACC01-09 over the fixture journal.
            Declaration(TrialBalanceCapabilityId, TrialBalanceRouteId, TrialBalanceRoutePath, TrialBalanceScreenId,
                ShellCapabilityRoutePrecedence.Static, [AccountingPermissionManifestV1.JournalsRead], available: true),
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
