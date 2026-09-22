/**
 * SALES-A1 — the internal approval axis on S-04 and S-02 in the real shell: the facts shown as
 * stored, the commands offered only by capability and state, one idempotent attempt per command
 * with the detail re-read after a recorded transition, a replay told apart by the header, a
 * refusal kept honest, and the list's state column and server-side filter. Component evidence,
 * NOT_ENV: the API answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import listResponseRaw from './examples/list-response.json?raw'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { ORDER_DETAIL_TEXT as T, ORDER_LIST_TEXT } from './salesText'
import { ALL_SALES, ALL_SALES_WITH_APPROVE, COMPANY_A, HARNESS_USER_ID, deferred, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'

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

type Body = Record<string, any>

const PENDING = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: null,
  decidedAtUtc: null,
  rejectionReason: null,
}

function detail(state: string, approval: Body = { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }, version = '9007199254740993'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = state
  body.draft.version = version
  body.approval = approval
  return body
}

const mutation = (body: Body, isReplay = false) => ({ detail: body, isReplay, originalCorrelationId: 'corr-1' })

describe('S-04 approval facts', () => {
  it('shows the axis beside the revision and version, "not yet" for facts that did not happen, and the honest linked effects', async () => {
    installFetch({ onDetail: () => json(detail('Preparing')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByTestId('sales-draft-approval-state').textContent).toBe('Hazırlanıyor')
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
    expect(screen.getByTestId('sales-draft-version').textContent).toBe('9007199254740993')
    expect(screen.getByTestId('sales-approval-submitted-by').textContent).toBe(T.approval.notYet)
    expect(screen.getByTestId('sales-approval-decided-by').textContent).toBe(T.approval.notYet)
    expect(screen.getByTestId('sales-approval-linked-effects').textContent).toBe(T.approval.linkedEffects)
    expect(screen.queryByTestId('sales-approval-reason')).toBeNull()
  })

  it('a rejected draft shows who decided and the reason, or that none was given', async () => {
    installFetch({
      capabilities: ALL_SALES,
      onDetail: () => json(detail('Rejected', { ...PENDING, decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: null })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByTestId('sales-approval-decided-by').textContent).toBe('onay-1')
    expect(screen.getByTestId('sales-approval-reason').textContent).toBe(T.approval.noReason)
    // A rejected draft may be submitted again by whoever may create.
    expect(screen.getByRole('button', { name: T.approval.submit })).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approval.approve })).toBeNull()
  })

  it('a body whose facts do not fit its state fails closed', async () => {
    installFetch({ onDetail: () => json(detail('Approved', PENDING)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByTestId('sales-draft-approval-state')).toBeNull()
  })
})

describe('S-04 commands', () => {
  it('offers the decision only with the approve control and only while pending; submit only with create while preparing', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail('PendingApproval', PENDING)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByRole('button', { name: T.approval.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: T.approval.reject })).toBeNull()
    expect(screen.queryByRole('button', { name: T.approval.submit })).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    installFetch({ capabilities: [SALES_CAPABILITY.draftsDetail, SALES_CAPABILITY.ordersApprove], onDetail: () => json(detail('Approved', { ...PENDING, decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z' })) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByTestId('sales-approval-commands')).toBeNull()
    expect(screen.getByText(T.approval.approvedNote)).toBeTruthy()
  })

  it('submits with one key and the draft version, then re-reads the detail the server answers', async () => {
    let reads = 0
    const fetch = installFetch({
      capabilities: ALL_SALES,
      onDetail: () => json(detail(reads++ === 0 ? 'Preparing' : 'PendingApproval', reads === 1 ? undefined : PENDING, reads === 1 ? '9007199254740993' : '9007199254740994')),
      onCommand: (command, call) => {
        expect(command).toBe('submission')
        expect(JSON.parse(call.body ?? '')).toEqual({ expectedVersion: '9007199254740993' })
        return json(mutation(detail('PendingApproval', PENDING, '9007199254740994')))
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    await userEvent.click(screen.getByRole('button', { name: T.approval.submit }))

    expect(await screen.findByText(T.approval.completed)).toBeTruthy()
    await waitFor(() => expect(screen.getByTestId('sales-draft-approval-state').textContent).toBe('Onay bekliyor'))
    const command = fetch.salesCalls().find((c) => c.method === 'POST')!
    expect(command.url.endsWith(`/sales/drafts/${DRAFT}/submission`)).toBe(true)
    expect(command.companyId).toBe(COMPANY_A)
    expect(command.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(screen.getByTestId('sales-draft-version').textContent).toBe('9007199254740994')
    // The submitted draft offers no submit button any more, and no decision without the control.
    expect(screen.queryByRole('button', { name: T.approval.submit })).toBeNull()
  })

  it('rejects with the typed reason and tells a replayed answer apart by the header', async () => {
    const fetch = installFetch({
      capabilities: ALL_SALES_WITH_APPROVE,
      onDetail: () => json(detail('PendingApproval', PENDING)),
      onCommand: (command, call) => {
        expect(command).toBe('rejection')
        expect(JSON.parse(call.body ?? '')).toEqual({ expectedVersion: '9007199254740993', reason: 'Fiyat yok' })
        return json(mutation(detail('Rejected', { ...PENDING, decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: 'Fiyat yok' }, '9007199254740994'), true), 200, {
          'Idempotency-Replayed': 'true',
        })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    await userEvent.type(screen.getByLabelText(T.approval.reasonLabel), 'Fiyat yok')
    await userEvent.click(screen.getByRole('button', { name: T.approval.reject }))
    expect(await screen.findByText(T.approval.replayed)).toBeTruthy()
    expect(fetch.salesCalls().filter((c) => c.method === 'POST')).toHaveLength(1)
  })

  it('a 409 version conflict says the draft moved, never the product-version sentence, and re-reads the draft (S04-03)', async () => {
    let detailReads = 0
    const fetch = installFetch({
      capabilities: ALL_SALES_WITH_APPROVE,
      onDetail: () => {
        detailReads += 1
        return json(detail(detailReads === 1 ? 'PendingApproval' : 'Approved', detailReads === 1 ? PENDING : { ...PENDING, decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z' }))
      },
      onCommand: () => json({ status: 409, title: 'Conflict', code: 'version_conflict', errors: [{ field: 'expectedVersion', code: 'version_conflict' }] }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    await userEvent.click(screen.getByRole('button', { name: T.approval.reject }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toBe(`${T.approval.conflict} ${T.approval.versionConflict}`)
    expect(alert.textContent).not.toContain('Ürün bilgisi')
    // The conflict re-reads the draft: the screen now shows the state the API holds, and no command remains.
    expect((await screen.findAllByText('Onaylı')).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: T.approval.approve })).toBeNull()
    expect(fetch.salesCalls().filter((c) => c.method === 'POST')).toHaveLength(1)
  })

  it('a 409 state conflict is a refusal with its text and re-reads the draft; an unknown outcome keeps its key for the retry', async () => {
    const pending = deferred<Response>()
    let commands = 0
    const fetch = installFetch({
      capabilities: ALL_SALES_WITH_APPROVE,
      onDetail: () => json(detail('PendingApproval', PENDING)),
      onCommand: () => {
        commands += 1
        if (commands === 1) return json({ status: 409, title: 'Conflict', code: 'sales_state_conflict', errors: [{ field: 'approvalState', code: 'sales_state_conflict' }] }, 409)
        if (commands === 2) return json('{}', 503)
        return pending.promise
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    await userEvent.click(screen.getByRole('button', { name: T.approval.approve }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain(T.approval.conflict)
    expect(alert.textContent).toContain('şu anki onay durumunda')

    // A 503 is an unknown outcome: the buttons freeze, the retry resends the SAME key.
    await userEvent.click(screen.getByRole('button', { name: T.approval.approve }))
    expect(await screen.findByText(T.approval.unknown)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.approval.approve }) as HTMLButtonElement).disabled).toBe(true)
    await userEvent.click(screen.getByRole('button', { name: T.approval.retry }))
    pending.resolve(json(mutation(detail('Approved', { ...PENDING, decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z' }, '9007199254740994'))))
    expect(await screen.findByText(T.approval.completed)).toBeTruthy()
    const keys = fetch.salesCalls().filter((c) => c.method === 'POST').map((c) => c.idempotencyKey)
    expect(keys).toHaveLength(3)
    expect(keys[1]).toBe(keys[2])
    expect(keys[0]).not.toBe(keys[1])
  })
})

describe('S-02 approval column and filter', () => {
  it('shows each row\'s internal approval state and asks the server for a filtered list', async () => {
    const fetch = installFetch({
      onList: (url) => {
        const example = JSON.parse(listResponseRaw)
        const state = new URL(url, 'http://x').searchParams.get('approvalState')
        if (state === null) return json(example)
        example.items[0].approvalState = state
        return json(example)
      },
    })
    renderSalesApp('/satis/siparisler')
    const table = await screen.findByRole('table', { name: ORDER_LIST_TEXT.caption })
    expect(within(table).getByTestId(`sales-row-approval-${DRAFT}`).textContent).toBe('Taslak · Hazırlanıyor')

    await userEvent.selectOptions(screen.getByLabelText(ORDER_LIST_TEXT.filter), 'Approved')
    await waitFor(() => expect(screen.getByTestId(`sales-row-approval-${DRAFT}`).textContent).toBe('Taslak · Onaylı'))
    const states = fetch.salesCalls().map((c) => new URL(c.url, 'http://x').searchParams.get('approvalState'))
    expect(states).toEqual([null, 'Approved'])
  })
})

/**
 * D2 (Sinan 5749046401) — the persona fences on the screen. They are the SERVER's (403 at the API, measured there);
 * the screen mirrors them from the facts it was given: the signed-in actor against the stored submitter / creator.
 * Two-actor evidence on the live wire is s05's; this is component evidence of the mirror only.
 */
describe('D2 persona fences on S-04', () => {
  const OWN_SUBMISSION = { ...PENDING, submittedBy: HARNESS_USER_ID }
  const own = (body: Body) => {
    body.isOwnDraft = true
    return body
  }

  it('hides approve/reject for the pending draft one PREPARED (isOwnDraft), whoever submitted it; shows them for another\'s', async () => {
    // Own draft, submitted by a colleague (satis-1): the preparer still cannot decide it; the preparer may withdraw it.
    installFetch({ capabilities: ALL_SALES_WITH_APPROVE, onDetail: () => json(own(detail('PendingApproval', PENDING))) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByRole('button', { name: T.approval.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: T.approval.reject })).toBeNull()
    expect(screen.getByTestId('sales-own-decision-note').textContent).toBe(T.approval.ownDecisionNote)
    expect(screen.getByRole('button', { name: T.approval.withdraw })).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    // Another's draft that THIS actor submitted (isOwnDraft false, submittedBy = me): decided as on main — offered.
    installFetch({ capabilities: ALL_SALES_WITH_APPROVE, onDetail: () => json(detail('PendingApproval', OWN_SUBMISSION)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByRole('button', { name: T.approval.approve })).toBeTruthy()
    expect(screen.getByRole('button', { name: T.approval.reject })).toBeTruthy()
    expect(screen.queryByTestId('sales-own-decision-note')).toBeNull()
  })

  it('offers the withdrawal to the creator of an unapproved draft only, and sends it with the typed reason', async () => {
    const WITHDRAWN = {
      submittedBy: HARNESS_USER_ID,
      submittedAtUtc: '2026-09-19T09:00:00Z',
      decidedBy: HARNESS_USER_ID,
      decidedAtUtc: '2026-09-19T09:05:00Z',
      rejectionReason: 'Müşteri vazgeçti',
    }
    let reads = 0
    const fetch = installFetch({
      capabilities: ALL_SALES,
      onDetail: () =>
        json(reads++ === 0 ? own(detail('PendingApproval', OWN_SUBMISSION)) : own(detail('Withdrawn', WITHDRAWN, '9007199254740994'))),
      onCommand: (command, call) => {
        expect(command).toBe('withdrawal')
        expect(JSON.parse(call.body ?? '')).toEqual({ expectedVersion: '9007199254740993', reason: 'Müşteri vazgeçti' })
        return json(mutation(own(detail('Withdrawn', WITHDRAWN, '9007199254740994'))))
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    // Pending: no submit, no decision without the control, the creator's withdrawal offered.
    expect(screen.queryByRole('button', { name: T.approval.submit })).toBeNull()
    await userEvent.type(screen.getByLabelText(T.approval.withdrawReasonLabel), 'Müşteri vazgeçti')
    await userEvent.click(screen.getByRole('button', { name: T.approval.withdraw }))

    expect(await screen.findByText(T.approval.completed)).toBeTruthy()
    await waitFor(() => expect(screen.getByTestId('sales-draft-approval-state').textContent).toBe('Geri çekildi'))
    const command = fetch.salesCalls().find((c) => c.method === 'POST')!
    expect(command.url.endsWith(`/sales/drafts/${DRAFT}/withdrawal`)).toBe(true)
    expect(command.companyId).toBe(COMPANY_A)
    expect(command.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    // The facts as stored, in the withdrawal's own words; nothing deleted (the revision is still shown).
    expect(screen.getByText(T.approval.withdrawnBy)).toBeTruthy()
    expect(screen.getByTestId('sales-approval-decided-by').textContent).toBe(HARNESS_USER_ID)
    expect(screen.getByText(T.approval.withdrawalReason)).toBeTruthy()
    expect(screen.getByTestId('sales-approval-reason').textContent).toBe('Müşteri vazgeçti')
    expect(screen.getByText(T.approval.withdrawnNote)).toBeTruthy()
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
    // Terminal: no command is offered on a withdrawn draft.
    expect(screen.queryByRole('button', { name: T.approval.withdraw })).toBeNull()
    expect(screen.queryByRole('button', { name: T.approval.submit })).toBeNull()
  })

  it('offers no withdrawal on an approved draft of one\'s own, nor on someone else\'s preparing draft', async () => {
    const APPROVED = { ...OWN_SUBMISSION, decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z' }
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(own(detail('Approved', APPROVED))) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByRole('button', { name: T.approval.withdraw })).toBeNull()
    expect(screen.queryByTestId('sales-approval-commands')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail('Preparing')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByRole('button', { name: T.approval.submit })).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approval.withdraw })).toBeNull()
  })

  it('offers no easy withdrawal after an approved revision returned the own draft to Preparing', async () => {
    const revised = own(detail('Preparing'))
    revised.draft.revision = 'Rev.1'
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(revised) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByRole('button', { name: T.approval.withdraw })).toBeNull()
  })
})
