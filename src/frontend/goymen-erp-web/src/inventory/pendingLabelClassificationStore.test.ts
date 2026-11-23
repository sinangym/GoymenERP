import { afterEach, describe, expect, it } from 'vitest'
import {
  pendingLabelClassificationKey,
  recallPendingLabelClassification,
  releasePendingLabelClassification,
  reservePendingLabelClassification,
} from './pendingLabelClassificationStore'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const LABEL = '66666666-6666-4666-8666-666666666666'
const KEY = '99999999-9999-4999-8999-999999999999'
const ACTOR = 'SENTETIK-actor'

const attempt = {
  startedAuthorityNamespace: 'SENTETIK-context', actorId: ACTOR, companyId: COMPANY, labelId: LABEL, idempotencyKey: KEY,
  request: { expectedLabelVersion: '3', classification: 'SecondQuality' as const },
}

afterEach(() => sessionStorage.clear())

describe('pending label classification store', () => {
  const key = pendingLabelClassificationKey(ACTOR, COMPANY, LABEL)!
  const stored = (value: unknown) => { sessionStorage.setItem(key, JSON.stringify(value)); return recallPendingLabelClassification(key) }

  it('keeps one attempt until the same key releases it', () => {
    reservePendingLabelClassification(key, attempt)
    expect(recallPendingLabelClassification(key)).toEqual(attempt)
    releasePendingLabelClassification(key, COMPANY)
    expect(recallPendingLabelClassification(key)).toEqual(attempt)
    releasePendingLabelClassification(key, KEY)
    expect(recallPendingLabelClassification(key)).toBeNull()
  })

  it('refuses a stored attempt with an extra or a missing member', () => {
    expect(stored(attempt)).toEqual(attempt)
    expect(stored({ ...attempt, extra: 1 })).toBeNull()
    for (const name of Object.keys(attempt)) expect(stored({ ...attempt, [name]: undefined })).toBeNull()
  })

  it('refuses a stored request with an extra or a missing member', () => {
    expect(stored({ ...attempt, request: { ...attempt.request, extra: 1 } })).toBeNull()
    expect(stored({ ...attempt, request: { expectedLabelVersion: '3' } })).toBeNull()
    expect(stored({ ...attempt, request: { classification: 'SecondQuality' } })).toBeNull()
  })
})
