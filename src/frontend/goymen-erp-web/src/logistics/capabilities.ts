/**
 * Logistics loading-instruction screen mount ids and routes — the frontend's side of the LOG-4
 * card (D6; master v3 §27.8 screen ids), pinned to the backend's `LogisticsShellCapabilityProvider`
 * (LOG-4a, #350) by ./capabilities.test.ts.
 *
 * STATUS: DECLARED, STAGED. The provider declares all three with `ImplementationAvailable: false`
 * until the `l04` browser suite accepts the screens on the staged profile; a separate exact-head
 * change flips them (the S03-c / FIN-UI-1c / MFG-UI-F pattern). While staged, `hasCapability`
 * answers false and `CapabilityRoute` closes every Logistics route onto the neutral not-found block.
 * The ids are mount keys, not permissions: every route's service measures its own Logistics
 * permission afresh (#276), and recording a load is a server-measured command on the detail
 * (`logistics.loads.record`), not a screen. The new-instruction picker reads the price-free Logistics
 * approved-order-lines read under the create grant (Sinan §4: the Sevkiyatçı sees orders, never prices);
 * no Sales grant is needed.
 */

export const LOGISTICS_CAPABILITY = {
  /** L-02 Yükleme Talimatları — the company's instructions, newest first. */
  instructionsList: 'logistics.instructions.list',
  /** L-03 Yeni Yükleme Talimatı — a Draft with lines on APPROVED sales lines. A SEPARATE grant from reading. */
  instructionsCreate: 'logistics.instructions.create',
  /** L-02-DETAIL — one instruction: the read view of one L-02 row (the F-02-DETAIL precedent). */
  instructionsDetail: 'logistics.instructions.detail',
} as const

export type LogisticsCapabilityId = (typeof LOGISTICS_CAPABILITY)[keyof typeof LOGISTICS_CAPABILITY]

export const LOGISTICS_ROUTE = {
  list: '/lojistik/yukleme-talimatlari',
  // Static before the parameter route: `yeni` must never be read as an instruction id.
  create: '/lojistik/yukleme-talimatlari/yeni',
  detail: '/lojistik/yukleme-talimatlari/:instructionId',
} as const

/** Master v3 §27.8 screen ids, one per route, as the backend provider declares them. */
export const LOGISTICS_SCREEN_ID = {
  [LOGISTICS_CAPABILITY.instructionsList]: 'L-02',
  [LOGISTICS_CAPABILITY.instructionsCreate]: 'L-03',
  [LOGISTICS_CAPABILITY.instructionsDetail]: 'L-02-DETAIL',
} as const

export const LOGISTICS_NAV_LABEL = {
  [LOGISTICS_CAPABILITY.instructionsList]: 'Yükleme Talimatları',
  // No nav entry: one entry point per creation route (coordinator, 19 September 2026) — the L-02 list carries
  // its own primary link to L-03, as Finance and Accounting do; a second sidebar link with the same accessible
  // name is what l04's strict-mode violation found. The shell's table must say the same (logisticsMountDrift).
  [LOGISTICS_CAPABILITY.instructionsCreate]: '',
  // No nav entry: an instruction is opened from the list row.
  [LOGISTICS_CAPABILITY.instructionsDetail]: '',
} as const

export const instructionDetailRoute = (instructionId: string) =>
  `/lojistik/yukleme-talimatlari/${encodeURIComponent(instructionId)}`
