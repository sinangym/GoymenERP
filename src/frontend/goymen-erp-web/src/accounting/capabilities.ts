/**
 * Accounting screen mount ids and routes — exactly what the backend's staged
 * `AccountingShellCapabilityProvider` declares (ACC-V0), pinned by a C# test to this file.
 *
 * STATUS: DECLARED, STAGED. Every declaration is `ImplementationAvailable: false` until the ACC-01
 * browser acceptance; the shell never publishes a staged declaration, so in the normal runtime
 * `CapabilityRoute` closes every Accounting route onto the neutral not-found block and calls nothing.
 * The ids are mount keys, not permissions: each route's service measures its own narrow permission.
 *
 * The approval (Ö4) and the close (Ö5) are CONTROLS of the M-07 period list (the provider's
 * `ParentCapabilityId`), never routes: `hasCapability` checks inside the list. The server refuses a
 * self-approval anyway.
 */

export const ACCOUNTING_CAPABILITY = {
  /** M-07 Dönem — the company's accounting periods. */
  periodsList: 'accounting.periods.list',
  /** M-07 — propose a period opening. */
  periodsPropose: 'accounting.periods.propose',
  /** M-07 — the approval control of the list (Ö4: someone other than the preparer; not a route). */
  periodsApprove: 'accounting.periods.approve',
  /** M-07 — the close control of the list (Ö5; not a route). */
  periodsClose: 'accounting.periods.close',
  /** M-04 Muhasebe Fişleri — the journals, read-only. */
  journalsList: 'accounting.journals.list',
  /** M-04 — one journal, read-only. */
  journalsDetail: 'accounting.journals.detail',
  /** ACC-V1 — create a normal journal draft from server-approved accounting sources. */
  normalDraftCreate: 'accounting.normal-journal-drafts.create',
  /** ACC-V1 — reopen a draft prepared by the current user. */
  normalDraftDetail: 'accounting.normal-journal-drafts.detail',
  /** ACC-V1 — read and approve a company draft with the narrow approval permission. */
  normalDraftApprove: 'accounting.normal-journal-drafts.approve',
  /** M-01 Muhasebe Kontrol Merkezi — the posting queues, read-only. */
  postingQueues: 'accounting.posting-queues',
} as const

export type AccountingCapabilityId = (typeof ACCOUNTING_CAPABILITY)[keyof typeof ACCOUNTING_CAPABILITY]

export const ACCOUNTING_ROUTE = {
  periodsList: '/muhasebe/donemler',
  // Static before the parameter-free list's siblings; `yeni` is its own route.
  periodsPropose: '/muhasebe/donemler/yeni',
  journalsList: '/muhasebe/fisler',
  journalsDetail: '/muhasebe/fisler/:journalId',
  normalDraftCreate: '/muhasebe/normal-fisler/yeni',
  normalDraftDetail: '/muhasebe/normal-fisler/:draftId',
  normalDraftApprove: '/muhasebe/normal-fis-onay/:draftId',
  postingQueues: '/muhasebe/kontrol',
} as const

export const journalDetailPath = (journalId: string) => `/muhasebe/fisler/${encodeURIComponent(journalId)}`

export const normalDraftDetailPath = (draftId: string) =>
  `/muhasebe/normal-fisler/${encodeURIComponent(draftId)}`

export const normalDraftApprovalPath = (draftId: string) =>
  `/muhasebe/normal-fis-onay/${encodeURIComponent(draftId)}`

export const periodJournalsPath = (periodId: string) =>
  `/muhasebe/fisler?${new URLSearchParams({ periodId }).toString()}`
