/**
 * SAT-D17 / SAT-D115 (LC-3) / SAT-D216 / SAT-D222 on S-04: "Satırı eksik kapat" is offered only behind the
 * `sales.orders.lines.close` control and only for the lines the server answers as under-delivered on an Active order; the
 * confirmation shows the server's remainder and tolerance; the command names the draft version, the line and the shipped total
 * shown; a recorded closure re-reads the order, whose status then marks the line "eksik kapatıldı"; the server's refusals
 * (block band, shipper unverified, self closure, open commitments, total changed, revision awaiting customer acceptance) are
 * said in Turkish; an unknown outcome is retried with the same key; a 200 whose replay headers disagree with the body is not
 * success. Component evidence, NOT_ENV: the API answers are the harness's (SENTETIK data).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import statusResponseRaw from './examples/status-response.json?raw'
import { SALES_LINE_CLOSE_CAPABILITY } from './capabilities'
import { lineClosureNotice, isLineClosable, type LineClosureAttempt } from './lineClosure'
import type { LineRealization } from './orderStatus'
import { LINE_CLOSURE_TEXT as T, ORDER_STATUS_TEXT, PROBLEM_CODE_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const WITH_CLOSE = [...ALL_SALES, SALES_LINE_CLOSE_CAPABILITY]
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

/** SENTETIK — line 1 (1000) shipped 980: open and short (`Partial`, no band yet), or closed short once the version moved. */
function status(closed = false, version = '4'): Body {
  const body = JSON.parse(statusResponseRaw) as Body
  body.draftVersion = version
  Object.assign(body.realization, { state: closed ? 'Completed' : 'Partial', warning: false })
  Object.assign(body.realization.lines[0], closed
    ? { shipped: '980', delivered: '980', direction: 'Under', deviation: '20', band: 'Approval', warning: false, state: 'Completed', closedRemainder: '20' }
    : { shipped: '980', delivered: '0', direction: 'Under', deviation: '20', band: null, warning: false, state: 'Partial', closedRemainder: null })
  return body
}

const mutation = (version = '5', isReplay = false, originalCorrelationId = 'corr-1') =>
  ({ detail: detail(version), isReplay, originalCorrelationId })

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

const closures = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/line-closure') && call.method === 'POST')

async function openAndConfirm() {
  fireEvent.click(await screen.findByTestId('sales-line-closure-open-1'))
  fireEvent.click(screen.getByTestId('sales-line-closure-confirm'))
}

const notice = () => screen.getByTestId('sales-line-closure-notice')

describe('S-04 line closure (SAT-D17, SAT-D115, SAT-D216)', () => {
  it('closes the open short line at the shown total and the re-read status marks it closed short', async () => {
    let closed = false
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(closed ? detail('5') : detail()),
      onStatus: () => json(closed ? status(true, '5') : status()),
      onLineClosure: () => {
        closed = true
        return json(mutation())
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-line-closure')
    const row = within(section).getByTestId('sales-line-closure-line-1')
    expect(row.textContent).toContain('1000')
    expect(row.textContent).toContain('980')
    expect(row.textContent).toContain('20')
    fireEvent.click(within(section).getByRole('button', { name: T.closeLabel(1) }))

    // The confirmation: the server's facts and the tolerance profile of the status read — information only.
    const dialog = screen.getByRole('dialog', { name: T.confirmHeading(1) })
    expect(within(dialog).getByTestId('sales-line-closure-shipped').textContent).toBe('980')
    expect(within(dialog).getByTestId('sales-line-closure-remainder').textContent).toBe('20')
    expect(within(dialog).getByTestId('sales-line-closure-band').textContent).toBe(T.bandNotMeasured)
    expect(within(dialog).getByTestId('sales-line-closure-tolerance').textContent).toBe(T.toleranceBand('1', '2', '3'))
    fireEvent.click(within(dialog).getByTestId('sales-line-closure-confirm'))

    await waitFor(() => expect(closures(harness.calls)).toHaveLength(1))
    const [sent] = closures(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    // No reason member (SAT-D215): exactly the strict body.
    expect(JSON.parse(sent.body as string)).toEqual({ expectedVersion: '4', lineId: LINE, shipped: '980' })

    await waitFor(() => expect(screen.getByTestId('sales-status-line-1-state-closed-short').textContent)
      .toContain(ORDER_STATUS_TEXT.lineClosedMark))
    expect(screen.getByTestId('sales-status-line-1').getAttribute('data-closed-short')).toBe('true')
    expect(screen.getByTestId('sales-status-line-1-closed-remainder').textContent).toBe('20')
    expect(screen.getByTestId('sales-status-note-line-closed').textContent).toBe(ORDER_STATUS_TEXT.lineClosedNote)
    // The notice survives the remount the moved version causes; no line is offered any more.
    expect(notice().textContent).toBe(T.completed)
    expect(screen.queryByTestId('sales-line-closure-open-1')).toBeNull()
    expect(screen.queryByTestId('sales-line-closure-dialog')).toBeNull()
  })

  it('offers nothing without the control, and nothing when no line is short', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()), onStatus: () => json(status()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-status-line-1')
    expect(screen.queryByTestId('sales-line-closure')).toBeNull()
    cleanup()

    // The example status: line 1 over-delivered in the warn band — not closable short.
    installFetch({ capabilities: WITH_CLOSE, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-status-line-1')
    expect(screen.queryByTestId('sales-line-closure')).toBeNull()
    cleanup()

    // Already closed short: marked in the status table, not offered again.
    installFetch({ capabilities: WITH_CLOSE, onDetail: () => json(detail()), onStatus: () => json(status(true)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-status-line-1-state-closed-short')
    expect(screen.queryByTestId('sales-line-closure')).toBeNull()
  })

  it('cancels the confirmation without sending', async () => {
    const harness = installFetch({ capabilities: WITH_CLOSE, onDetail: () => json(detail()), onStatus: () => json(status()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    fireEvent.click(await screen.findByTestId('sales-line-closure-open-1'))
    fireEvent.click(screen.getByTestId('sales-line-closure-cancel'))
    expect(screen.queryByTestId('sales-line-closure-dialog')).toBeNull()
    expect(closures(harness.calls)).toHaveLength(0)
  })

  it.each([
    ['sales_line_closure_shipper_unverified', 'shippedBy'],
    ['sales_line_closure_self_closure', 'actor'],
    ['sales_line_closure_open_commitments', 'lineId'],
    ['sales_line_closure_shipped_changed', 'shipped'],
    ['sales_line_closure_not_applicable', 'lineId'],
  ])('says the recorded refusal %s in Turkish and reads the order again', async (code, field) => {
    let statusReads = 0
    installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => {
        statusReads += 1
        return json(status())
      },
      onLineClosure: () => json({ status: 409, code, errors: [{ field, code }] }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await openAndConfirm()
    await waitFor(() => expect(notice().textContent)
      .toBe(`${T.conflict} ${PROBLEM_CODE_TEXT[code as keyof typeof PROBLEM_CODE_TEXT]}`))
    expect(notice().getAttribute('role')).toBe('alert')
    await waitFor(() => expect(statusReads).toBeGreaterThan(1))
  })

  it('explains the state conflict (revision awaiting customer acceptance) and the version conflict', async () => {
    let answer = 'sales_state_conflict'
    installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onLineClosure: () => json({ status: 409, code: answer, errors: [] }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await openAndConfirm()
    await waitFor(() => expect(notice().textContent).toBe(`${T.conflict} ${T.stateConflict}`))
    expect(T.stateConflict).toContain('müşteri')

    // A definite refusal closes the attempt: another try mints a new attempt.
    answer = 'version_conflict'
    await openAndConfirm()
    await waitFor(() => expect(notice().textContent).toBe(`${T.conflict} ${T.versionConflict}`))
  })

  it('says the block band without retrying', async () => {
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onLineClosure: () => json({
        status: 422, code: 'sales_line_closure_blocked', errors: [{ field: 'lineId', code: 'sales_line_closure_blocked' }],
      }, 422),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await openAndConfirm()
    await waitFor(() => expect(notice().textContent).toBe(`${T.validation} ${PROBLEM_CODE_TEXT.sales_line_closure_blocked}`))
    expect(screen.queryByTestId('sales-line-closure-retry')).toBeNull()
    expect(closures(harness.calls)).toHaveLength(1)
  })

  it('keeps the same key after a 503 and shows the replayed answer whose headers agree', async () => {
    let attempts = 0
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onLineClosure: () => {
        attempts += 1
        return attempts === 1
          ? json({ status: 503 }, 503)
          : json(mutation('5', true, 'corr-1'), 200, { [REPLAYED]: 'true', [ORIGINAL]: 'corr-1' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await openAndConfirm()
    await waitFor(() => expect(notice().textContent).toBe(T.unknownServiceUnavailable))
    expect(screen.getByTestId('sales-line-closure-open-1')).toHaveProperty('disabled', true)
    expect(screen.getByTestId('sales-line-closure-confirm')).toHaveProperty('disabled', true)
    fireEvent.click(screen.getByTestId('sales-line-closure-retry'))
    await waitFor(() => expect(closures(harness.calls)).toHaveLength(2))
    const [first, second] = closures(harness.calls)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toBe(first.body)
    await waitFor(() => expect(notice().textContent).toBe(T.replayed))
  })

  it('says offline and network failures apart and retries them with the same key', async () => {
    let attempts = 0
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onLineClosure: () => {
        attempts += 1
        if (attempts === 1) {
          vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
          return Promise.reject(new TypeError('offline'))
        }
        vi.restoreAllMocks()
        return Promise.reject(new TypeError('lost'))
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await openAndConfirm()
    await waitFor(() => expect(notice().textContent).toBe(T.unknownOffline))
    fireEvent.click(screen.getByTestId('sales-line-closure-retry'))
    // The second send answers nothing either: the first outcome is still the one reported.
    await waitFor(() => expect(closures(harness.calls)).toHaveLength(2))
    await waitFor(() => expect(notice().textContent).toBe(T.unknownOffline))
    const [first, second] = closures(harness.calls)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
  })

  it('reports a network failure on a first send', async () => {
    installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onLineClosure: () => Promise.reject(new TypeError('lost')),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openAndConfirm()
    await waitFor(() => expect(notice().textContent).toBe(T.unknownNetwork))
    fireEvent.click(screen.getByRole('button', { name: T.abandon }))
    expect(notice().textContent).toBe(T.abandoned)
  })

  it.each([
    ['a body without its replay flag', () => json({ detail: detail('5'), originalCorrelationId: 'corr-1' })],
    ['a replay body without the replay header', () => json(mutation('5', true))],
    ['a replay header beside a first answer', () => json(mutation('5', false), 200, { [REPLAYED]: 'true', [ORIGINAL]: 'corr-1' })],
    ['a replay whose original correlation disagrees', () => json(mutation('5', true, 'corr-1'), 200, { [REPLAYED]: 'true', [ORIGINAL]: 'corr-2' })],
    ['a first answer that names an original correlation', () => json(mutation('5', false), 200, { [ORIGINAL]: 'corr-1' })],
    ['a detail of another draft', () => {
      const body = mutation()
      body.detail.draft.id = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
      return json(body)
    }],
    ['a 204 instead of the pinned 200', () => new Response(null, { status: 204 })],
  ])('treats %s as an unknown outcome and keeps the key', async (_name, answer) => {
    let attempts = 0
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onLineClosure: () => {
        attempts += 1
        return attempts === 1 ? answer() : json(mutation())
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await openAndConfirm()
    await waitFor(() => expect(notice().textContent).toBe(T.unknownMalformed))
    fireEvent.click(screen.getByTestId('sales-line-closure-retry'))
    await waitFor(() => expect(notice().textContent).toBe(T.completed))
    const [first, second] = closures(harness.calls)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
  })

  it('on a 403 hands the refusal to the real auth boundary: the protected order closes and no closure is left', async () => {
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onLineClosure: () => json({ status: 403, code: 'forbidden' }, 403),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openAndConfirm()
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-line-closure')).toBeNull()
    expect(closures(harness.calls)).toHaveLength(1)
  })
})

describe('line closure rules and notices', () => {
  const line = (overrides: Partial<LineRealization>): LineRealization => ({
    lineId: LINE, lineNo: 1, orderUnitId: '44444444-4444-4444-4444-444444444444', ordered: '1000', shipped: '980', delivered: '0',
    state: 'Partial', direction: 'Under', deviation: '20', band: null, warning: false, approvedOverDelivery: false,
    closedRemainder: null, approvedReturned: '0', ...overrides,
  })

  it('offers only measured under-delivered lines that are not closed yet', () => {
    expect(isLineClosable(line({}))).toBe(true)
    expect(isLineClosable(line({ state: 'Completed', band: 'Within' }))).toBe(true)
    expect(isLineClosable(line({ state: 'NotStarted', shipped: '0', deviation: '1000' }))).toBe(false)
    expect(isLineClosable(line({ shipped: null }))).toBe(false)
    expect(isLineClosable(line({ closedRemainder: '20', state: 'Completed' }))).toBe(false)
    expect(isLineClosable(line({ direction: 'Over' }))).toBe(false)
    expect(isLineClosable(line({ state: 'PendingReapproval' }))).toBe(false)
  })

  const snapshot = { key: 'k', path: 'p', binding: 'b', companyId: COMPANY_A, payload: { expectedVersion: '4', lineId: LINE, shipped: '980' } }

  it.each([
    ['unauthenticated', T.unauthenticated],
    ['forbidden', T.forbidden],
    ['not-found', T.notFound],
    ['unexpected', T.unexpected],
  ] as const)('says the %s refusal in its own words', (failure, text) => {
    const attempt: LineClosureAttempt = { phase: 'failed', snapshot, failure, problem: null }
    expect(lineClosureNotice(attempt)).toBe(text)
  })

  it('keeps saying unknown when a refusal answered a retry of an unknown outcome', () => {
    const attempt: LineClosureAttempt = {
      phase: 'failed', snapshot, failure: 'conflict', problem: { status: 409, code: 'same_key_payload_mismatch' } as never,
      unresolvedSince: 'network-error',
    }
    expect(lineClosureNotice(attempt)).toContain(T.unknown)
  })
})
