/**
 * FIN-S16 — M-05 Hesap Planı: the strict decoder, the transport, and the screen through the real shell gate over
 * SENTETIK fixtures. NOT real-API, real-SQL or browser acceptance.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { fetchAccountPlan } from './accountPlanApi'
import { decodeAccountPlanView, filterAccountPlan } from './accountPlanContract'
import { ACCOUNTING_CAPABILITY, ACCOUNTING_ROUTE } from './capabilities'
import { installFetch, json, problem, READER_CAPABILITIES, renderAt, requestsFor } from './testHarness'

const COMPANY = '10000000-0000-4000-8000-000000000001'
const PACKAGE = 'a8000000-0000-4000-8000-000000000001'
const CASH = 'a6000000-0000-4000-8000-000000000011'
const RECEIVABLES = 'a6000000-0000-4000-8000-000000000012'
const SHA = 'ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12'

const plan = () => ({
  source: {
    packageId: PACKAGE,
    catalogVersion: 3,
    accountPlanSourceVersion: 'SENTETIK-plan-2026',
    accountsSourceSha256: SHA,
    activatedAtUtc: '2026-11-02T09:30:00+00:00',
    accountCount: 2,
  },
  accounts: [
    { accountId: CASH, code: '100.01', name: 'SENTETIK Kasa' },
    { accountId: RECEIVABLES, code: '120.01', name: 'SENTETIK Alıcılar' },
  ],
})

const empty = () => ({ source: null, accounts: [] })

afterEach(() => vi.unstubAllGlobals())

describe('M-05 account plan decoder', () => {
  it('accepts the exact active plan and the exact no-active-plan answer', () => {
    expect(decodeAccountPlanView(plan())).toEqual(plan())
    expect(decodeAccountPlanView(empty())).toEqual(empty())
    expect(decodeAccountPlanView({ ...plan(), source: { ...plan().source, catalogVersion: null, activatedAtUtc: null } }))
      .not.toBeNull()
  })

  it('fails closed on unknown keys, wrong types and partial answers', () => {
    expect(decodeAccountPlanView(null)).toBeNull()
    expect(decodeAccountPlanView([])).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), extra: true })).toBeNull()
    expect(decodeAccountPlanView({ source: plan().source })).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), source: { ...plan().source, activatedBy: CASH } })).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), accounts: [{ ...plan().accounts[0], parentCode: '100' }, plan().accounts[1]] }))
      .toBeNull()
    expect(decodeAccountPlanView({ source: null, accounts: plan().accounts })).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), source: { ...plan().source, catalogVersion: '3' } })).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), source: { ...plan().source, catalogVersion: 0 } })).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), source: { ...plan().source, accountsSourceSha256: 'abc' } })).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), source: { ...plan().source, activatedAtUtc: '2026-11-02' } })).toBeNull()
    expect(decodeAccountPlanView({ ...plan(), source: { ...plan().source, accountPlanSourceVersion: ' ' } })).toBeNull()
  })

  it('rejects nil or malformed ids, blank codes or names, duplicates, a wrong count and rows out of code order', () => {
    const [cash, receivables] = plan().accounts
    const withAccounts = (accounts: unknown[], count = accounts.length) =>
      ({ source: { ...plan().source, accountCount: count }, accounts })
    expect(decodeAccountPlanView(withAccounts([{ ...cash, accountId: '00000000-0000-0000-0000-000000000000' }]))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([{ ...cash, accountId: 'not-a-guid' }]))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([{ ...cash, code: '' }]))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([{ ...cash, name: '   ' }]))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([cash, { ...receivables, accountId: CASH }]))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([cash, { ...receivables, code: cash.code }]))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([receivables, cash]))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([cash, receivables], 3))).toBeNull()
    expect(decodeAccountPlanView(withAccounts([], 0))).toBeNull()
  })

  it('searches the decoded list by code or name, Turkish case-insensitively, without changing it', () => {
    const accounts = decodeAccountPlanView(plan())!.accounts
    expect(filterAccountPlan(accounts, '')).toBe(accounts)
    expect(filterAccountPlan(accounts, '120').map((account) => account.accountId)).toEqual([RECEIVABLES])
    expect(filterAccountPlan(accounts, 'ALICI').map((account) => account.accountId)).toEqual([RECEIVABLES])
    expect(filterAccountPlan(accounts, 'kasa').map((account) => account.accountId)).toEqual([CASH])
    expect(filterAccountPlan(accounts, 'yok')).toEqual([])
  })
})

describe('M-05 account plan transport', () => {
  it('reads the account plan with only the company header and accepts exactly 200', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(JSON.stringify(plan()), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetch)

    const result = await fetchAccountPlan(COMPANY)

    expect(result).toEqual({ type: 'ok-single', data: plan() })
    const [url, init] = fetch.mock.calls[0]!
    expect(String(url)).toMatch(/\/accounting\/account-plan$/)
    expect(init?.method ?? 'GET').toBe('GET')
    expect(new Headers(init?.headers).get('X-Company-Id')).toBe(COMPANY)
  })

  it('keeps a non-200 status with its body and treats an undecodable 200 as malformed', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ code: 'accounting_authority_denied' }), {
      status: 403,
      headers: { 'Content-Type': 'application/problem+json' },
    })))
    expect(await fetchAccountPlan(COMPANY)).toEqual({ type: 'http', status: 403, body: { code: 'accounting_authority_denied' } })

    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...plan(), extra: 1 }), { status: 200 })))
    expect(await fetchAccountPlan(COMPANY)).toEqual({ type: 'malformed' })

    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(plan()), { status: 201 })))
    expect((await fetchAccountPlan(COMPANY)).type).toBe('http')
  })

  it('reports an aborted read as stale and a failed fetch as a network error', async () => {
    const controller = new AbortController()
    controller.abort()
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new DOMException('aborted', 'AbortError')
    }))
    expect(await fetchAccountPlan(COMPANY, controller.signal)).toEqual({ type: 'fence-stale' })

    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('failed')
    }))
    const result = await fetchAccountPlan(COMPANY)
    expect(['network-error', 'offline']).toContain(result.type)
  })
})

describe('M-05 account plan screen', () => {
  const PLAN_CAPABILITIES = [ACCOUNTING_CAPABILITY.periodsList, ACCOUNTING_CAPABILITY.accountPlan]
  const responder = (response: () => Response) => (url: string) =>
    (url.endsWith('/accounting/account-plan') ? response() : undefined)

  it('lists the approved accounts in code order with their source, issuing no write', async () => {
    const recorded = installFetch({ capabilities: PLAN_CAPABILITIES, respond: responder(() => json(plan())) })
    renderAt(ACCOUNTING_ROUTE.accountPlan)

    expect(await screen.findByText('SENTETIK Kasa')).toBeTruthy()
    const rows = screen.getAllByRole('row').slice(1)
    expect(rows.map((row) => within(row).getAllByRole('cell')[0]!.textContent)).toEqual(['100.01', '120.01'])
    expect(screen.getByText('SENTETIK-plan-2026')).toBeTruthy()
    expect(screen.getByText(SHA)).toBeTruthy()
    expect(screen.getByText('2 hesaptan 2 tanesi gösteriliyor.')).toBeTruthy()
    expect(requestsFor(recorded, '/accounting/account-plan', 'GET')).toHaveLength(1)
    expect(recorded.filter((request) => request.method !== 'GET')).toHaveLength(0)
  })

  it('filters locally, shows the filtered-empty state, and never re-reads the server', async () => {
    const recorded = installFetch({ capabilities: PLAN_CAPABILITIES, respond: responder(() => json(plan())) })
    renderAt(ACCOUNTING_ROUTE.accountPlan)
    const search = await screen.findByRole('searchbox', { name: 'Kod veya ad ara' })

    fireEvent.change(search, { target: { value: 'alıcı' } })
    expect(screen.queryByText('SENTETIK Kasa')).toBeNull()
    expect(screen.getByText('SENTETIK Alıcılar')).toBeTruthy()
    expect(screen.getByText('2 hesaptan 1 tanesi gösteriliyor.')).toBeTruthy()

    fireEvent.change(search, { target: { value: 'eşleşmeyen' } })
    expect(document.querySelector('[data-state="empty-filtered"]')).not.toBeNull()
    expect(requestsFor(recorded, '/accounting/account-plan')).toHaveLength(1)
  })

  it('shows "no active plan" for an empty answer, not an error and not a search box', async () => {
    installFetch({ capabilities: PLAN_CAPABILITIES, respond: responder(() => json(empty())) })
    renderAt(ACCOUNTING_ROUTE.accountPlan)

    expect(await screen.findByText('Etkin hesap planı yok')).toBeTruthy()
    expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull()
    expect(screen.queryByRole('searchbox')).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it.each([
    [403, 'not-found'],
    [503, 'service-unavailable'],
  ] as const)('renders a %i as the %s state with no list', async (status, kind) => {
    const recorded = installFetch({ capabilities: PLAN_CAPABILITIES, respond: responder(() => problem(status, { code: 'x' })) })
    renderAt(ACCOUNTING_ROUTE.accountPlan)

    await waitFor(() => expect(document.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    if (status === 403) {
      // The real auth boundary closes CapabilityRoute; no old read or retry control survives that generation.
      expect(requestsFor(recorded, '/accounting/account-plan', 'GET')).toHaveLength(1)
      expect(screen.queryByRole('button', { name: 'Tekrar dene' })).toBeNull()
    }
  })

  it('renders a malformed 200 as the malformed state, never a partial list', async () => {
    installFetch({
      capabilities: PLAN_CAPABILITIES,
      respond: responder(() => json({ ...plan(), accounts: [...plan().accounts].reverse() })),
    })
    renderAt(ACCOUNTING_ROUTE.accountPlan)

    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText('SENTETIK Kasa')).toBeNull()
  })

  it('stays closed and reads nothing while the server has not published the capability (staged)', async () => {
    const recorded = installFetch({ capabilities: READER_CAPABILITIES, respond: responder(() => json(plan())) })
    renderAt(ACCOUNTING_ROUTE.accountPlan)

    expect((await screen.findAllByText(/bulunamadı/i)).length).toBeGreaterThan(0)
    expect(requestsFor(recorded, '/accounting/account-plan')).toHaveLength(0)
  })
})
