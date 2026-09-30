import { describe, expect, it } from 'vitest'
import {
  INVENTORY_LABEL_TRANSFORMATION_CAPABILITY,
  INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY,
  INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY,
  INVENTORY_STOCK_COUNT_RECORD_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
  INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY,
  INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY,
} from './capabilities'
import { MANUFACTURING_CAPABILITY, parsePlanCreateLabel } from '../manufacturing/capabilities'
import {
  carriedStockSearch,
  normalizeStockSearch,
  parseRowActionTarget,
  permittedStockRowActions,
  ROW_ACTION_TARGET,
  STOCK_ROW_ACTIONS,
  stockPositionsPath,
  stockRowActionHref,
  stockRowActionPath,
  stockRowActionsFor,
  stockRowActionState,
} from './stockRowActions'

const LABEL = '66666666-6666-4666-8666-666666666666'
const ids = (granted: readonly string[]) => permittedStockRowActions((capability) => granted.includes(capability)).map((action) => action.id)

describe('stock row actions (AT-063)', () => {
  it('offers nothing without a server capability and never mints one', () => {
    expect(ids([])).toEqual([])
    expect(ids(['inventory.unknown'])).toEqual([])
  })

  it('offers each action only for the capability that already gates its ST-02 control', () => {
    expect(ids([INVENTORY_STOCK_POSITIONS_CAPABILITY])).toEqual(['label', 'soy-agaci'])
    expect(ids([INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_STOCK_COUNT_RECORD_CAPABILITY]))
      .toEqual(['label', 'sayim', 'soy-agaci'])
    expect(ids([INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY])).toEqual(['sayim'])
    expect(ids([INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY])).toEqual(['hurda'])
    expect(ids([INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY])).toEqual(['hurda'])
    expect(ids([INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY])).toEqual(['tahsis', 'serbest-birak'])
    expect(ids([INVENTORY_LABEL_TRANSFORMATION_CAPABILITY])).toEqual(['bol-birlestir'])
    expect(ids([MANUFACTURING_CAPABILITY.planCreate])).toEqual(['uretim-planla'])
    // A neighbouring Manufacturing capability does not offer the plan action; only plan-create does.
    expect(ids([MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planEdit])).toEqual([])
    expect(ids(STOCK_ROW_ACTIONS.flatMap((action) => action.anyOf)))
      .toEqual(['label', 'tahsis', 'serbest-birak', 'uretim-planla', 'sayim', 'hurda', 'bol-birlestir', 'soy-agaci'])
  })

  it('offers release only on a row that holds active allocation kg', () => {
    const all = permittedStockRowActions(() => true)
    const row = (activeAllocationKg: string) => ({ labelId: LABEL, labelCode: 'LBL-1', activeAllocationKg })
    expect(stockRowActionsFor(all, row('0')).map((action) => action.id)).not.toContain('serbest-birak')
    expect(stockRowActionsFor(all, row('0.001')).map((action) => action.id)).toContain('serbest-birak')
    expect(stockRowActionsFor(all, row('30')).map((action) => action.id)).toContain('serbest-birak')
    // Everything else on the row is unaffected by its allocation kg.
    expect(stockRowActionsFor(all, row('0'))).toHaveLength(all.length - 1)
  })

  it('offers no undecided action (sales allocation, loading instruction, transfer, external processing, block)', () => {
    const labels = STOCK_ROW_ACTIONS.map((action) => action.label.toLocaleLowerCase('tr-TR'))
    for (const undecided of ['satışa', 'yükleme', 'transfer', 'fason', 'blokaj']) {
      expect(labels.some((label) => label.includes(undecided))).toBe(false)
    }
  })

  it('opens the plan-create screen with the row roll preselected, and every other action on ST-02', () => {
    const row = { labelId: LABEL, labelCode: 'LBL 1', activeAllocationKg: '5' }
    const byId = (id: string) => STOCK_ROW_ACTIONS.find((action) => action.id === id)!
    const plan = stockRowActionHref(byId('uretim-planla'), row)
    expect(plan).toBe(`/uretim/plan/yeni?etiket=${LABEL}&kod=LBL+1`)
    expect(parsePlanCreateLabel(plan.slice(plan.indexOf('?')))).toEqual({ labelId: LABEL, labelCode: 'LBL 1' })
    expect(stockRowActionHref(byId('serbest-birak'), row)).toBe(`/stok/etiket/${LABEL}?islem=serbest-birak`)
    expect(stockRowActionHref(byId('label'), row)).toBe(`/stok/etiket/${LABEL}`)
    expect(parseRowActionTarget('?islem=serbest-birak')).toBe('serbest-birak')
  })

  it('opens the row label, with the panel named in the URL', () => {
    expect(stockRowActionPath(LABEL, null)).toBe(`/stok/etiket/${LABEL}`)
    expect(stockRowActionPath(LABEL, ROW_ACTION_TARGET.count)).toBe(`/stok/etiket/${LABEL}?islem=sayim`)
    expect(parseRowActionTarget('?islem=sayim')).toBe('sayim')
    expect(parseRowActionTarget('?islem=tahsis&x=1')).toBe('tahsis')
    expect(parseRowActionTarget('?islem=uretim-planla')).toBeNull()
    expect(parseRowActionTarget('')).toBeNull()
  })

  it('keeps the ST-01 filter across the action and back, bounded and trimmed', () => {
    expect(stockPositionsPath('')).toBe('/stok/pozisyonlar')
    expect(stockPositionsPath(' LBL 1 ')).toBe('/stok/pozisyonlar?etiket=LBL+1')
    expect(stockPositionsPath('x'.repeat(201))).toBe('/stok/pozisyonlar')
    expect(normalizeStockSearch(null)).toBe('')
    expect(stockRowActionState(' LBL ')).toEqual({ stockPositionsSearch: 'LBL' })
    expect(carriedStockSearch({ stockPositionsSearch: 'LBL' })).toBe('LBL')
    expect(carriedStockSearch({ stockPositionsSearch: '' })).toBe('')
    expect(carriedStockSearch(null)).toBeNull()
    expect(carriedStockSearch({ stockPositionsSearch: 3 })).toBeNull()
    expect(carriedStockSearch({ other: 'LBL' })).toBeNull()
  })
})
