/**
 * AT-063 / Master v3 §25.4 — the ST-01 stock row as a contextual action centre, READ/NAVIGATION only.
 *
 * A row action opens an EXISTING screen or ST-02 panel with the row's label already chosen; it never runs a command
 * itself and never mints an authority. Each action is offered only when the server's capability snapshot carries the
 * capability that already gates the target control on ST-02 (the backend re-measures its own permission on every
 * command, so a hidden action is a convenience, never the fence). No new write policy lives here.
 *
 * Offered (decided and built on ST-02): open the label (ST-02), its genealogy, view/revise its sales allocations
 * (INV-AL-1), stock count (ST-05 / AT-037), scrap write-off, split/merge (R19).
 * NOT offered — no row-context target exists yet, see the lane report: "Üretim planla" (PR-03 takes no preselected
 * label), "Satışa tahsis et" and "Yükleme talimatı oluştur" (both start from a Sales line, not a label), release of an
 * allocation (no release command), transfer / external processing and quality block (no screens).
 */
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
} as const

export type RowActionTarget = (typeof ROW_ACTION_TARGET)[keyof typeof ROW_ACTION_TARGET]

const TARGETS: readonly RowActionTarget[] = Object.values(ROW_ACTION_TARGET)

export interface StockRowAction {
  readonly id: 'label' | RowActionTarget
  readonly label: string
  /** Any ONE of these capabilities offers the action (e.g. a count reader may record OR approve). */
  readonly anyOf: readonly string[]
  readonly target: RowActionTarget | null
}

/** The decided row actions, in the order §25.4 lists them. */
export const STOCK_ROW_ACTIONS: readonly StockRowAction[] = [
  { id: 'label', label: 'Etiket 360’ı aç', anyOf: [INVENTORY_STOCK_POSITIONS_CAPABILITY], target: null },
  {
    id: ROW_ACTION_TARGET.allocations, label: 'Tahsisi görüntüle / revize et',
    anyOf: [INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY], target: ROW_ACTION_TARGET.allocations,
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
  return STOCK_ROW_ACTIONS.filter((action) => action.anyOf.some(has))
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
