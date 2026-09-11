/**
 * Synthetic fixtures for the T lane (controlled responses).
 *
 * These mirror the component harness of the F008 delivery
 * (`src/cari/commandPageHarness.ts`) so that what the real browser is fed has the
 * exact wire shapes the accepted backend contract defines. Nothing here is real
 * data; every id is a synthetic UUID. The L lane never uses this module.
 */

export const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
export const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
export const PARTY = '11111111-1111-1111-1111-111111111111'
export const PARTY_2 = '22222222-2222-2222-2222-222222222222'
export const ACC_A = 'cccccccc-1111-1111-1111-111111111111'
export const ACC_B = 'dddddddd-2222-2222-2222-222222222222'
export const ACC_A2 = 'eeeeeeee-2222-2222-2222-222222222222'

export const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Kullanıcı A',
  email: null,
  allowedCompanyIds: [A_ID, B_ID],
}

export const DIRECTORY = [
  { id: A_ID, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: B_ID, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

export const SHELL_CAPABILITY_CONTRACT_VERSION = 'SHELL-CAPABILITY-V1'

export const CAP = {
  list: 'cari.parties.list',
  create: 'cari.parties.create',
  link: 'cari.accounts.link',
  detail: 'cari.parties.detail',
} as const

export const ALL_CAPS: readonly string[] = [CAP.list, CAP.create, CAP.link, CAP.detail]
export const WITHOUT_DETAIL: readonly string[] = [CAP.list, CAP.create, CAP.link]

export const ROLE = { Customer: 1, Supplier: 2, Tolling: 4, Service: 8 } as const

export const PROBLEM = {
  invalidCursor: 'invalid_cursor',
  validationFailed: 'validation_failed',
  forbidden: 'forbidden',
  notFound: 'not_found',
  serviceUnavailable: 'service_unavailable',
  commandMetadataInvalid: 'command_metadata_invalid',
  idempotencyConflict: 'idempotency_conflict',
  alreadyLinked: 'already_linked',
} as const

export const LEGAL_NAME = 'Örnek Metal Sanayi ve Ticaret A.Ş.'
export const TRADE_NAME = 'Örnek Metal'

export function capabilitySnapshot(companyId: string | null, capabilities: readonly string[]) {
  return {
    contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
    generation: `gen-${companyId?.slice(-1) ?? 'x'}`,
    capabilities,
  }
}

/** `CounterpartyMutationView` — the 201 body of create/link, also accepted by the detail decoder. */
export function mutationView(over: Record<string, unknown> = {}) {
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: LEGAL_NAME,
    tradeName: TRADE_NAME,
    isActive: true,
    version: '1',
    companyAccounts: [
      { id: ACC_A, companyId: A_ID, roles: ROLE.Customer | ROLE.Supplier, isActive: true, version: '1' },
    ],
    ...over,
  }
}

/** The same Party linked into B: same id/code, only the B account. */
export function linkedView(roles: number = ROLE.Supplier, over: Record<string, unknown> = {}) {
  return mutationView({
    companyAccounts: [{ id: ACC_B, companyId: B_ID, roles, isActive: true, version: '1' }],
    ...over,
  })
}

/** `CounterpartyLinkCandidateItem` — visibleSourceAccounts, no Party version. */
export function candidate(over: Record<string, unknown> = {}) {
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: LEGAL_NAME,
    tradeName: TRADE_NAME,
    isActive: true,
    visibleSourceAccounts: [
      { id: ACC_A, companyId: A_ID, roles: ROLE.Customer | ROLE.Supplier, isActive: true, version: '1' },
    ],
    ...over,
  }
}

export function candidatePage(items: unknown[], over: Record<string, unknown> = {}) {
  return { items, nextCursor: null, hasMore: false, ...over }
}

/** An empty C-01 list page — the list is out of this task's scope and is only a landing surface here. */
export function emptyListPage() {
  return { items: [], nextCursor: null, hasMore: false, totalCount: '0', countScope: 'filtered-authorized-visible-parties' }
}
