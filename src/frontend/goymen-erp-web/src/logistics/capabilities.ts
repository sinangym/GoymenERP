/**
 * Logistics loading-instruction screen mount ids and routes — the frontend's side of the LOG-4
 * card (D6; master v3 §27.8 screen ids), pinned to the backend's `LogisticsShellCapabilityProvider`
 * (LOG-4a, #350) by ./capabilities.test.ts.
 *
 * STATUS: PUBLISHED. The `l04` browser suite accepted all three screens on the staged profile at
 * exact `af2072ce1f5ca17b8124fadc81cd83ea7c32fd92` (run 35484271018, 5/5), so the owner provider now
 * declares them with `ImplementationAvailable: true` (the S03-c / FIN-UI-1c / MFG-UI-F pattern).
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
  /**
   * AT-025 L-01 — the operations work list's SALES section (approved sales order lines to load, ship, deliver). Its own
   * grant (`logistics.operations-work.sales.read`); it opens nothing of the purchase section.
   */
  operationsSales: 'logistics.operations-work.sales',
  /**
   * AT-025 L-09 — the operations work list's PURCHASE section (approved purchase order lines waiting for goods receipt).
   * Its own grant (`logistics.operations-work.purchase.read`); it opens nothing of the sales section.
   */
  operationsPurchase: 'logistics.operations-work.purchase',
} as const

export type LogisticsCapabilityId = (typeof LOGISTICS_CAPABILITY)[keyof typeof LOGISTICS_CAPABILITY]

export const LOGISTICS_ROUTE = {
  list: '/lojistik/yukleme-talimatlari',
  // Static before the parameter route: `yeni` must never be read as an instruction id.
  create: '/lojistik/yukleme-talimatlari/yeni',
  detail: '/lojistik/yukleme-talimatlari/:instructionId',
  operationsSales: '/lojistik/operasyon-isleri/satis',
  operationsPurchase: '/lojistik/operasyon-isleri/satinalma',
} as const

/** Master v3 §27.8 screen ids, one per route, as the backend provider declares them. */
export const LOGISTICS_SCREEN_ID = {
  [LOGISTICS_CAPABILITY.instructionsList]: 'L-02',
  [LOGISTICS_CAPABILITY.instructionsCreate]: 'L-03',
  [LOGISTICS_CAPABILITY.instructionsDetail]: 'L-02-DETAIL',
  [LOGISTICS_CAPABILITY.operationsSales]: 'L-01',
  [LOGISTICS_CAPABILITY.operationsPurchase]: 'L-09',
} as const

export const LOGISTICS_NAV_LABEL = {
  [LOGISTICS_CAPABILITY.instructionsList]: 'Yükleme Talimatları',
  // No nav entry: one entry point per creation route (coordinator, 19 September 2026) — the L-02 list carries
  // its own primary link to L-03, as Finance and Accounting do; a second sidebar link with the same accessible
  // name is what l04's strict-mode violation found. The shell's table must say the same (logisticsMountDrift).
  [LOGISTICS_CAPABILITY.instructionsCreate]: '',
  // No nav entry: an instruction is opened from the list row.
  [LOGISTICS_CAPABILITY.instructionsDetail]: '',
  // AT-025: one entry per section, because each section is its own grant.
  [LOGISTICS_CAPABILITY.operationsSales]: 'Operasyon işleri — Satış',
  [LOGISTICS_CAPABILITY.operationsPurchase]: 'Operasyon işleri — Satın alma',
} as const

export const instructionDetailRoute = (instructionId: string) =>
  `/lojistik/yukleme-talimatlari/${encodeURIComponent(instructionId)}`
