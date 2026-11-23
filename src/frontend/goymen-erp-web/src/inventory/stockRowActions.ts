/**
 * AT-063 / Master v3 §25.4 — the ST-01 stock row as a contextual action centre, READ/NAVIGATION only.
 *
 * A row action opens an EXISTING screen or ST-02 panel with the row's label already chosen; it never runs a command
 * itself and never mints an authority. Each action is offered only when the server's capability snapshot carries the
 * capability that already gates the target control (the backend re-measures its own permission on every command, so
 * a hidden action is a convenience, never the fence). No new write policy lives here.
 *
 * Offered (decided and built): open the label (ST-02), its genealogy, view/revise its sales allocations (INV-AL-1),
 * release an allocation (the EXISTING Inventory release command, on ST-02; offered only on a row that holds active
 * allocation kg), stock count (ST-05 / AT-037), scrap write-off, split/merge (R19), and "Üretim planla" (the
 * Manufacturing plan-create screen with the row's roll preselected through Inventory's candidate read).
 * KLN-D117 (Sinan 2026-11-21): "Satışa tahsis et" and "Yükleme talimatı oluştur" also start from a label row. They only
 * navigate: the first to the approved sales orders with the label code carried into the chosen order's allocation search,
 * the second to the new loading instruction narrowed to the label's product. The user still chooses the order line and the
 * command is the same server command as from the order line (authority, quantity, ownership and company rules unchanged).
 * NOT offered — transfer / external processing and quality block (no screens).
 */
import { LOGISTICS_CAPABILITY, instructionCreateFromLabelPath } from '../logistics/capabilities'
import { MANUFACTURING_CAPABILITY, planCreateFromLabelPath } from '../manufacturing/capabilities'
import { SALES_CAPABILITY, salesAllocationFromLabelPath } from '../sales/capabilities'
import {
  INVENTORY_LABEL_TRANSFORMATION_CAPABILITY,
  INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY,
  INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY,
  INVENTORY_STOCK_COUNT_RECORD_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_ROUTE,
  INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY,
  INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY,
} from './capabilities'
import { inventoryLabelDetailPath } from './labelDetailRoute'

/** The ST-02 search parameter naming the panel a row action opened. */
export const ROW_ACTION_PARAM = 'islem' as const
/** The ST-01 search parameter that keeps the label-code filter across a row action and back. */
export const STOCK_SEARCH_PARAM = 'etiket' as const
/** A label-code filter longer than this is not carried (the ST-01 field is a short code search). */
export const STOCK_SEARCH_MAX_LENGTH = 200

/** ST-02 panels a row action can open; the value is the URL token. */
export const ROW_ACTION_TARGET = {
  genealogy: 'soy-agaci',
  allocations: 'tahsis',
  count: 'sayim',
  scrap: 'hurda',
  transformation: 'bol-birlestir',
  /** The allocation panel with the release form of the row's one active allocation open. */
  release: 'serbest-birak',
} as const

export type RowActionTarget = (typeof ROW_ACTION_TARGET)[keyof typeof ROW_ACTION_TARGET]

const TARGETS: readonly RowActionTarget[] = Object.values(ROW_ACTION_TARGET)

/** The row action that leaves Inventory for the Manufacturing plan-create screen. */
export const PLAN_PRODUCTION_ACTION_ID = 'uretim-planla' as const
/** KLN-D117: the row action that leaves Inventory for the approved sales orders (allocation from the label). */
export const SALES_ALLOCATION_ACTION_ID = 'satisa-tahsis' as const
/** KLN-D117: the row action that leaves Inventory for the new loading instruction (narrowed to the label's product). */
export const LOADING_INSTRUCTION_ACTION_ID = 'yukleme-talimati' as const

export interface StockRowAction {
  readonly id: 'label' | RowActionTarget | typeof PLAN_PRODUCTION_ACTION_ID | typeof SALES_ALLOCATION_ACTION_ID
    | typeof LOADING_INSTRUCTION_ACTION_ID
  readonly label: string
  /** Any ONE of these capabilities offers the action (e.g. a count reader may record OR approve). */
  readonly anyOf: readonly string[]
  /** And EVERY one of these (the screens the action passes through must open for the user). */
  readonly allOf?: readonly string[]
  /** The ST-02 panel it opens; null for the label itself or for a screen outside ST-02. */
  readonly target: RowActionTarget | null
  /** Offered only on a row that holds active allocation kg (nothing to release otherwise). */
  readonly needsActiveAllocation?: true
}

/** What a row action needs of its row: the label, and whether it holds active allocation kg. */
export interface StockRowContext {
  readonly labelId: string
  readonly labelCode: string
  readonly productId: string
  readonly activeAllocationKg: string
}

/** The decided row actions, in the order §25.4 lists them. */
export const STOCK_ROW_ACTIONS: readonly StockRowAction[] = [
  { id: 'label', label: 'Etiket 360’ı aç', anyOf: [INVENTORY_STOCK_POSITIONS_CAPABILITY], target: null },
  {
    id: ROW_ACTION_TARGET.allocations, label: 'Tahsisi görüntüle / revize et',
    anyOf: [INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY], target: ROW_ACTION_TARGET.allocations,
  },
  {
    id: ROW_ACTION_TARGET.release, label: 'Tahsisi serbest bırak',
    anyOf: [INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY], target: ROW_ACTION_TARGET.release, needsActiveAllocation: true,
  },
  {
    id: PLAN_PRODUCTION_ACTION_ID, label: 'Üretim planla',
    anyOf: [MANUFACTURING_CAPABILITY.planCreate], target: null,
  },
  {
    // The allocation panel of S-04 opens on the physical allocation write; the user reaches it through S-02 and S-04.
    id: SALES_ALLOCATION_ACTION_ID, label: 'Satışa tahsis et',
    anyOf: [INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY],
    allOf: [SALES_CAPABILITY.draftsList, SALES_CAPABILITY.draftsDetail], target: null,
  },
  {
    id: LOADING_INSTRUCTION_ACTION_ID, label: 'Yükleme talimatı oluştur',
    anyOf: [LOGISTICS_CAPABILITY.instructionsCreate], target: null,
  },
  {
    id: ROW_ACTION_TARGET.count, label: 'Sayım / fark',
    anyOf: [INVENTORY_STOCK_COUNT_RECORD_CAPABILITY, INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY], target: ROW_ACTION_TARGET.count,
  },
  {
    id: ROW_ACTION_TARGET.scrap, label: 'Hurda / fire çıkışı',
    anyOf: [INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY, INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY], target: ROW_ACTION_TARGET.scrap,
  },
  {
    id: ROW_ACTION_TARGET.transformation, label: 'Böl / birleştir',
    anyOf: [INVENTORY_LABEL_TRANSFORMATION_CAPABILITY], target: ROW_ACTION_TARGET.transformation,
  },
  {
    id: ROW_ACTION_TARGET.genealogy, label: 'Etiket / lot soy ağacı',
    anyOf: [INVENTORY_STOCK_POSITIONS_CAPABILITY], target: ROW_ACTION_TARGET.genealogy,
  },
]

/** The actions the server's capability snapshot permits, in menu order. */
export function permittedStockRowActions(has: (capability: string) => boolean): readonly StockRowAction[] {
  return STOCK_ROW_ACTIONS.filter((action) => action.anyOf.some(has) && (action.allOf ?? []).every(has))
}

/** The permitted actions that apply to ONE row (release only where the row holds active allocation kg). */
export function stockRowActionsFor(actions: readonly StockRowAction[], row: StockRowContext): readonly StockRowAction[] {
  const allocated = /[1-9]/.test(row.activeAllocationKg)
  return actions.filter((action) => action.needsActiveAllocation !== true || allocated)
}

/** Where a row action goes: the plan-create screen with the row's roll, or ST-02 (with its panel). */
export function stockRowActionHref(action: StockRowAction, row: StockRowContext): string {
  if (action.id === PLAN_PRODUCTION_ACTION_ID) return planCreateFromLabelPath(row.labelId, row.labelCode)
  if (action.id === SALES_ALLOCATION_ACTION_ID) return salesAllocationFromLabelPath(row.labelCode)
  if (action.id === LOADING_INSTRUCTION_ACTION_ID) return instructionCreateFromLabelPath(row.labelCode, row.productId)
  return stockRowActionPath(row.labelId, action.target)
}

/** The ST-02 path a row action opens: the row's label, and the panel when the action names one. */
export function stockRowActionPath(labelId: string, target: RowActionTarget | null): string {
  const path = inventoryLabelDetailPath(labelId)
  return target === null ? path : `${path}?${new URLSearchParams({ [ROW_ACTION_PARAM]: target }).toString()}`
}

/** The ST-02 panel named in a URL search string, or null for none / an unknown token. */
export function parseRowActionTarget(search: string): RowActionTarget | null {
  const value = new URLSearchParams(search).get(ROW_ACTION_PARAM)
  return TARGETS.find((target) => target === value) ?? null
}

/** A carried ST-01 filter: trimmed, bounded text; anything else is no filter. */
export function normalizeStockSearch(value: unknown): string {
  if (typeof value !== 'string') return ''
  const trimmed = value.trim()
  return trimmed.length <= STOCK_SEARCH_MAX_LENGTH ? trimmed : ''
}

/** The ST-01 path with its label-code filter kept (the back link of a row action). */
export function stockPositionsPath(search: string): string {
  const q = normalizeStockSearch(search)
  return q === ''
    ? INVENTORY_STOCK_POSITIONS_ROUTE
    : `${INVENTORY_STOCK_POSITIONS_ROUTE}?${new URLSearchParams({ [STOCK_SEARCH_PARAM]: q }).toString()}`
}

/** Router state a row action hands ST-02 so its back link returns to the same filtered ST-01. */
export interface StockRowActionState {
  readonly stockPositionsSearch: string
}

export function stockRowActionState(search: string): StockRowActionState {
  return { stockPositionsSearch: normalizeStockSearch(search) }
}

/** The ST-01 filter carried in router state, or null when ST-02 was not opened from an ST-01 row. */
export function carriedStockSearch(state: unknown): string | null {
  if (typeof state !== 'object' || state === null || !('stockPositionsSearch' in state)) return null
  const value = (state as { readonly stockPositionsSearch: unknown }).stockPositionsSearch
  return typeof value === 'string' && value.trim().length <= STOCK_SEARCH_MAX_LENGTH ? value.trim() : null
}
