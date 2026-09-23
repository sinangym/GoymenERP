import { afterEach, describe, expect, it } from 'vitest'
import {
  lastOpeningCountKey,
  pendingOpeningCountKey,
  recallLastOpeningCount,
  recallPendingOpeningCount,
  releasePendingOpeningCount,
  rememberLastOpeningCount,
  reservePendingOpeningCount,
} from './pendingOpeningStore'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const COUNT = '11111111-1111-4111-8111-111111111111'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LINE = '55555555-5555-4555-8555-555555555555'
const LABEL = '66666666-6666-4666-8666-666666666666'
const LOT = '77777777-7777-4777-8777-777777777777'
const PRODUCT = '88888888-8888-4888-8888-888888888888'
const KEY = '99999999-9999-4999-8999-999999999999'
const ACTOR = 'actor-a'

const snapshot = {
  actorId: ACTOR, companyId: COMPANY, idempotencyKey: KEY,
  payload: {
    countId: COUNT, locationId: LOCATION, countedOn: '2026-09-23', sheetReference: 'SAYIM-1',
    lines: [{
      lineId: LINE, labelId: LABEL, labelCode: 'LBL-1', lotId: LOT, heatNumber: null, coilNumber: null,
      productId: PRODUCT, productVersion: '7', quantityKg: '12.5',
    }],
  },
}

afterEach(() => sessionStorage.clear())

describe('Inventory opening session stores', () => {
  it('keeps one immutable command until the same key releases it', () => {
    const key = pendingOpeningCountKey(4, ACTOR, COMPANY)
    reservePendingOpeningCount(key, snapshot)
    expect(recallPendingOpeningCount(key)).toEqual(snapshot)
    releasePendingOpeningCount(key, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa')
    expect(recallPendingOpeningCount(key)).toEqual(snapshot)
    releasePendingOpeningCount(key, KEY)
    expect(recallPendingOpeningCount(key)).toBeNull()
  })

  it('remembers only a valid last record in the same session actor and company namespace', () => {
    const key = lastOpeningCountKey(4, ACTOR, COMPANY)
    rememberLastOpeningCount(key, { actorId: ACTOR, companyId: COMPANY, countId: COUNT })
    expect(recallLastOpeningCount(key)?.countId).toBe(COUNT)
    sessionStorage.setItem(key!, JSON.stringify({ actorId: ACTOR, companyId: COMPANY, countId: 'bad' }))
    expect(recallLastOpeningCount(key)).toBeNull()
  })
})
