/**
 * ORG-05 (KIM-S23) component cases — the company VKN card and its maker-checker change flow.
 *
 * COMPONENT/DOM EVIDENCE ONLY. The harness stubs `fetch` with synthetic fixtures, so nothing here is evidence of
 * real API, real SQL or browser acceptance. What these cases establish is what the screen renders from the server's
 * viewer statement, what it sends (body, key, company) and what it does after each kind of answer.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, installFetch, json, noContent, problem, renderAt, type RecordedRequest } from './testHarness'
import { ORGANIZATION_CAPABILITY, ORGANIZATION_ROUTE } from './capabilities'
import { COMPANY_TAX_ID_PROBLEM } from './companyTaxIdContract'
import { COMPANY_TAX_ID_TEXT } from './companyTaxIdText'

const PATH = ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.companyTaxId]
const CAPABILITIES = [ORGANIZATION_CAPABILITY.companyTaxId]
const REQUEST = 'cccccccc-0000-0000-0000-000000000003'
const VIEW_RE = /\/organization\/company\/tax-id$/
const CREATE_RE = /\/organization\/company\/tax-id\/change-requests$/
const DECIDE_RE = /\/organization\/company\/tax-id\/change-requests\/[^/]+\/decision$/

afterEach(() => {
  vi.unstubAllGlobals()
})

interface Viewer {
  readonly canRequest: boolean
  readonly canApprove: boolean
  readonly isRequesterOfPending: boolean
}

function pending(overrides: Record<string, unknown> = {}) {
  return {
    id: REQUEST,
    currentVkn: '1234567890',
    proposedVkn: '0987654321',
    reason: 'Vergi dairesi yazısı',
    status: 'pending',
    requestedBy: 'Hazırlayan A',
    requestedAtUtc: '2026-11-14T08:00:00Z',
    decidedBy: null,
    decidedAtUtc: null,
    decisionNote: null,
    ...overrides,
  }
}

function view(viewer: Viewer, overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY_A,
    code: 'GDC',
    legalName: 'Göymen Demir Çelik',
    vkn: '1234567890',
    isPlaceholder: false,
    version: 3,
    pending: null,
    lastDecided: null,
    viewer,
    ...overrides,
  }
}

const MAKER: Viewer = { canRequest: true, canApprove: false, isRequesterOfPending: false }
const CHECKER: Viewer = { canRequest: false, canApprove: true, isRequesterOfPending: false }
const BOTH_AS_PREPARER: Viewer = { canRequest: true, canApprove: true, isRequesterOfPending: true }

const reads = (recorded: readonly RecordedRequest[]) =>
  recorded.filter((request) => request.method === 'GET' && VIEW_RE.test(request.url))
const writes = (recorded: readonly RecordedRequest[]) => recorded.filter((request) => request.method === 'POST')

describe('CompanyTaxIdPage', () => {
  it('shows the active company’s VKN and nothing to decide when nothing is pending', async () => {
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url) => (VIEW_RE.test(url) ? json(view(MAKER)) : undefined),
    })
    renderAt(PATH)

    expect(await screen.findByText('1234567890')).toBeTruthy()
    expect(screen.getByText('GDC')).toBeTruthy()
    expect(screen.getByRole('button', { name: COMPANY_TAX_ID_TEXT.openRequest })).toBeTruthy()
    expect(screen.queryByRole('button', { name: COMPANY_TAX_ID_TEXT.approve })).toBeNull()
    const [read] = reads(recorded)
    expect(read.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(writes(recorded)).toHaveLength(0)
  })

  it('renders the server’s 403 as the forbidden block, never an empty card', async () => {
    installFetch({
      capabilities: CAPABILITIES,
      respond: (url) => (VIEW_RE.test(url) ? problem(403) : undefined),
    })
    renderAt(PATH)

    expect(await screen.findByText('Yetkiniz yok')).toBeTruthy()
    expect(screen.queryByText('1234567890')).toBeNull()
  })

  it('treats an answer about another company as malformed', async () => {
    installFetch({
      capabilities: CAPABILITIES,
      respond: (url) =>
        VIEW_RE.test(url) ? json(view(MAKER, { companyId: 'bbbbbbbb-0000-0000-0000-000000000002' })) : undefined,
    })
    renderAt(PATH)

    expect(await screen.findByText('Beklenmeyen yanıt')).toBeTruthy()
  })

  it('does not read without the capability', async () => {
    const recorded = installFetch({ capabilities: [ORGANIZATION_CAPABILITY.settingsList], respond: () => undefined })
    renderAt(PATH)

    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    expect(reads(recorded)).toHaveLength(0)
  })

  it('refuses a malformed VKN locally without minting a request', async () => {
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url) => (VIEW_RE.test(url) ? json(view(MAKER)) : undefined),
    })
    renderAt(PATH)

    await userEvent.click(await screen.findByRole('button', { name: COMPANY_TAX_ID_TEXT.openRequest }))
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.proposedVkn), '12345')
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.reason), 'Gerekçe')
    await userEvent.click(screen.getByRole('button', { name: COMPANY_TAX_ID_TEXT.submitRequest }))

    expect(await screen.findByText(COMPANY_TAX_ID_TEXT.vknInvalid)).toBeTruthy()
    expect(writes(recorded)).toHaveLength(0)
  })

  it('sends the request with the read version and a key, then re-reads the view', async () => {
    let created = false
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (CREATE_RE.test(url) && init?.method === 'POST') {
          created = true
          return json({ id: REQUEST, version: 1 }, 201)
        }
        if (VIEW_RE.test(url)) {
          return json(view(MAKER, created ? { pending: pending(), version: 3 } : {}))
        }
        return undefined
      },
    })
    renderAt(PATH)

    await userEvent.click(await screen.findByRole('button', { name: COMPANY_TAX_ID_TEXT.openRequest }))
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.proposedVkn), '0987654321')
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.reason), 'Vergi dairesi yazısı')
    await userEvent.click(screen.getByRole('button', { name: COMPANY_TAX_ID_TEXT.submitRequest }))

    expect(await screen.findByText(COMPANY_TAX_ID_TEXT.requestedTitle)).toBeTruthy()
    const [write] = writes(recorded)
    expect(write.body).toEqual({ expectedVersion: 3, proposedVkn: '0987654321', reason: 'Vergi dairesi yazısı' })
    expect(write.headers['Idempotency-Key']).toBeTruthy()
    expect(write.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(reads(recorded).length).toBeGreaterThanOrEqual(2)
    // The maker holds no approve name: the pending request waits for someone else.
    expect(await screen.findByText(COMPANY_TAX_ID_TEXT.awaitingApprover)).toBeTruthy()
    expect(screen.getByText(COMPANY_TAX_ID_TEXT.requestNotAllowedWhilePending)).toBeTruthy()
  })

  it('says the VKN is used by another company when the server refuses with 422', async () => {
    installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (CREATE_RE.test(url) && init?.method === 'POST') {
          return json({ code: COMPANY_TAX_ID_PROBLEM.inUse, status: 422 }, 422)
        }
        return VIEW_RE.test(url) ? json(view(MAKER)) : undefined
      },
    })
    renderAt(PATH)

    await userEvent.click(await screen.findByRole('button', { name: COMPANY_TAX_ID_TEXT.openRequest }))
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.proposedVkn), '0987654321')
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.reason), 'Gerekçe')
    await userEvent.click(screen.getByRole('button', { name: COMPANY_TAX_ID_TEXT.submitRequest }))

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('VKN başka bir şirkette kayıtlı')).toBeTruthy()
  })

  it('keeps the key and the command after an unknown outcome and resends them unchanged', async () => {
    let attempts = 0
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (CREATE_RE.test(url) && init?.method === 'POST') {
          attempts += 1
          return attempts === 1
            ? json({}, 503)
            : json({ id: REQUEST, version: 1 }, 201, { 'Idempotency-Replayed': 'true' })
        }
        return VIEW_RE.test(url) ? json(view(MAKER, attempts > 1 ? { pending: pending() } : {})) : undefined
      },
    })
    renderAt(PATH)

    await userEvent.click(await screen.findByRole('button', { name: COMPANY_TAX_ID_TEXT.openRequest }))
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.proposedVkn), '0987654321')
    await userEvent.type(screen.getByLabelText(COMPANY_TAX_ID_TEXT.reason), 'Gerekçe')
    await userEvent.click(screen.getByRole('button', { name: COMPANY_TAX_ID_TEXT.submitRequest }))

    const resend = await screen.findByRole('button', { name: COMPANY_TAX_ID_TEXT.resend })
    // Frozen: no new submit while the first outcome is unknown.
    expect(screen.queryByRole('button', { name: COMPANY_TAX_ID_TEXT.submitRequest })).toBeNull()
    await userEvent.click(resend)

    expect(await screen.findByText(COMPANY_TAX_ID_TEXT.replayedTitle)).toBeTruthy()
    const [first, second] = writes(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('never offers approve or reject to the preparer of the pending request', async () => {
    installFetch({
      capabilities: CAPABILITIES,
      respond: (url) => (VIEW_RE.test(url) ? json(view(BOTH_AS_PREPARER, { pending: pending() })) : undefined),
    })
    renderAt(PATH)

    expect(await screen.findByText(COMPANY_TAX_ID_TEXT.ownRequest)).toBeTruthy()
    expect(screen.queryByRole('button', { name: COMPANY_TAX_ID_TEXT.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: COMPANY_TAX_ID_TEXT.reject })).toBeNull()
  })

  it('lets another holder approve with the read version and shows the decided request afterwards', async () => {
    let decided = false
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (DECIDE_RE.test(url) && init?.method === 'POST') {
          decided = true
          return noContent()
        }
        if (VIEW_RE.test(url)) {
          return json(
            decided
              ? view(CHECKER, {
                  vkn: '0987654321',
                  version: 4,
                  lastDecided: pending({
                    status: 'approved',
                    decidedBy: 'Onaylayan B',
                    decidedAtUtc: '2026-11-14T09:00:00Z',
                    decisionNote: 'Uygun',
                  }),
                })
              : view(CHECKER, { pending: pending() }),
          )
        }
        return undefined
      },
    })
    renderAt(PATH)

    await userEvent.type(await screen.findByLabelText(COMPANY_TAX_ID_TEXT.note), 'Uygun')
    await userEvent.click(screen.getByRole('button', { name: COMPANY_TAX_ID_TEXT.approve }))

    expect(await screen.findByText(COMPANY_TAX_ID_TEXT.decidedTitle)).toBeTruthy()
    const [write] = writes(recorded)
    expect(write.url).toMatch(new RegExp(`/change-requests/${REQUEST}/decision$`))
    expect(write.body).toEqual({ expectedVersion: 3, decision: 'approve', note: 'Uygun' })
    expect(await screen.findByText(COMPANY_TAX_ID_TEXT.lastDecidedHeading)).toBeTruthy()
    expect(screen.getByText('Onaylandı')).toBeTruthy()
  })

  it('explains a stale request refusal and offers a reload', async () => {
    installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (DECIDE_RE.test(url) && init?.method === 'POST') {
          return json({ code: COMPANY_TAX_ID_PROBLEM.changeStale, status: 409 }, 409)
        }
        return VIEW_RE.test(url) ? json(view(CHECKER, { pending: pending() })) : undefined
      },
    })
    renderAt(PATH)

    await userEvent.click(await screen.findByRole('button', { name: COMPANY_TAX_ID_TEXT.approve }))

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('Şirket kaydı talepten sonra değişti')).toBeTruthy()
    expect(within(alert).getByRole('button', { name: COMPANY_TAX_ID_TEXT.reload })).toBeTruthy()
  })
})
