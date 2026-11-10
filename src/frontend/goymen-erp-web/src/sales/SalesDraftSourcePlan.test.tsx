/**
 * API-11 (SAT-D251..SAT-D255): "Kaynak planı" on the order detail. The read is shown in every state; the server's figures
 * (locked part, stock from the allocation, suggestion "kullanılamıyor", warnings) are shown as given; only writable lines take
 * input and only behind `sales.orders.source-plan.write`; an open linked document asks for a confirmation first and the save
 * then names `confirmLinkedSupply: true`; an unreadable link blocks the save; an unknown outcome is retried with the same key;
 * a 200 whose replay headers disagree with the body is not success; a 409 re-reads the order and the plan; a 422 says the
 * server's rule. Component evidence, NOT_ENV: the API answers are the harness's (SENTETIK data).
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_SOURCE_PLAN_WRITE_CAPABILITY } from './capabilities'
import { PROBLEM_CODE_TEXT, SOURCE_PLAN_TEXT as T } from './salesText'
import { SALES_PROBLEM_CODE } from './contract'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp, sourcePlanOf, type RecordedCall } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const WITH_PLAN = [...ALL_SALES, SALES_SOURCE_PLAN_WRITE_CAPABILITY]
const REPLAYED = 'Idempotency-Replayed'
const ORIGINAL = 'Idempotency-Original-Correlation-Id'

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

function detail(version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, approvalState: 'Approved', state: 'Draft', version }
  body.approval = APPROVED
  return body
}

/** SENTETIK — line 1 (1000 kg): 200 delivered (locked), 300 allocated (stock), open to planning unless overridden. */
const openLine = (line: Body): Body => ({ ...line, locked: '200', remaining: '800', stock: '300' })

const savedPlan = (version: string): Body => ({
  draftVersion: version, lineQuantity: '1000', locked: '200', stock: '300', production: '400', purchasing: '100',
  subcontract: '0', linkedSupplyConfirmed: false, savedBy: 'planlama-1', savedAtUtc: '2026-11-09T08:00:00Z',
})

const mutation = (version = '5', isReplay = false, originalCorrelationId = 'corr-1') =>
  ({ detail: detail(version), isReplay, originalCorrelationId })

const LINKED_PRESENT = [{ kind: 'Production', status: 'Present' }, { kind: 'Purchasing', status: 'None' }]
const LINKED_UNREADABLE = [{ kind: 'Production', status: 'None' }, { kind: 'Purchasing', status: 'Unreadable' }]

afterEach(() => {
  cleanup()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const saves = (calls: readonly RecordedCall[]) => calls.filter((call) => call.url.endsWith('/source-plan') && call.method === 'POST')
const reads = (calls: readonly RecordedCall[]) => calls.filter((call) => call.url.endsWith('/source-plan') && call.method === 'GET')
const notice = () => screen.getByTestId('sales-source-plan-notice')

function enterPlan(production: string, purchasing: string, subcontract: string) {
  fireEvent.change(screen.getByTestId('sales-source-plan-production-1'), { target: { value: production } })
  fireEvent.change(screen.getByTestId('sales-source-plan-purchasing-1'), { target: { value: purchasing } })
  fireEvent.change(screen.getByTestId('sales-source-plan-subcontract-1'), { target: { value: subcontract } })
}

/** The open plan read for whichever detail version is served (the harness re-reads after a recorded plan). */
function openPlan(linkedSupply?: unknown, recordedAt: string | null = null) {
  return () => sourcePlanOf(detail(recordedAt ?? '4'), {
    writable: true,
    linkedSupply,
    line: (line) => recordedAt === null
      ? openLine(line)
      : { ...openLine(line), saved: savedPlan(recordedAt), warnings: [] },
  })
}

describe('API-11 order source plan (SAT-D252..SAT-D255)', () => {
  it('shows the server figures and records the touched line with the exact body and a fresh key', async () => {
    let recorded = false
    const harness = installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(recorded ? detail('5') : detail()),
      onSourcePlan: () => (recorded ? openPlan(undefined, '5')() : openPlan()()),
      onSourcePlanSave: () => {
        recorded = true
        return json(mutation())
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-source-plan')
    await within(section).findByTestId('sales-source-plan-line-1')
    expect(screen.getByTestId('sales-source-plan-locked-1').textContent).toBe('200')
    expect(screen.getByTestId('sales-source-plan-stock-1').textContent).toBe('300')
    expect(screen.getByTestId('sales-source-plan-suggestion-1').textContent).toContain(T.suggestionValue.Unavailable)
    expect(screen.getByTestId('sales-source-plan-recorded-1').textContent).toBe(T.noRecorded)
    expect(screen.getByTestId('sales-source-plan-warnings-1').textContent).toContain(T.warning.NoPlan)

    enterPlan('400', '100', '0')
    fireEvent.click(screen.getByTestId('sales-source-plan-save'))

    await waitFor(() => expect(saves(harness.calls)).toHaveLength(1))
    const [sent] = saves(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    // The stock and locked parts are never sent: the server derives them (SAT-D253, SAT-D255).
    expect(JSON.parse(sent.body as string)).toEqual({
      expectedVersion: '4', confirmLinkedSupply: false, lines: [{ lineId: LINE, production: '400', purchasing: '100', subcontract: '0' }],
    })

    await waitFor(() => expect(screen.getByTestId('sales-source-plan-recorded-1').textContent)
      .toContain(T.recordedParts('300', '400', '100', '0')))
    expect(notice().textContent).toBe(T.completed)
  })

  it('is read only without the control, and shows the lock reason when the order is not open to planning', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()), onSourcePlan: openPlan() })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    expect(screen.getByTestId('sales-source-plan-read-only').textContent).toBe(T.readOnly)
    expect(screen.queryByTestId('sales-source-plan-production-1')).toBeNull()
    expect(screen.queryByTestId('sales-source-plan-save')).toBeNull()
    cleanup()

    installFetch({ capabilities: WITH_PLAN, onDetail: () => json(detail()), onSourcePlan: () => sourcePlanOf(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    expect(screen.getByTestId('sales-source-plan-order-locked').textContent).toBe(T.orderLocked)
    expect(screen.getByTestId('sales-source-plan-lock-1').textContent).toBe(T.lockReason.OrderState)
    expect(screen.queryByTestId('sales-source-plan-save')).toBeNull()
  })

  it('locks a delivered line with its reason and shows an unreadable allocation as "okunamadı"', async () => {
    installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: () => sourcePlanOf(detail(), {
        writable: true,
        line: (line) => ({
          ...line, stockStatus: 'Unreadable', stock: null, writable: false, lockReason: 'AllocationUnreadable',
          suggestionReason: 'AllocationUnreadable', warnings: ['AllocationUnreadable', 'NoPlan'],
        }),
      }),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    expect(screen.getByTestId('sales-source-plan-stock-1').textContent).toBe(T.unreadable)
    expect(screen.getByTestId('sales-source-plan-lock-1').textContent).toBe(T.lockReason.AllocationUnreadable)
    expect(screen.queryByTestId('sales-source-plan-production-1')).toBeNull()
    // No writable line: nothing to save.
    expect(screen.queryByTestId('sales-source-plan-save')).toBeNull()
  })

  it('asks to confirm an open linked document; cancel sends nothing, confirm sends confirmLinkedSupply true', async () => {
    const harness = installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: openPlan(LINKED_PRESENT),
      onSourcePlanSave: () => json(mutation()),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    expect(screen.getByTestId('sales-source-plan-linked-present').textContent).toBe(T.linkedPresent)
    enterPlan('500', '0', '0')

    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    // The dialog sits in the allocation tab panel (hidden in the DOM tests, so found by test id).
    const dialog = screen.getByTestId('sales-source-plan-confirm-dialog')
    expect(dialog.getAttribute('role')).toBe('dialog')
    expect(within(dialog).getByRole('heading', { hidden: true }).textContent).toBe(T.confirmHeading)
    fireEvent.click(within(dialog).getByTestId('sales-source-plan-cancel'))
    expect(screen.queryByTestId('sales-source-plan-confirm-dialog')).toBeNull()
    expect(saves(harness.calls)).toHaveLength(0)

    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    fireEvent.click(within(screen.getByTestId('sales-source-plan-confirm-dialog')).getByTestId('sales-source-plan-confirm'))
    await waitFor(() => expect(saves(harness.calls)).toHaveLength(1))
    expect(JSON.parse(saves(harness.calls)[0].body as string)).toEqual({
      expectedVersion: '4', confirmLinkedSupply: true, lines: [{ lineId: LINE, production: '500', purchasing: '0', subcontract: '0' }],
    })
    await waitFor(() => expect(notice().textContent).toBe(T.completed))
  })

  it('blocks the save while a linked document cannot be read', async () => {
    installFetch({ capabilities: WITH_PLAN, onDetail: () => json(detail()), onSourcePlan: openPlan(LINKED_UNREADABLE) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    expect(screen.getByTestId('sales-source-plan-linked-unreadable').textContent).toBe(T.linkedUnreadable)
    expect(screen.getByTestId('sales-source-plan-linked-Purchasing').textContent).toBe(T.linkedStatus.Unreadable)
    expect(screen.queryByTestId('sales-source-plan-production-1')).toBeNull()
    expect(screen.queryByTestId('sales-source-plan-save')).toBeNull()
  })

  it('refuses an invalid or empty entry in the form without sending', async () => {
    const harness = installFetch({ capabilities: WITH_PLAN, onDetail: () => json(detail()), onSourcePlan: openPlan() })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    expect(screen.getByTestId('sales-source-plan-form-error').textContent).toBe(T.nothingToSave)
    enterPlan('abc', '0', '0')
    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    expect(screen.getByTestId('sales-source-plan-form-error').textContent).toBe(T.invalidInput(1))
    expect(saves(harness.calls)).toHaveLength(0)
  })

  it('keeps the key of an unknown outcome and resends the same plan', async () => {
    let answers = 0
    const harness = installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: openPlan(),
      onSourcePlanSave: () => (++answers === 1 ? json({ status: 503 }, 503) : json(mutation('5', true, 'corr-0'), 200, {
        [REPLAYED]: 'true', [ORIGINAL]: 'corr-0',
      })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    enterPlan('400', '100', '0')
    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    await waitFor(() => expect(notice().textContent).toBe(T.unknownServiceUnavailable))
    // The inputs are frozen on the unknown snapshot.
    expect((screen.getByTestId('sales-source-plan-production-1') as HTMLInputElement).disabled).toBe(true)

    fireEvent.click(screen.getByTestId('sales-source-plan-retry'))
    await waitFor(() => expect(saves(harness.calls)).toHaveLength(2))
    const [first, second] = saves(harness.calls)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toBe(first.body)
    await waitFor(() => expect(notice().textContent).toBe(T.replayed))
  })

  it('treats a 200 whose replay headers disagree with the body as unknown', async () => {
    const harness = installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: openPlan(),
      onSourcePlanSave: () => json(mutation('5', true, 'corr-0')),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    enterPlan('400', '100', '0')
    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    await waitFor(() => expect(notice().textContent).toBe(T.unknownMalformed))
    expect(saves(harness.calls)).toHaveLength(1)
    expect(screen.getByTestId('sales-source-plan-retry')).toBeTruthy()
  })

  it('re-reads the order and the plan on a version conflict and says why', async () => {
    const harness = installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: openPlan(),
      onSourcePlanSave: () => json({ status: 409, code: SALES_PROBLEM_CODE.versionConflict, errors: [] }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    const readsBefore = reads(harness.calls).length
    enterPlan('400', '100', '0')
    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    await waitFor(() => expect(notice().textContent).toBe(`${T.conflict} ${T.versionConflict}`))
    expect(notice().getAttribute('role')).toBe('alert')
    await waitFor(() => expect(reads(harness.calls).length).toBeGreaterThan(readsBefore))
  })

  it('says the server sum rule on a 422 and does not re-read', async () => {
    const harness = installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: openPlan(),
      onSourcePlanSave: () => json({
        status: 422, code: SALES_PROBLEM_CODE.sourcePlanSumMismatch,
        errors: [{ field: 'lines[0]', code: SALES_PROBLEM_CODE.sourcePlanSumMismatch }],
      }, 422),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-source-plan-line-1')
    const readsBefore = reads(harness.calls).length
    enterPlan('400', '0', '0')
    fireEvent.click(screen.getByTestId('sales-source-plan-save'))
    await waitFor(() => expect(notice().textContent)
      .toBe(`${T.validation} ${PROBLEM_CODE_TEXT[SALES_PROBLEM_CODE.sourcePlanSumMismatch]}`))
    expect(reads(harness.calls).length).toBe(readsBefore)
    // The person's entry stays for a correction; a new attempt gets a new key.
    expect((screen.getByTestId('sales-source-plan-production-1') as HTMLInputElement).value).toBe('400')
  })

  it('shows the read states: loading, service unavailable with retry, and a malformed read', async () => {
    installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: () => new Promise<Response>(() => {}),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const section = await screen.findByTestId('sales-source-plan')
    await waitFor(() => expect(section.querySelector('[data-state="initial-loading"]')).not.toBeNull())
    cleanup()

    installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      onSourcePlan: () => json({ status: 503 }, 503),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const unavailable = await screen.findByTestId('sales-source-plan')
    await waitFor(() => expect(unavailable.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
    cleanup()

    installFetch({
      capabilities: WITH_PLAN,
      onDetail: () => json(detail()),
      // A plan for another version of the order is not this order's plan.
      onSourcePlan: () => sourcePlanOf(detail('3')),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const malformed = await screen.findByTestId('sales-source-plan')
    await waitFor(() => expect(malformed.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  })
})
