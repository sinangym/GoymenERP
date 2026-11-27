import { useEffect, useRef } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { ReceiptDraftPage } from './ReceiptPages'
import { fetchCounterparties } from '../cari/api'
import { PARTY_ROLE_FLAG, type CounterpartyListItem } from '../cari/contract'
import type { CounterpartyMutationResult } from '../cari/mutationContract'
import { FIELD_LABEL, RECEIPT_DRAFT_NEEDS_PARTY, RESEND_SAME, SAVE_DRAFT } from './financeText'
import { EXAMPLE_ACCOUNT, EXAMPLE_BANK, EXAMPLE_PARTY } from './contractExamples'
import { COMPANY_A, COMPANY_B, NO_REPLY, bankPage, commitContext, contextOk, contextStatus, draftContextBody,
  found, recordingPort, renderFinance, shellFor } from './testHarness'
import type { DraftContextRequest } from './draftContext'

vi.mock('../cari/api', () => ({ fetchCounterparties: vi.fn() }))
vi.mock('./CustomerInstrumentRisk', () => ({ CustomerInstrumentRiskBlock: () => null }))
// Caller seam only: the shared native create/atomic/unknown engine is covered by Cari suites.
const quick = vi.hoisted(() => ({ result: null as boolean | null, signal: null as AbortSignal | null,
  company: null as string | null }))
vi.mock('../cari/CariQuickCreate', () => ({ CariQuickCreate: ({ initialRoles, onCreated, onUnresolvedChange }: {
  initialRoles: number; onCreated: (record: CounterpartyMutationResult, signal: AbortSignal) => Promise<boolean>;
  onUnresolvedChange?: (pending: boolean) => void;
}) => {
  const controller = useRef<AbortController | null>(null)
  useEffect(() => () => controller.current?.abort(), [])
  return <>
    <span data-testid="roles">{initialRoles}</span>
    <button type="button" onClick={() => {
      controller.current?.abort(); const next = new AbortController(); controller.current = next; quick.signal = next.signal
      onUnresolvedChange?.(true)
      void onCreated({ id: EXAMPLE_PARTY, code: 'SENTETIK-NEW', kind: 1, legalName: 'SENTETIK new customer',
        tradeName: null, isActive: true, version: '2', account: { id: EXAMPLE_ACCOUNT,
          companyId: quick.company ?? COMPANY_A, roles: initialRoles, isActive: true, version: '1' } }, next.signal)
        .then(selected => { if (!next.signal.aborted) { quick.result = selected; onUnresolvedChange?.(false) } })
    }}>SENTETIK quick create</button>
    <button type="button" onClick={() => onUnresolvedChange?.(true)}>SENTETIK unresolved</button>
    <button type="button" onClick={() => onUnresolvedChange?.(false)}>SENTETIK resolved</button>
  </>
} }))
const offer: CounterpartyListItem = { id: EXAMPLE_PARTY, code: 'SENTETIK-NEW', kind: 1,
  legalName: 'SENTETIK current customer', tradeName: null, isActive: true, version: 19,
  companyAccounts: [{ id: EXAMPLE_ACCOUNT, companyId: COMPANY_A, roles: 1, isActive: true, version: 23 }] }
function page(items: readonly CounterpartyListItem[] = [offer], nextCursor: string | null = null) {
  return { type: 'ok-single' as const, data: { items, nextCursor, hasMore: nextCursor !== null, totalCount: null, countScope: null } }
}
function mount(search = '', contextFailure?: 403 | 503) {
  const recorder = recordingPort((method, scope, request) => {
    if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
    if (method === 'draftContext' && contextFailure !== undefined) return contextStatus(contextFailure)
    if (method === 'draftContext') return contextOk(draftContextBody(scope.companyId, request as DraftContextRequest))
    return NO_REPLY
  })
  const view = renderFinance(<MemoryRouter initialEntries={[`/finans/tahsilatlar/yeni${search}`]}><ReceiptDraftPage /></MemoryRouter>, { port: recorder.port })
  return { ...recorder, ...view }
}
async function selectCreated() {
  fireEvent.click(screen.getByRole('button', { name: 'SENTETIK quick create' }))
  await waitFor(() => expect(quick.result).toBe(true))
}
async function fill() {
  fireEvent.change(await screen.findByLabelText(FIELD_LABEL.treasuryAccountId), { target: { value: EXAMPLE_BANK } })
  fireEvent.change(screen.getByRole('textbox', { name: /^Tutar/ }), { target: { value: '1000' } })
  fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
  fireEvent.change(screen.getByLabelText(FIELD_LABEL.description), { target: { value: 'SENTETIK/NOT_ENV' } })
  await waitFor(() => expect(screen.getByRole('button', { name: SAVE_DRAFT }).matches(':disabled')).toBe(false))
}
beforeEach(() => { vi.clearAllMocks(); quick.result = null; quick.signal = null; quick.company = null
  vi.mocked(fetchCounterparties).mockResolvedValue(page()) })
afterEach(cleanup)

describe('independent receipt entry shared quick Cari', () => {
  it('uses the editable Customer preselection and all owner pages before the native version-bound draft-context/save', async () => {
    vi.mocked(fetchCounterparties).mockResolvedValueOnce(page([], 'SENTETIK-next')).mockResolvedValueOnce(page())
    const { commands, reads } = mount()
    expect(screen.getByTestId('roles').textContent).toBe(String(PARTY_ROLE_FLAG.Customer))
    expect(screen.queryByRole('button', { name: SAVE_DRAFT })).toBeNull()
    await selectCreated(); await fill()
    expect(fetchCounterparties).toHaveBeenNthCalledWith(2, { mode: 'single', companyId: COMPANY_A },
      expect.objectContaining({ q: 'SENTETIK-NEW', cursor: 'SENTETIK-next' }), expect.any(AbortSignal))
    expect(reads.some(read => read.method === 'draftContext')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: SAVE_DRAFT }))
    expect(commands).toHaveLength(1)
    expect(commands[0].request).toMatchObject({ preparation: { partyId: EXAMPLE_PARTY, companyAccountId: EXAMPLE_ACCOUNT } })
  })
  it.each(['missing', 'wrong-account', 'inactive', 'foreign-account', 'malformed', 'forbidden', 'unavailable'] as const)(
    'does not open the receipt form for an unverified owner offer: %s', async kind => {
      vi.mocked(fetchCounterparties).mockResolvedValue(kind === 'forbidden' ? { type: 'http', status: 403 }
        : kind === 'unavailable' ? { type: 'http', status: 503 } : kind === 'malformed' ? { type: 'malformed' }
        : page(kind === 'missing' ? [] : [{ ...offer, companyAccounts: [{ ...offer.companyAccounts[0],
          ...(kind === 'wrong-account' ? { id: EXAMPLE_BANK } : {}), ...(kind === 'inactive' ? { isActive: false } : {}),
          ...(kind === 'foreign-account' ? { companyId: COMPANY_B } : {}) }] }]))
      const { commands } = mount(); fireEvent.click(screen.getByRole('button', { name: 'SENTETIK quick create' }))
      await waitFor(() => expect(quick.result).toBe(false))
      expect(screen.queryByRole('button', { name: SAVE_DRAFT })).toBeNull(); expect(commands).toHaveLength(0)
    })
  it.each([403, 503] as const)('keeps native saving closed when the receipt owner rejects the current context: %s', async status => {
    const { commands, reads } = mount('', status); await selectCreated()
    const form = screen.getByRole('button', { name: SAVE_DRAFT }).closest('form')
    if (form === null) throw new Error('missing native receipt form')
    fireEvent.change(await screen.findByLabelText(FIELD_LABEL.treasuryAccountId), { target: { value: EXAMPLE_BANK } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
    await waitFor(() => expect(reads.some(read => read.method === 'draftContext')).toBe(true))
    if (status === 403) {
      // Auth-boundary invalidation removes the whole form, including its old event handlers.
      await waitFor(() => expect(screen.queryByRole('button', { name: SAVE_DRAFT })).toBeNull())
      expect(document.body.contains(form)).toBe(false)
      expect(screen.queryByRole('button', { name: 'SENTETIK quick create' })).toBeNull()
    } else {
      expect(screen.getByRole('button', { name: SAVE_DRAFT }).matches(':disabled')).toBe(true)
    }
    fireEvent.submit(form); expect(commands).toHaveLength(0)
  })
  it('does not read eligibility for a foreign mutation response', async () => {
    quick.company = COMPANY_B; mount(); fireEvent.click(screen.getByRole('button', { name: 'SENTETIK quick create' }))
    await waitFor(() => expect(quick.result).toBe(false)); expect(fetchCounterparties).not.toHaveBeenCalled()
  })
  it('preserves filled fields and blocks the native submit handler during an unresolved create; native unknown retry stays frozen', async () => {
    const { commands } = mount(); await selectCreated(); await fill()
    fireEvent.click(screen.getByRole('button', { name: 'SENTETIK unresolved' }))
    const save = screen.getByRole('button', { name: SAVE_DRAFT })
    expect(save.matches(':disabled')).toBe(true); const form = save.closest('form'); expect(form).not.toBeNull()
    if (form === null) throw new Error('missing native receipt form')
    fireEvent.submit(form); expect(commands).toHaveLength(0)
    expect((screen.getByRole('textbox', { name: /^Tutar/ }) as HTMLInputElement).value).toBe('1000')
    fireEvent.click(screen.getByRole('button', { name: 'SENTETIK resolved' })); fireEvent.click(save)
    expect(commands).toHaveLength(1)
    fireEvent.submit(form); expect(commands).toHaveLength(1)
    await commands[0].reject()
    fireEvent.click(await screen.findByRole('button', { name: RESEND_SAME }))
    expect(commands).toHaveLength(2)
    expect(commands[1].key).toBe(commands[0].key)
    expect(commands[1].request).toEqual(commands[0].request)
  })
  it.each(['company', 'session', 'route'] as const)('aborts a late owner response on %s transition', async transition => {
    let release!: (value: Awaited<ReturnType<typeof fetchCounterparties>>) => void
    vi.mocked(fetchCounterparties).mockReturnValue(new Promise(resolve => { release = resolve }))
    mount(); fireEvent.click(screen.getByRole('button', { name: 'SENTETIK quick create' }))
    await waitFor(() => expect(fetchCounterparties).toHaveBeenCalledTimes(1))
    const shell = shellFor(transition === 'company' ? COMPANY_B : COMPANY_A, 2)
    await commitContext({ ...shell, sessionEpoch: transition === 'session' ? 2 : 1,
      bootstrappedSessionEpoch: transition === 'session' ? 2 : 1, routeEntryGeneration: transition === 'route' ? 2 : 0 })
    expect(quick.signal?.aborted).toBe(true)
    await act(async () => { release(page()) })
    expect(screen.queryByRole('button', { name: SAVE_DRAFT })).toBeNull()
  })
  it('recovers an in-flight create after leaving and returning without another Cari; explicit retry keeps the original key/body', async () => {
    const { commands, remount } = mount(); await selectCreated(); await fill()
    fireEvent.click(screen.getByRole('button', { name: SAVE_DRAFT })); expect(commands).toHaveLength(1)
    const original = commands[0]
    remount(<MemoryRouter><p>SENTETIK other screen</p></MemoryRouter>)
    remount(<MemoryRouter initialEntries={['/finans/tahsilatlar/yeni']}><ReceiptDraftPage /></MemoryRouter>)
    const retry = await screen.findByRole('button', { name: RESEND_SAME })
    expect(fetchCounterparties).toHaveBeenCalledTimes(1)
    expect(commands).toHaveLength(1)
    fireEvent.click(retry)
    expect(commands).toHaveLength(2); expect(commands[1].key).toBe(original.key)
    expect(commands[1].request).toEqual(original.request)
  })
  it('recovers the same original draft on a same-session route-entry generation change without a new create', async () => {
    const { commands } = mount(); await selectCreated(); await fill()
    fireEvent.click(screen.getByRole('button', { name: SAVE_DRAFT })); await commands[0].reject()
    await screen.findByRole('button', { name: RESEND_SAME })
    await commitContext({ ...shellFor(COMPANY_A, 2), routeEntryGeneration: 2 })
    fireEvent.click(await screen.findByRole('button', { name: RESEND_SAME }))
    expect(commands).toHaveLength(2); expect(commands[1].key).toBe(commands[0].key)
    expect(commands[1].request).toEqual(commands[0].request)
    expect(fetchCounterparties).toHaveBeenCalledTimes(1)
  })
  it.each(['company', 'session', 'actor'] as const)('does not recover an unresolved draft into another %s', async transition => {
    const { commands, remount } = mount(); await selectCreated(); await fill()
    fireEvent.click(screen.getByRole('button', { name: SAVE_DRAFT })); await commands[0].reject()
    remount(<MemoryRouter><p>SENTETIK other screen</p></MemoryRouter>)
    const shell = shellFor(transition === 'company' ? COMPANY_B : COMPANY_A, 2)
    await commitContext({ ...shell, sessionEpoch: transition === 'session' ? 2 : 1,
      bootstrappedSessionEpoch: transition === 'session' ? 2 : 1,
      activeContext: shell.activeContext === null ? null : { ...shell.activeContext,
        userId: transition === 'actor' ? 'aaaaaaaa-0000-4000-8000-000000000010' : shell.activeContext.userId } })
    remount(<MemoryRouter initialEntries={['/finans/tahsilatlar/yeni']}><ReceiptDraftPage /></MemoryRouter>)
    expect(screen.queryByRole('button', { name: RESEND_SAME })).toBeNull()
    expect(screen.queryByRole('button', { name: SAVE_DRAFT })).toBeNull()
    expect(commands).toHaveLength(1)
  })
  it('retains the original URL account entry and does not offer independent quick create there', async () => {
    mount(`?partyId=${EXAMPLE_PARTY}&companyAccountId=${EXAMPLE_ACCOUNT}`)
    expect(screen.queryByRole('button', { name: 'SENTETIK quick create' })).toBeNull()
    expect(await screen.findByRole('button', { name: SAVE_DRAFT })).not.toBeNull()
    expect(fetchCounterparties).not.toHaveBeenCalled()
  })
  it('keeps a malformed or partial URL account entry closed rather than replacing it with an independent create', () => {
    mount(`?partyId=${EXAMPLE_PARTY}`)
    expect(screen.getByText(RECEIPT_DRAFT_NEEDS_PARTY)).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'SENTETIK quick create' })).toBeNull()
  })
})
