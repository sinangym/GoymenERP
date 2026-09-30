/**
 * AT-061 / AT-062 (DEC-PRDCT-005): the live preview's timing and honesty, with fake timers and a controlled fetch.
 * - one request 400 ms after the LAST change (debounce), carrying exactly the last content;
 * - an in-flight request for an older content is aborted, and its late answer never lands;
 * - the last answer is kept with the snapshot it answered and is `current` only for that snapshot;
 * - an incomplete form sends nothing and says why; a refusal replaces "current" with its reason.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import type { SecuritySnapshot } from '../shell/authBoundary'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { decodePlanPreview, type PlanPreviewRequest } from './planPreview'
import {
  PLAN_PREVIEW_DEBOUNCE_MS,
  planPreviewInput,
  usePlanPreview,
  type PlanPreviewFormRow,
  type PlanPreviewInput,
} from './usePlanPreview'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const WORK_CENTER = 'd0000000-0000-4000-8000-000000000003'
const ROLL = 'e0000000-0000-4000-8000-000000000001'

function answer(request: PlanPreviewRequest) {
  const kg = request.inputs[0].plannedKg.replace(/\.?0+$/, '')
  return {
    calculatorVersion: 'GDC-PLAN-CALC/1',
    workCenterId: request.workCenterId,
    process: 'ColdRolling',
    plannedRollCount: request.inputs.length,
    plannedInputKg: kg,
    target: null,
    answers: [],
    rolls: request.inputs.map((input) => ({
      parentRollId: input.parentRollId,
      plannedKg: input.plannedKg.replace(/\.?0+$/, ''),
      answers: [{
        formula: 'CR-001…005',
        formulaVersion: 'GDC-CR/1 (master v3 §28.8)',
        outcome: 'Unavailable',
        values: [],
        missingParameters: ['densityKgPerCubicMetre', 'widthMm'],
        refusalCode: null,
        refusedParameter: null,
      }],
    })),
    missingParameters: ['densityKgPerCubicMetre', 'widthMm'],
    hasRefusal: false,
  }
}

interface Pending {
  readonly request: PlanPreviewRequest
  readonly signal: AbortSignal
  readonly resolve: (response: Response) => void
}

let pending: Pending[] = []
let securitySnapshot: SecuritySnapshot
const reportSecurity = vi.fn()
const authBoundary: AuthBoundary = {
  capture: () => securitySnapshot,
  report: reportSecurity,
}

function row(plannedKg: string): PlanPreviewFormRow {
  return { parentRollId: ROLL, label: 'LBL-0007', labelVersion: '7', plannedKg }
}

function input(plannedKg: string): PlanPreviewInput {
  return planPreviewInput(WORK_CENTER, [row(plannedKg)], '', '')
}

function respond(index: number, status = 200, body?: unknown) {
  const call = pending[index]
  call.resolve(new Response(JSON.stringify(body ?? answer(call.request)), {
    status,
    headers: { 'Content-Type': 'application/json' },
  }))
}

async function flush() {
  await act(async () => {
    for (let index = 0; index < 5; index += 1) await Promise.resolve()
  })
}

async function elapse(ms: number) {
  await act(async () => {
    vi.advanceTimersByTime(ms)
  })
}

beforeEach(() => {
  pending = []
  reportSecurity.mockReset()
  securitySnapshot = {
    actorId: 'bbbbbbbb-0000-0000-0000-000000000001',
    sessionEpoch: 3,
    companyId: COMPANY,
    committedContextRevision: 5,
    requestFenceGeneration: 8,
    capabilityGeneration: 'cap-11',
    namespace: `single:${COMPANY}`,
  }
  vi.useFakeTimers()
  vi.stubGlobal('fetch', vi.fn((url: string, init: RequestInit) => {
    expect(url).toMatch(/\/manufacturing\/plans\/preview$/)
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY)
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBeUndefined()
    return new Promise<Response>((resolve, reject) => {
      const signal = init.signal as AbortSignal
      signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
      pending.push({ request: JSON.parse(init.body as string) as PlanPreviewRequest, signal, resolve })
    })
  }))
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('usePlanPreview', () => {
  it('sends one request 400 ms after the last change, with the last content', async () => {
    const { result, rerender } = renderHook(({ value }) => usePlanPreview(COMPANY, value, authBoundary), {
      initialProps: { value: input('1') },
    })
    expect(PLAN_PREVIEW_DEBOUNCE_MS).toBeGreaterThanOrEqual(300)
    expect(PLAN_PREVIEW_DEBOUNCE_MS).toBeLessThanOrEqual(500)
    await elapse(200)
    rerender({ value: input('10') })
    await elapse(200)
    rerender({ value: input('100') })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS - 1)
    expect(pending).toHaveLength(0)
    expect(result.current.status.type).toBe('waiting')

    await elapse(1)
    expect(pending).toHaveLength(1)
    expect(pending[0].request.inputs[0].plannedKg).toBe('100')

    respond(0)
    await flush()
    expect(result.current.status.type).toBe('answered')
    expect(result.current.current).toBe(true)
    expect(result.current.last?.request.inputs[0].plannedKg).toBe('100')
    expect(result.current.last?.preview.missingParameters).toEqual(['densityKgPerCubicMetre', 'widthMm'])
  })

  it('re-rendering the same content does not send it again', async () => {
    const { rerender } = renderHook(({ value }) => usePlanPreview(COMPANY, value, authBoundary), {
      initialProps: { value: input('5') },
    })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    respond(0)
    await flush()
    rerender({ value: input('5') })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS * 3)
    expect(pending).toHaveLength(1)
  })

  it('aborts the in-flight request for an older content and never lets its late answer land', async () => {
    const { result, rerender } = renderHook(({ value }) => usePlanPreview(COMPANY, value, authBoundary), {
      initialProps: { value: input('10') },
    })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    expect(pending).toHaveLength(1)

    rerender({ value: input('20') })
    expect(pending[0].signal.aborted).toBe(true)
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    expect(pending).toHaveLength(2)

    respond(1)
    await flush()
    // The first, older request answers late: it was aborted and is ignored.
    respond(0)
    await flush()
    expect(result.current.last?.request.inputs[0].plannedKg).toBe('20')
    expect(result.current.current).toBe(true)
  })

  it('keeps the last answer with its snapshot and marks it not current as soon as the form changes', async () => {
    const { result, rerender } = renderHook(({ value }) => usePlanPreview(COMPANY, value, authBoundary), {
      initialProps: { value: input('10') },
    })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    respond(0)
    await flush()
    expect(result.current.current).toBe(true)

    rerender({ value: input('11') })
    expect(result.current.current).toBe(false)
    expect(result.current.last?.request.inputs[0].plannedKg).toBe('10')
    expect(result.current.status.type).toBe('waiting')

    // An incomplete form sends nothing, says why, and the old answer stays not current.
    rerender({ value: input('0') })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS * 2)
    expect(pending).toHaveLength(1)
    expect(result.current.status).toEqual({
      type: 'incomplete',
      reasons: ['LBL-0007: planlanan kg sıfırdan büyük bir sayı değil.'],
    })
    expect(result.current.current).toBe(false)
  })

  it('a refusal says why and on which field, and the older answer is not current', async () => {
    const { result, rerender } = renderHook(({ value }) => usePlanPreview(COMPANY, value, authBoundary), {
      initialProps: { value: input('10') },
    })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    respond(0)
    await flush()

    rerender({ value: input('10.0001') })
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    respond(1, 422, { code: 'manufacturing_mass_scale_exceeded', field: 'inputs.plannedKg' })
    await flush()
    expect(result.current.status).toEqual({
      type: 'refused',
      status: 422,
      code: 'manufacturing_mass_scale_exceeded',
      field: 'inputs.plannedKg',
    })
    expect(result.current.current).toBe(false)
    expect(result.current.last?.request.inputs[0].plannedKg).toBe('10')
  })

  it.each([401, 403])('reports a current %i refusal to the shell security boundary', async (status) => {
    const startedUnder = securitySnapshot
    const { result } = renderHook(() => usePlanPreview(COMPANY, input('10'), authBoundary))
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    respond(0, status, { code: status === 401 ? 'unauthenticated' : 'forbidden' })
    await flush()

    expect(reportSecurity).toHaveBeenCalledOnce()
    expect(reportSecurity).toHaveBeenCalledWith(status, startedUnder)
    expect(result.current.status).toMatchObject({ type: 'refused', status })
  })

  it('aborts an answer started under an older security context', async () => {
    const { result, rerender } = renderHook(() => usePlanPreview(COMPANY, input('10'), authBoundary))
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    expect(pending).toHaveLength(1)

    securitySnapshot = { ...securitySnapshot, committedContextRevision: 6, capabilityGeneration: 'cap-12' }
    rerender()
    expect(pending[0].signal.aborted).toBe(true)
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    expect(pending).toHaveLength(2)

    respond(1)
    await flush()
    respond(0, 401, { code: 'unauthenticated' })
    await flush()

    expect(reportSecurity).not.toHaveBeenCalled()
    expect(result.current.status.type).toBe('answered')
    expect(result.current.current).toBe(true)
  })

  it('an answer that is not the contract is a failure, never a half-shown preview', async () => {
    const { result } = renderHook(() => usePlanPreview(COMPANY, input('10'), authBoundary))
    await elapse(PLAN_PREVIEW_DEBOUNCE_MS)
    respond(0, 200, { ...answer(pending[0].request), plannedRollCount: 3 })
    await flush()
    expect(result.current.status).toEqual({ type: 'failed', reason: 'malformed' })
    expect(result.current.last).toBeNull()
  })
})

describe('planPreviewInput', () => {
  it('lists what the form can already see is missing', () => {
    expect(planPreviewInput('', [], 'InputMass', '')).toEqual({
      type: 'incomplete',
      reasons: [
        'İş merkezi seçilmedi.',
        'En az bir ana rulo eklenmedi.',
        'Hedef kullanılacaksa türü ve sıfırdan büyük kg değeri birlikte girilmelidir.',
      ],
    })
    expect(planPreviewInput(WORK_CENTER, [{ ...row('5'), labelVersion: null }], '', '')).toMatchObject({
      type: 'incomplete',
      reasons: ['LBL-0007: etiket sürümü bilinmiyor; ana ruloyu yeniden seçin.'],
    })
  })

  it('sends the plan save body without a plan id', () => {
    expect(planPreviewInput(WORK_CENTER, [row('12.5')], 'GoodOutputMass', '10')).toEqual({
      type: 'ready',
      request: {
        workCenterId: WORK_CENTER,
        inputs: [{ parentRollId: ROLL, plannedKg: '12.5', expectedLabelVersion: '7' }],
        target: { basis: 'GoodOutputMass', kg: '10' },
      },
    })
  })
})

describe('decodePlanPreview', () => {
  const request: PlanPreviewRequest = {
    workCenterId: WORK_CENTER,
    inputs: [{ parentRollId: ROLL, plannedKg: '10', expectedLabelVersion: '7' }],
    target: null,
  }

  it('accepts the contract and refuses an outcome that does not match its content', () => {
    expect(decodePlanPreview(answer(request))?.missingParameters).toEqual(['densityKgPerCubicMetre', 'widthMm'])
    const computedWithoutValue = answer(request)
    computedWithoutValue.rolls[0].answers[0].outcome = 'Computed'
    expect(decodePlanPreview(computedWithoutValue)).toBeNull()
    expect(decodePlanPreview({ ...answer(request), target: { basis: 'InputMass', kg: '5', outcome: 'Computed', targetMinusPlannedInputKg: null, missingParameters: [] } })).toBeNull()
    expect(decodePlanPreview({ ...answer(request), target: { basis: 'InputMass', kg: '5', outcome: 'Computed', targetMinusPlannedInputKg: '-5', missingParameters: [] } })?.target?.targetMinusPlannedInputKg).toBe('-5')
  })
})
