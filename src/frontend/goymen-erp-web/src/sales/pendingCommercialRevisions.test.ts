import { beforeEach, describe, expect, it } from 'vitest'
import type { DraftCommercialRevisionRequest } from './contract'
import type { CommercialRevisionRow } from './commercialRevision'
import { beginSend, type OrderAttemptSnapshot, type OrderAttemptState } from './orderAttempt'
import {
  recallCommercialRevisionAttempt,
  rememberCommercialRevisionAttempt,
  resetPendingCommercialRevisions,
  type CommercialRevisionAttempt,
} from './pendingCommercialRevisions'

const payload: DraftCommercialRevisionRequest = {
  expectedVersion: '7',
  lines: [{ lineId: '11111111-1111-1111-1111-111111111111', unitPrice: '12.50', priceUnitId: '22222222-2222-2222-2222-222222222222', discountPercent: '5' }],
}
const snapshot: OrderAttemptSnapshot<DraftCommercialRevisionRequest> = {
  key: '0123456789abcdef0123456789abcdef',
  path: '33333333-3333-3333-3333-333333333333/commercial-revision',
  binding: '["actor",1,"company","scope"]',
  companyId: '44444444-4444-4444-4444-444444444444',
  payload,
}
const rows: readonly CommercialRevisionRow[] = [{
  lineId: payload.lines[0].lineId,
  lineNo: 1,
  productName: 'Sac',
  orderUnitId: payload.lines[0].priceUnitId!,
  unitPrice: '12.50',
  priceUnitId: payload.lines[0].priceUnitId!,
  discountPercent: '5',
}]

beforeEach(resetPendingCommercialRevisions)

describe('pending commercial revision', () => {
  it('reopens an in-flight attempt as unknown with the same key, payload and frozen rows', () => {
    rememberCommercialRevisionAttempt(snapshot.binding, beginSend(snapshot), rows)

    expect(recallCommercialRevisionAttempt(snapshot.binding)).toEqual({
      attempt: { phase: 'unknown', snapshot, reason: 'network-error' },
      rows,
    })
  })

  it('keeps an explicitly abandoned unknown attempt available for the same-payload retry', () => {
    const abandoned: CommercialRevisionAttempt = { phase: 'abandoned', snapshot, reason: 'service-unavailable' }
    rememberCommercialRevisionAttempt(snapshot.binding, abandoned, rows)

    expect(recallCommercialRevisionAttempt(snapshot.binding)?.attempt).toEqual(abandoned)
    expect(recallCommercialRevisionAttempt('another-binding')).toBeNull()
  })

  it('settles a definite result and does not let a late unknown outcome resurrect the key', () => {
    const refusal: OrderAttemptState<DraftCommercialRevisionRequest, never> = {
      phase: 'failed',
      snapshot,
      failure: 'conflict',
      problem: null,
    }
    rememberCommercialRevisionAttempt(snapshot.binding, refusal, rows)
    rememberCommercialRevisionAttempt(snapshot.binding, { phase: 'unknown', snapshot, reason: 'network-error' }, rows)

    expect(recallCommercialRevisionAttempt(snapshot.binding)).toBeNull()
  })

  it('does not let a late old callback overwrite a different-payload replacement key', () => {
    const oldAttempt: CommercialRevisionAttempt = { phase: 'unknown', snapshot, reason: 'network-error' }
    const replacementSnapshot: OrderAttemptSnapshot<DraftCommercialRevisionRequest> = {
      ...snapshot,
      key: 'fedcba9876543210fedcba9876543210',
      payload: {
        ...payload,
        lines: payload.lines.map((line) => ({ ...line, unitPrice: '13.00' })),
      },
    }
    const replacementAttempt: CommercialRevisionAttempt = {
      phase: 'unknown',
      snapshot: replacementSnapshot,
      reason: 'network-error',
    }
    const replacementRows = rows.map((row) => ({ ...row, unitPrice: '13.00' }))

    rememberCommercialRevisionAttempt(snapshot.binding, oldAttempt, rows)
    rememberCommercialRevisionAttempt(snapshot.binding, replacementAttempt, replacementRows)
    rememberCommercialRevisionAttempt(snapshot.binding, oldAttempt, rows)

    expect(recallCommercialRevisionAttempt(snapshot.binding)).toEqual({
      attempt: replacementAttempt,
      rows: replacementRows,
    })
  })
})
