import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CashExchangeBookValuePage } from './CashExchangeBookValuePage'
import { decodeBookValue, decodeBookValueWorkspace, sendBookValueAttempt, type BookValueAttempt } from './cashExchangeBookValue'
import { BOOK_VALUE_TEXT as T } from './cashExchangeBookValueText'

// SENTETIK consumer fixtures; not SQL, grants, Documents acceptance or browser evidence.
const C = '11111111-1111-4111-8111-111111111111'; const ID = '22222222-2222-4222-8222-222222222222'
const USD = '33333333-3333-4333-8333-333333333333'; const TRY = '44444444-4444-4444-8444-444444444444'
const DOC = '55555555-5555-4555-8555-555555555555'; const PROPOSAL = '66666666-6666-4666-8666-666666666666'
const ACCOUNT = '77777777-7777-4777-8777-777777777777'; const OTHER = '88888888-8888-4888-8888-888888888888'
const profile = { profileId: OTHER, version: '1' }; const date = '2026-11-18'; const actor = 'SENTETIK-maker'
const sourceMoney = { amount: '100', currencyId: USD }; const targetMoney = { amount: '3300', currencyId: TRY }
const prep = { kind: 'CurrencyExchange', sourceKind: 'Bank', sourceAccountId: ACCOUNT, sourceMoney, sourceProfile: profile,
  targetKind: 'Bank', targetAccountId: OTHER, targetMoney, targetProfile: profile, rate: '33.00', occurredOn: date, valueOn: null, reference: 'SENTETIK FX belge', description: null }
const transfer = { id: ID, companyId: C, preparation: prep, recordedBy: 'SENTETIK-treasurer', recordedAtUtc: `${date}T08:00:00Z`, transferConfirmed: true, version: '1',
  exchange: { companyId: C, occurredOn: date, sourceMoney, targetMoney, sourceProfile: profile, targetProfile: profile,
    transactionRate: '33.00', comparisonRate: '34.00', rateDeviation: '-1.00', exactTransactionRate: { numerator: '33', denominator: '1' }, exactComparisonRate: { numerator: '34', denominator: '1' },
    sourceComparison: { currencyId: USD, sellingRate: '34', rateDay: date, source: 'Tcmb', recordId: OTHER, recordVersion: '1', bulletinDate: date, provenance: 'SENTETIK' },
    targetComparison: { currencyId: TRY, sellingRate: '1', rateDay: date, source: 'FunctionalCurrency', recordId: null, recordVersion: null, bulletinDate: null, provenance: null },
    sourceReferenceTl: { currencyId: TRY, amount: '3400', sourceVersion: 'finance.cash-transfer.tl-reference.v1' }, targetReferenceTl: { currencyId: TRY, amount: '3300', sourceVersion: 'finance.cash-transfer.tl-reference.v1' } } }
const evidence = { documentId: DOC, versionNo: 2, sha256: 'a'.repeat(64) }
const value = { id: PROPOSAL, companyId: C, transferId: ID, transferVersion: '1', transferSourceHash: 'b'.repeat(64), sourceBookValueTl: '3500.00', evidence,
  reason: 'SENTETIK gerçek defter belgesi', preparedBy: actor, preparedAtUtc: `${date}T08:01:00Z`, status: 'Pending', version: '1', decidedBy: null, decidedAtUtc: null, decisionReason: null,
  targetFunctionalValueTl: '3300.00' }
const attempt: BookValueAttempt = { key: 'SENTETIK-fixed-key', decision: false, body: { transferId: ID, expectedTransferVersion: '1', sourceBookValueTl: value.sourceBookValueTl, evidence, reason: value.reason, targetFunctionalValueTl: '3300.00' } }
const json = (x: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(x), { status, headers: { 'Content-Type': 'application/json', ...headers } })
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 0, sessionEpoch: 1 }) }))
vi.mock('../shell/shellState', async original => ({ ...await original<typeof import('../shell/shellState')>(), contextKey: () => ['SENTETIK', 'single', C, 'g', '0'] }))
vi.mock('../documents/DocumentsPanel', () => ({ DocumentsPanel: () => <p>SENTETIK vault consumer</p> }))

afterEach(() => { cleanup(); vi.unstubAllGlobals() })
beforeEach(() => { boundary.report.mockClear() })

describe('FIN14 typed book-value proof', () => {
  it('keeps actual book value separate from the benchmark and rejects foreign, self-approved or malformed proof', () => {
    expect(decodeBookValueWorkspace({ companyId: C, transfer, proposals: [value] }, C, ID)?.proposals[0].sourceBookValueTl).toBe('3500.00')
    expect(decodeBookValue({ ...value, companyId: OTHER }, C)).toBeNull()
    expect(decodeBookValue({ ...value, status: 'Approved', version: '2', decidedBy: actor, decidedAtUtc: `${date}T09:00:00Z` }, C)).toBeNull()
    expect(decodeBookValue({ ...value, sourceBookValueTl: 3500 }, C)).toBeNull()
    expect(decodeBookValue({ ...value, evidence: { ...evidence, sha256: 'missing' } }, C)).toBeNull()
    expect(decodeBookValue({ ...value, targetFunctionalValueTl: 3300 }, C)).toBeNull()
    expect(decodeBookValue({ ...value, targetFunctionalValueTl: '0' }, C)).toBeNull()
    const legacy = Object.fromEntries(Object.entries(value).filter(([key]) => key !== 'targetFunctionalValueTl'))
    expect(decodeBookValue(legacy, C)?.targetFunctionalValueTl).toBeNull()
  })

  it('refuses a wrong actor or mismatched replay header and never displays arbitrary 503 body text', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(json({ outcome: 'Succeeded', resource: { ...value, preparedBy: 'SENTETIK-other' }, isReplay: false, originalCorrelationId: 'SENTETIK-corr', code: null }))))
    expect((await sendBookValueAttempt(C, actor, attempt)).kind).toBe('unknown')
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(json({ outcome: 'Succeeded', resource: { ...value, targetFunctionalValueTl: '3400.00' }, isReplay: false, originalCorrelationId: 'SENTETIK-corr', code: null }))))
    expect((await sendBookValueAttempt(C, actor, attempt)).kind).toBe('unknown')
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(json({ outcome: 'Succeeded', resource: value, isReplay: true, originalCorrelationId: 'SENTETIK-corr', code: null }))))
    expect((await sendBookValueAttempt(C, actor, attempt)).kind).toBe('unknown')
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(json({ detail: 'SENTETIK private failure' }, 503))))
    expect(await sendBookValueAttempt(C, actor, attempt)).toEqual({ kind: 'unknown' })
  })
})

function show(proposals: unknown[] = [], canPrepare = true, canApprove = true, currentActor = actor, pinnedHash = evidence.sha256) {
  const posts: RequestInit[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    if (init.method === 'POST') { posts.push(init); return posts.length === 1 ? json({}, 503)
      : json({ outcome: 'Succeeded', resource: value, isReplay: true, originalCorrelationId: 'SENTETIK-corr', code: null }, 200, { 'Idempotency-Replayed': 'true' }) }
    if (url.includes(`/documents/${DOC}/versions?`)) return json({ documentId: DOC, versions: [{ id: DOC, companyId: C,
      ownerKind: 'finance.cash-exchange', ownerId: ID, versionNo: 2, fileName: 'SENTETIK pinned defter.pdf', contentType: 'application/pdf',
      sizeBytes: 20, sha256: pinnedHash, scanState: 'Clean', downloadable: true, uploadedBy: actor, uploadedAtUtc: `${date}T08:00:00Z` }], nextCursor: null })
    if (url.includes('/documents?')) return json({ items: [{ id: DOC, companyId: C, ownerKind: 'finance.cash-exchange', ownerId: ID, versionNo: 2,
      fileName: 'SENTETIK defter.pdf', contentType: 'application/pdf', sizeBytes: 20, sha256: evidence.sha256, scanState: 'Clean', downloadable: true,
      uploadedBy: actor, uploadedAtUtc: `${date}T08:00:00Z` }], canUpload: true, canLink: true, limits: { maximumBytes: 1000, extensions: ['.pdf'], contentTypes: ['application/pdf'] } })
    return json(url.endsWith(`/${ID}`) ? { companyId: C, transfer, proposals } : { companyId: C, items: [transfer], nextCursor: null })
  }))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(<QueryClientProvider client={client}><MemoryRouter><CashExchangeBookValuePage companyId={C} actorId={currentActor} canPrepare={canPrepare} canApprove={canApprove} /></MemoryRouter></QueryClientProvider>)
  return posts
}

describe('FIN14 book-value workflow consumer', () => {
  it('uses the real listed document version and freezes the exact proposal/key through an unknown retry', async () => {
    const posts = show(); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /SENTETIK FX belge/ }))
    await user.type(await screen.findByLabelText(T.amount), '3500,00')
    await user.type(screen.getByLabelText(T.targetAmount), '3300,00')
    await user.selectOptions(screen.getByLabelText(T.document), DOC)
    await user.type(screen.getByLabelText(T.reason), value.reason)
    await user.click(screen.getByRole('button', { name: T.prepare }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect((screen.getByLabelText(T.amount) as HTMLInputElement).closest('fieldset')?.disabled).toBe(true)
    expect((screen.getByLabelText(T.targetAmount) as HTMLInputElement).closest('fieldset')?.disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(posts).toHaveLength(2))
    expect(posts[0].body).toBe(posts[1].body)
    expect(new Headers(posts[0].headers).get('Idempotency-Key')).toBe(new Headers(posts[1].headers).get('Idempotency-Key'))
    expect(JSON.parse(posts[0].body as string)).toEqual(attempt.body)
    expect(await screen.findByText(T.replay)).toBeTruthy()
  })
  it('does not offer the maker their own checker action even when the coarse approval capability exists', async () => {
    show([value], false, true); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /SENTETIK FX belge/ }))
    expect(await screen.findByText(T.own)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: T.reject })).toBeNull()
  })
  it('shows the exact original pinned document to a different checker without preparation authority', async () => {
    show([value], false, true, 'SENTETIK-checker'); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /SENTETIK FX belge/ }))
    expect(await screen.findByText(/SENTETIK pinned defter.pdf/)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.download })).toBeTruthy()
    expect(screen.getByRole('button', { name: T.approve })).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.prepare })).toBeNull()
    expect(screen.getByText('SENTETIK vault consumer')).toBeTruthy()
    expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).includes(`/documents/${DOC}/versions?pageSize=50&cursor=3`))).toBe(true)
  })
  it('does not offer a pinned download when the vault version hash differs from the approved source', async () => {
    show([value], false, true, 'SENTETIK-checker', 'c'.repeat(64)); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /SENTETIK FX belge/ }))
    expect(await screen.findByText(T.documentUnavailable)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.download })).toBeNull()
  })
  it('shows a legacy missing target without substituting a benchmark or enabling a fresh approval', async () => {
    const legacy = Object.fromEntries(Object.entries(value).filter(([key]) => key !== 'targetFunctionalValueTl'))
    show([legacy], false, true, 'SENTETIK-checker'); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /SENTETIK FX belge/ }))
    expect(await screen.findByText(T.targetValueMissing)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.approve }) as HTMLButtonElement).disabled).toBe(true)
    await user.type(screen.getByLabelText(T.rejection), 'SENTETIK iki gerçek değerle yeniden hazırlama')
    expect((screen.getByRole('button', { name: T.reject }) as HTMLButtonElement).disabled).toBe(false)
  })
})
