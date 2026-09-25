import { matchPath } from 'react-router'
import { describe, expect, it } from 'vitest'
import { INVENTORY_LABEL_DETAIL_ROUTE, inventoryLabelDetailPath } from './labelDetailRoute'

describe('Inventory label detail route', () => {
  it('round-trips the exact label identity without adding a navigation entry', () => {
    const labelId = '22222222-2222-2222-2222-222222222222'
    const path = inventoryLabelDetailPath(labelId)
    expect(path).toBe(`/stok/etiket/${labelId}`)
    expect(matchPath(INVENTORY_LABEL_DETAIL_ROUTE, path)?.params.labelId).toBe(labelId)
  })
})
