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
import {
  carriedStockSearch,
  normalizeStockSearch,
  parseRowActionTarget,
  permittedStockRowActions,
  ROW_ACTION_TARGET,
  STOCK_ROW_ACTIONS,
  stockPositionsPath,
  stockRowActionPath,
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
    expect(ids([INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY])).toEqual(['tahsis'])
    expect(ids([INVENTORY_LABEL_TRANSFORMATION_CAPABILITY])).toEqual(['bol-birlestir'])
    expect(ids(STOCK_ROW_ACTIONS.flatMap((action) => action.anyOf)))
      .toEqual(['label', 'tahsis', 'sayim', 'hurda', 'bol-birlestir', 'soy-agaci'])
  })

  it('offers no undecided action (production plan, sales allocation, loading instruction, release, transfer)', () => {
    const labels = STOCK_ROW_ACTIONS.map((action) => action.label.toLocaleLowerCase('tr-TR'))
    for (const undecided of ['üretim', 'satışa', 'yükleme', 'serbest', 'transfer', 'fason', 'blokaj']) {
      expect(labels.some((label) => label.includes(undecided))).toBe(false)
    }
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
