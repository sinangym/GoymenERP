/**
 * CARI-LINK-WORKSPACE-001 — component evidence through the REAL shell
 * (provider, memory router, capability gate, boundary, dirty-switch dialog),
 * with `fetch` stubbed by synthetic fixtures. The shell starts in company B,
 * the TARGET; A is the source the candidate is visible from.
 *
 * G-002 mapping: check 2 (fresh target-scoped selection, passive source
 * labelled, hint is not authority), 3 (roles-only link, one 201 → detail under
 * B), 5 (one X-Company-Id, gating, 403), 6 (q/pageSize/cursor only, no count,
 * invalid_cursor recovery), 7 (same-key retry, replay once), 8 (404 / 409
 * already_linked reconcile through a fresh read), 9 (Kal / discard), 10
 * (candidate decoder leaks).
 *
 * Not browser acceptance (handoff §7.8).
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  ACC_A,
  ALL_CARI_CAPABILITIES,
  A_ID,
  B_ID,
  CHAIN,
  LEGAL_NAME,
  PARTY,
  PARTY_2,
  candidate,
  candidatePage,
  findStateBlock,
  held,
  installFetch,
  json,
  jsonWithoutAck,
  linkedView,
  preferCompany,
  problem,
  renderAt,
  settle,
  type RecordedCall,
} from './commandPageHarness'
import { CAPABILITY } from '../shell/capabilities'
import { PARTY_ROLE_FLAG, PROBLEM_CODE } from './contract'

const WITHOUT_DETAIL = ALL_CARI_CAPABILITIES.filter((c) => c !== CAPABILITY.cariPartiesDetail)
const HEADING = { name: 'Cariyi Şirkete Bağla' } as const
const ROUTE = '/cari/sirkete-bagla'

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
  preferCompany(B_ID)
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

async function openWorkspace(): Promise<void> {
  await screen.findByRole('heading', HEADING, CHAIN)
}
async function findRow(code: string): Promise<HTMLElement> {
  return await screen.findByRole('row', { name: new RegExp(code) }, CHAIN)
}
function radioFor(code: string): HTMLInputElement {
  return screen.getByRole('radio', { name: new RegExp(code) }) as HTMLInputElement
}
function roleCheckbox(name: string): HTMLInputElement {
  return screen.getByRole('checkbox', { name }) as HTMLInputElement
}
function submitButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /^(Şirkete bağla|Gönderiliyor…)$/ }) as HTMLButtonElement
}
function companySelect(): HTMLSelectElement {
  return screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement
}
function query(selector: string): HTMLElement {
  const node = document.querySelector<HTMLElement>(selector)
  if (node === null) throw new Error(`${selector} not rendered`)
  return node
}
function selectionState(): string | null {
  return document.querySelector('[data-selection]')?.getAttribute('data-selection') ?? null
}
function factValue(label: string): string | null {
  const dt = [...document.querySelectorAll('dt')].find((n) => n.textContent === label)
  return dt?.nextElementSibling?.textContent ?? null
}
function cursorOf(call: RecordedCall): string | null {
  return new URL(call.url, 'http://test').searchParams.get('cursor')
}

async function selectAndChoose(user: ReturnType<typeof userEvent.setup>, role = 'Tedarikçi'): Promise<void> {
  await findRow('CAR-000001')
  await user.click(radioFor('CAR-000001'))
  await user.click(roleCheckbox(role))
}

describe('CARI-LINK route, capability and fresh read (G-002 checks 2, 5, 6)', () => {
  it('without cari.accounts.link the route closes non-disclosingly, has no nav entry and reads nothing', async () => {
    const fetchLog = installFetch({ capabilities: [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail] })
    renderAt(ROUTE)
    await findStateBlock('not-found')
    expect(screen.queryByRole('heading', HEADING)).toBeNull()
    expect(screen.queryByRole('link', { name: 'Cariyi Şirkete Bağla' })).toBeNull()
    expect(fetchLog.candidateReads()).toHaveLength(0)
    expect(fetchLog.calls.some((c) => c.url.includes('/counterparties'))).toBe(false)
  })

  it('reads candidates ONCE under the TARGET company with no parameter of its own, and labels sources through the directory', async () => {
    const fetchLog = installFetch()
    renderAt(ROUTE)
    await openWorkspace()
    const row = await findRow('CAR-000001')

    expect(fetchLog.candidateReads()).toHaveLength(1)
    const read = fetchLog.candidateReads()[0]
    expect(read.companyId).toBe(B_ID)
    expect(read.idempotencyKey).toBeNull()
    expect(new URL(read.url, 'http://test').searchParams.toString()).toBe('')

    await waitFor(() => expect(document.body.textContent).toContain('Göymen Sac Profil (GSP)'), CHAIN)
    expect(within(row).getByText('Göymen Demir Çelik (GDC)')).toBeTruthy()
    expect(within(row).getByText('Müşteri, Tedarikçi')).toBeTruthy()
    expect(within(row).getByText('Aktif')).toBeTruthy()
    expect(within(row).getByText('Tüzel kişi')).toBeTruthy()
    expect(radioFor('CAR-000001').checked).toBe(false)
    expect(selectionState()).toBe('none')

    const text = document.body.textContent ?? ''
    for (const raw of [A_ID, B_ID, PARTY, ACC_A]) expect(text).not.toContain(raw)
    expect(text).not.toMatch(/\b\d+ (aday|kayıt)\b/)
    expect(screen.getByRole('region', { name: 'Aday listesi tablosu' })).toBeTruthy()
    expect(document.querySelectorAll('h1')).toHaveLength(1)
    expect(screen.getByRole('link', { name: /listesine dön/ }).getAttribute('href')).toBe('/cari')
  })

  it('a passive SOURCE account is labelled, not hidden and not a default', async () => {
    installFetch({
      candidates: () =>
        json(
          candidatePage([
            candidate({
              visibleSourceAccounts: [{ id: ACC_A, companyId: A_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: false, version: '7' }],
            }),
          ]),
        ),
    })
    renderAt(ROUTE)
    await openWorkspace()
    const row = await findRow('CAR-000001')
    expect(within(row).getByText('Pasif')).toBeTruthy()
    expect(row.querySelector('[data-account-state="inactive"]')).not.toBeNull()
    // Source roles are facts about A; the target roles start empty.
    expect(roleCheckbox('Müşteri').checked).toBe(false)
  })

  it('a candidate page that leaks a target-shaped account or a count is rejected whole (decoder, check 10)', async () => {
    const cases = [
      { label: 'companyAccounts', page: candidatePage([candidate({ companyAccounts: [] })]) },
      { label: 'totalCount', page: candidatePage([candidate()], { totalCount: 1 }) },
    ]
    for (const testCase of cases) {
      cleanup()
      preferCompany(B_ID)
      installFetch({ candidates: () => json(testCase.page) })
      renderAt(ROUTE)
      await openWorkspace()
      await findStateBlock('malformed-response')
      expect(document.body.textContent, testCase.label).not.toContain(LEGAL_NAME)
      expect(screen.queryByRole('radio'), testCase.label).toBeNull()
    }
  })

  it('search sends only q (trimmed); an empty filtered page is "Sonuç yok", an empty unfiltered one "Kayıt yok"', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({ candidates: () => json(candidatePage([])) })
    renderAt(ROUTE)
    await openWorkspace()
    await findStateBlock('empty-no-data')

    await user.type(screen.getByRole('searchbox', { name: 'Aday ara' }), '  Örnek  ')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await waitFor(() => expect(fetchLog.candidateReads()).toHaveLength(2))
    const params = new URL(fetchLog.candidateReads()[1].url, 'http://test').searchParams
    expect([...params.keys()]).toEqual(['q'])
    expect(params.get('q')).toBe('Örnek')
    expect(fetchLog.candidateReads()[1].companyId).toBe(B_ID)
    await findStateBlock('empty-filtered')
  })

  it('403 on the candidate read closes the workspace: no rows, no form, no link; the session is not ended', async () => {
    const fetchLog = installFetch({ candidates: () => problem(403, PROBLEM_CODE.forbidden) })
    renderAt(ROUTE)
    await waitFor(() => {
      const block = document.querySelector('[data-state="forbidden"], [data-state="not-found"]')
      if (block === null) throw new Error('no closed block')
    }, CHAIN)
    expect(screen.queryByRole('radio')).toBeNull()
    expect(screen.queryByRole('button', { name: /Şirkete bağla/ })).toBeNull()
    expect(screen.queryByText(/Oturum sonlandı/)).toBeNull()
    expect(fetchLog.links()).toHaveLength(0)
  })
})

describe('CARI-LINK selection authority (G-002 check 2)', () => {
  it('a ?partyId hint preselects only a Party present in the FRESH page', async () => {
    installFetch()
    renderAt(`${ROUTE}?partyId=${PARTY}`)
    await openWorkspace()
    await findRow('CAR-000001')
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(selectionState()).toBe('fresh')
    expect(document.body.textContent).toContain(`Seçili aday: CAR-000001 — ${LEGAL_NAME}`)
  })

  it('a hint that is not in the fresh page is NO selection: submit lists the error in a FOCUSED summary, links it to every radio, and sends nothing (UX-AC-001)', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch()
    renderAt(`${ROUTE}?partyId=${PARTY_2}`)
    await openWorkspace()
    await findRow('CAR-000001')
    expect(radioFor('CAR-000001').checked).toBe(false)
    expect(selectionState()).toBe('none')
    expect(radioFor('CAR-000001').getAttribute('aria-describedby')).toBeNull()
    expect(radioFor('CAR-000001').getAttribute('aria-invalid')).toBeNull()

    await user.click(roleCheckbox('Tedarikçi'))
    await user.click(submitButton())
    const summary = await waitFor(() => query('#cari-link-summary'))
    expect(summary.getAttribute('role')).toBe('alert')
    expect(summary.getAttribute('tabindex')).toBe('-1')
    expect(within(summary).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'Güncel aday listesinden bir cari seçin.',
    ])
    // The summary takes focus; the field error is linked to the control it belongs to.
    await waitFor(() => expect(document.activeElement).toBe(summary))
    expect(query('#cari-link-selection-error').textContent).toBe('Güncel aday listesinden bir cari seçin.')
    expect(radioFor('CAR-000001').getAttribute('aria-invalid')).toBe('true')
    expect(radioFor('CAR-000001').getAttribute('aria-describedby')).toBe('cari-link-selection-error')
    expect(submitButton().closest('form')?.getAttribute('aria-describedby')).toBe('cari-link-summary')
    expect(fetchLog.links()).toHaveLength(0)

    /**
     * Selecting clears the selection error and its linkage — and, since the
     * 10 September decision, THAT IS THE WHOLE FORM. Clearing the classification
     * back to none leaves a valid link: a fresh selection and no commercial role.
     *
     * This block used to assert the opposite ("Hedef şirket için en az bir rol
     * seçin."), which was the mandatory-role precondition the decision removed.
     */
    await user.click(roleCheckbox('Tedarikçi'))
    await user.click(radioFor('CAR-000001'))
    expect(radioFor('CAR-000001').getAttribute('aria-invalid')).toBeNull()
    expect(radioFor('CAR-000001').getAttribute('aria-describedby')).toBeNull()
    expect(document.querySelector('#cari-link-selection-error')).toBeNull()
    await user.click(submitButton())

    // The link goes out, and its body is the roleless request the contract names:
    // `{}` — not an absent body, and not a fabricated `Customer=1` to get past a
    // rule that no longer exists (spec §3.1).
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(fetchLog.links()[0].body).toEqual({})
    // No roles error was ever raised: there is no rule left for it to report.
    expect(document.querySelector('#cari-link-roles-error')).toBeNull()
  })
})

describe('CARI-LINK selection authority is a FRESH successful read of the CURRENT query (G-002 checks 2, 6; C-004-P2-001)', () => {
  it("a new search suspends the selection while the previous query's rows are shown; only the fresh page restores it", async () => {
    const user = userEvent.setup()
    const gate = held()
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) =>
        new URL(call.url, 'http://test').searchParams.get('q') === 'Örnek'
          ? gate.promise
          : json(candidatePage([candidate()])),
    })
    renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    expect(selectionState()).toBe('fresh')

    await user.type(screen.getByRole('searchbox', { name: 'Aday ara' }), 'Örnek')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await waitFor(() => expect(fetchLog.candidateReads()).toHaveLength(2))
    // The previous query's rows are still on screen (keepPreviousData) and the
    // radio still shows the choice — but the choice is not authority.
    expect(screen.getByRole('row', { name: /CAR-000001/ })).toBeTruthy()
    expect(radioFor('CAR-000001').checked).toBe(true)
    await waitFor(() => expect(selectionState()).toBe('suspended'))
    expect(document.body.textContent).toContain('Seçim güncel aday listesiyle henüz doğrulanmadı.')
    expect(document.body.textContent).not.toContain('Seçili aday: CAR-000001')
    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.links(), 'no POST from previous-query rows').toHaveLength(0)
    await waitFor(() => expect(document.activeElement).toBe(query('#cari-link-summary')))
    expect(query('#cari-link-selection-error').textContent).toBe('Güncel aday listesinden bir cari seçin.')
    expect(radioFor('CAR-000001').getAttribute('aria-describedby')).toBe('cari-link-selection-error')

    // The fresh page of the CURRENT query contains the Party: authority restored.
    gate.release(json(candidatePage([candidate()])))
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    expect(document.querySelector('#cari-link-selection-error')).toBeNull()
    expect(document.body.textContent).toContain(`Seçili aday: CAR-000001 — ${LEGAL_NAME}`)
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(fetchLog.links()[0].url).toBe(`/api/v1/counterparties/${PARTY}/company-account`)
    expect(fetchLog.links()[0].companyId).toBe(B_ID)
  })

  it("when the new query's fresh page does not contain the remembered Party, the selection is NONE — the previous rows' choice does not carry", async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) =>
        json(
          candidatePage(
            new URL(call.url, 'http://test').searchParams.get('q') === 'Başka'
              ? [candidate({ id: PARTY_2, code: 'CAR-000002' })]
              : [candidate()],
          ),
        ),
    })
    renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.type(screen.getByRole('searchbox', { name: 'Aday ara' }), 'Başka')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await findRow('CAR-000002')
    expect(screen.queryByRole('row', { name: /CAR-000001/ })).toBeNull()
    expect(selectionState()).toBe('none')
    expect(radioFor('CAR-000002').checked).toBe(false)
    await user.click(submitButton())
    await settle(100)
    expect(fetchLog.links()).toHaveLength(0)
    expect(query('#cari-link-selection-error')).toBeTruthy()
  })

  it('a malformed continuation hides the table AND suspends the selection: no POST until a fresh successful read confirms it', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) =>
        cursorOf(call) === 'c1'
          ? json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })], { totalCount: 1 }))
          : json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' })),
    })
    renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    expect(selectionState()).toBe('fresh')
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findStateBlock('malformed-response')
    expect(screen.queryByRole('radio')).toBeNull()
    expect(document.body.textContent).not.toContain('CAR-000002')
    // The first-page rows are kept in memory for the retry, not as authority.
    expect(selectionState()).toBe('suspended')
    expect(document.body.textContent).not.toContain('Seçili aday: CAR-000001')

    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.links(), 'no POST after a non-survivable read').toHaveLength(0)
    /**
     * The submit is CLOSED, not merely ineffective: a failed read leaves write
     * compatibility unproven here, and a button that stayed enabled would invite a
     * submit that cannot go and burn an attempt to say so. It carries its reason,
     * because a disabled control that explains nothing is worse than an enabled one.
     */
    expect(submitButton().disabled).toBe(true)
    expect(query('#cari-link-not-ready').textContent).toContain('yazma uyumu şu an doğrulanmadı')

    // A fresh successful first page that contains the Party restores authority.
    // "Tekrar dene" leaves the chain and reads the first page — cursorless, not a
    // refetch of the broken continuation.
    await user.click(within(query('[data-state="malformed-response"]')).getByRole('button', { name: 'Tekrar dene' }))
    await findRow('CAR-000001')
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', null])
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(fetchLog.links()[0].url).toBe(`/api/v1/counterparties/${PARTY}/company-account`)
  })

  it('PRODUCTION cache (30 s staleTime, pages retained): the retained first page is NOT the recovery — a fresh cursorless GET is — and until it lands no link POST is possible (C-005-P2-001)', async () => {
    const user = userEvent.setup()
    const recovery = held()
    let firstPageReads = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) => {
        if (cursorOf(call) === 'c1') {
          return json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })], { totalCount: 1 }))
        }
        firstPageReads += 1
        // 1: the page the chain starts from; 2: the HELD recovery; later: the
        // re-read a successful link triggers (namespace invalidation).
        if (firstPageReads === 1) return json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
        if (firstPageReads === 2) return recovery.promise
        return json(candidatePage([candidate()]))
      },
    })
    const { queryClient } = renderAt(ROUTE, { retainCache: true })
    await openWorkspace()
    await selectAndChoose(user)
    expect(selectionState()).toBe('fresh')
    // The first page is retained AND fresh under the production staleTime: an
    // observer returning to its key would be served from it without a request.
    const cache = queryClient.getQueryCache()
    const retained = cache.findAll().filter((q) => q.queryKey.includes('link-candidates'))
    expect(retained).toHaveLength(1)
    expect(retained[0].isStaleByTime(30_000)).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findStateBlock('malformed-response')
    expect(selectionState()).toBe('suspended')
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1'])

    await user.click(within(query('[data-state="malformed-response"]')).getByRole('button', { name: 'Tekrar dene' }))
    // The recovery is a REQUEST — cursorless, for the current query, under the target —
    // not a cache hit and not a refetch of the broken continuation.
    await waitFor(() => expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', null]))
    expect(fetchLog.candidateReads()[2].companyId).toBe(B_ID)
    expect(new URL(fetchLog.candidateReads()[2].url, 'http://test').searchParams.toString()).toBe('')
    // Held: while it is in flight there are no rows, no radio and no authority.
    await findStateBlock('initial-loading')
    expect(screen.queryByRole('radio')).toBeNull()
    expect(selectionState()).toBe('suspended')
    expect(document.body.textContent).toContain('Seçim güncel aday listesiyle henüz doğrulanmadı.')
    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.links(), 'no POST before the recovery succeeds').toHaveLength(0)
    // The gate is closed while the recovery read is still in flight: compatibility
    // is proven by a read that has LANDED, so the submit carries its reason instead
    // of a validation summary about a selection the user did make.
    expect(submitButton().disabled).toBe(true)
    expect(query('#cari-link-not-ready').textContent).toContain('yazma uyumu şu an doğrulanmadı')
    expect(fetchLog.candidateReads()).toHaveLength(3)

    // The fresh page lands and contains the Party: authority is restored by IT —
    // the one recovery read, no other request in between.
    recovery.release(json(candidatePage([candidate()])))
    await findRow('CAR-000001')
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(document.querySelector('#cari-link-selection-error')).toBeNull()
    expect(fetchLog.candidateReads()).toHaveLength(3)
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(fetchLog.links()[0].url).toBe(`/api/v1/counterparties/${PARTY}/company-account`)
    expect(fetchLog.links()[0].companyId).toBe(B_ID)
    // (A successful link then re-reads candidates fresh — covered by the success suite.)
    queryClient.clear()
  })

  it('a first page STILL in the cache after the break is not authority: its rows show, the selection stays suspended, nothing is POSTed; only a read issued after the break restores it (C-005-P2-001)', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) =>
        cursorOf(call) === 'c1'
          ? json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })], { totalCount: 1 }))
          : json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' })),
    })
    const { queryClient } = renderAt(ROUTE, { retainCache: true })
    await openWorkspace()
    await selectAndChoose(user)
    const cache = queryClient.getQueryCache()
    const firstPage = cache.findAll().find((q) => q.queryKey.includes('link-candidates'))
    if (firstPage === undefined) throw new Error('first page not cached')
    const retained = { queryKey: firstPage.queryKey, data: firstPage.state.data }

    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findStateBlock('malformed-response')
    // The workspace drops the retained first page when it observes the break …
    await waitFor(() => expect(cache.find({ queryKey: retained.queryKey, exact: true })).toBeUndefined())
    // … here it is put back, exactly as production would still hold it: fresh for 30 s.
    queryClient.setQueryData(retained.queryKey, retained.data)

    await user.click(within(query('[data-state="malformed-response"]')).getByRole('button', { name: 'Tekrar dene' }))
    await findRow('CAR-000001')
    // Served from the cache — no request went out — so these rows are NOT authority.
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1'])
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(selectionState()).toBe('suspended')
    expect(document.body.textContent).toContain('Seçim güncel aday listesiyle henüz doğrulanmadı.')
    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.links(), 'no POST from a page read before the break').toHaveLength(0)
    // Nothing was READ after the break, so nothing proved compatibility after it
    // either: the submit is closed and says so, and the suspended selection keeps
    // its own separate explanation above.
    expect(submitButton().disabled).toBe(true)
    expect(query('#cari-link-not-ready').textContent).toContain('yazma uyumu şu an doğrulanmadı')

    // A read ISSUED after the break — here a search — is authority once it lands.
    await user.type(screen.getByRole('searchbox', { name: 'Aday ara' }), 'Örnek')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await waitFor(() => expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', null]))
    expect(new URL(fetchLog.candidateReads()[2].url, 'http://test').searchParams.get('q')).toBe('Örnek')
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(fetchLog.links()[0].url).toBe(`/api/v1/counterparties/${PARTY}/company-account`)
    queryClient.clear()
  })

  it('a continuation page retained from BEFORE the break is not served from the cache after recovery: paging is a fresh request, and only that request makes its rows authority (C-005-P2-001, F-DESKTOP-006)', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) => {
        const cursor = cursorOf(call)
        if (cursor === 'c2') return json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })], { totalCount: 1 }))
        if (cursor === 'c1') {
          return json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })], { hasMore: true, nextCursor: 'c2' }))
        }
        return json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
      },
    })
    const { queryClient } = renderAt(ROUTE, { retainCache: true })
    await openWorkspace()
    await selectAndChoose(user)
    // Page 2 lands and is retained, fresh under the production staleTime.
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findRow('CAR-000002')
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1'])

    // Page 3 breaks the chain.
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findStateBlock('malformed-response')
    expect(selectionState()).toBe('suspended')
    // Every page retained from before the break is gone — the first AND the
    // continuation — while the observed broken read itself stays.
    const cache = queryClient.getQueryCache()
    await waitFor(() =>
      expect(
        cache
          .findAll()
          .filter((q) => q.queryKey.includes('link-candidates'))
          .map((q) => (q.queryKey[3] as { readonly cursor?: string }).cursor),
      ).toEqual(['c2']),
    )

    // Recovery: the fresh first page restores authority for ITS rows.
    await user.click(within(query('[data-state="malformed-response"]')).getByRole('button', { name: 'Tekrar dene' }))
    await findRow('CAR-000001')
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', 'c2', null])

    // Paging after the recovery is a REQUEST — not the page retained from before
    // the break, however fresh production still holds it …
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findRow('CAR-000002')
    await waitFor(() => expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', 'c2', null, 'c1']))
    // … so its rows are authority: a Party chosen from it can be linked.
    await user.click(radioFor('CAR-000002'))
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    expect(document.body.textContent).toContain('Seçili aday: CAR-000002')
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(fetchLog.links()[0].url).toBe(`/api/v1/counterparties/${PARTY_2}/company-account`)
    expect(fetchLog.links()[0].companyId).toBe(B_ID)
    queryClient.clear()
  })

  it('the broken continuation itself is not reused from the cache after recovery: paging onto the same cursor again is a fresh GET (C-DESKTOP-002-P2-001)', async () => {
    const user = userEvent.setup()
    let c1Reads = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) => {
        if (cursorOf(call) === 'c1') {
          c1Reads += 1
          // 1: malformed (a count leaks); 2: the server has recovered.
          return c1Reads === 1
            ? json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })], { totalCount: 1 }))
            : json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })]))
        }
        return json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
      },
    })
    const { queryClient } = renderAt(ROUTE, { retainCache: true })
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findStateBlock('malformed-response')
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1'])

    // Recovery reads a fresh first page; the broken read's own cache entry goes
    // with the observer leaving it — nothing read before the break remains cached.
    await user.click(within(query('[data-state="malformed-response"]')).getByRole('button', { name: 'Tekrar dene' }))
    await findRow('CAR-000001')
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', null])
    const cache = queryClient.getQueryCache()
    await waitFor(() =>
      expect(
        cache
          .findAll()
          .filter((q) => q.queryKey.includes('link-candidates'))
          .map((q) => (q.queryKey[3] as { readonly cursor?: string }).cursor),
      ).toEqual([undefined]),
    )

    // Paging onto the SAME cursor again is a request — the malformed page production
    // would still hold fresh for 30 s is never shown again without one.
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findRow('CAR-000002')
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', null, 'c1'])
    expect(document.querySelector('[data-state="malformed-response"]')).toBeNull()
    await user.click(radioFor('CAR-000002'))
    await waitFor(() => expect(selectionState()).toBe('fresh'))
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(fetchLog.links()[0].url).toBe(`/api/v1/counterparties/${PARTY_2}/company-account`)
    queryClient.clear()
  })

  it('after a terminal 404 / 409 the rows on screen lose authority until a read ISSUED after the failure lands: no POST from them meanwhile, "yenilendi" only then (C-DESKTOP-002-P2-002)', async () => {
    const cases = [
      { label: 'not-found', respond: () => problem(404, PROBLEM_CODE.notFound), notice: 'Cari bulunamadı' },
      { label: 'already-linked', respond: () => problem(409, PROBLEM_CODE.alreadyLinked), notice: 'Zaten bağlı olabilir' },
    ] as const
    for (const testCase of cases) {
      cleanup()
      preferCompany(B_ID)
      const user = userEvent.setup()
      const refresh = held()
      let reads = 0
      const fetchLog = installFetch({
        capabilities: WITHOUT_DETAIL,
        candidates: () => {
          reads += 1
          // 1: the page the choice is made from; 2: the HELD post-failure re-read;
          // later: the re-read a successful link triggers.
          if (reads === 1) return json(candidatePage([candidate(), candidate({ id: PARTY_2, code: 'CAR-000002' })]))
          if (reads === 2) return refresh.promise
          return json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })]))
        },
        // The second attempt links PARTY_2: its 201 must carry THAT id (the decoder verifies it).
        link: (call) =>
          call.url.includes(PARTY_2)
            ? json(linkedView(PARTY_ROLE_FLAG.Supplier, { id: PARTY_2, code: 'CAR-000002' }), 201)
            : testCase.respond(),
      })
      const { queryClient } = renderAt(ROUTE, { retainCache: true })
      await openWorkspace()
      await selectAndChoose(user)
      await user.click(submitButton())
      const notice = await waitFor(() => query(`[data-failure="${testCase.label}"]`))
      expect(notice.textContent, testCase.label).toContain(testCase.notice)

      // The post-failure re-read is issued and held. The old rows are still on
      // screen, the selection is dropped, nothing is announced as refreshed yet …
      await waitFor(() => expect(fetchLog.candidateReads()).toHaveLength(2), CHAIN)
      expect(cursorOf(fetchLog.candidateReads()[1]), testCase.label).toBeNull()
      expect(screen.getByRole('row', { name: /CAR-000001/ }), testCase.label).toBeTruthy()
      expect(selectionState(), testCase.label).toBe('none')
      expect(screen.queryByText('Aday listesi yenilendi.'), testCase.label).toBeNull()
      // … and those rows are NOT authority: choosing from them (which closes the
      // failed attempt) is a suspended choice, and submit sends nothing.
      await user.click(radioFor('CAR-000001'))
      expect(document.querySelector('[data-failure]'), testCase.label).toBeNull()
      await waitFor(() => expect(selectionState()).toBe('suspended'))
      expect(document.body.textContent, testCase.label).toContain('Seçim güncel aday listesiyle henüz doğrulanmadı.')
      await user.click(submitButton())
      await settle(150)
      expect(fetchLog.links(), `${testCase.label}: no POST before the post-failure read lands`).toHaveLength(1)
      await waitFor(() => expect(document.activeElement).toBe(query('#cari-link-summary')))
      expect(query('#cari-link-selection-error').textContent, testCase.label).toBe('Güncel aday listesinden bir cari seçin.')

      // The post-failure read lands: the Party is gone, and the list is announced
      // refreshed NOW — by that read, no other request in between.
      refresh.release(json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })])))
      await waitFor(() => expect(screen.queryByRole('row', { name: /CAR-000001/ })).toBeNull(), CHAIN)
      await screen.findByText('Aday listesi yenilendi.')
      expect(selectionState(), testCase.label).toBe('none')
      expect(fetchLog.candidateReads(), testCase.label).toHaveLength(2)
      // A Party present in THAT read is authority again: a NEW attempt, new key.
      await user.click(radioFor('CAR-000002'))
      await waitFor(() => expect(selectionState()).toBe('fresh'))
      await user.click(submitButton())
      await waitFor(() => expect(fetchLog.links()).toHaveLength(2))
      expect(fetchLog.links()[1].url, testCase.label).toBe(`/api/v1/counterparties/${PARTY_2}/company-account`)
      expect(fetchLog.links()[1].companyId, testCase.label).toBe(B_ID)
      expect(fetchLog.links()[1].idempotencyKey, testCase.label).not.toBe(fetchLog.links()[0].idempotencyKey)
      await waitFor(() => query('[data-outcome="linked"]'), CHAIN)
      queryClient.clear()
    }
  })

  it('a candidate GET issued BEFORE a terminal 404 / 409 that returns AFTER it grants nothing — no selection authority, no POST, no "yenilendi"; only the read issued after the failure does (C-DESKTOP-003 evidence gap)', async () => {
    const cases = [
      { label: 'not-found', respond: () => problem(404, PROBLEM_CODE.notFound), notice: 'Cari bulunamadı' },
      { label: 'already-linked', respond: () => problem(409, PROBLEM_CODE.alreadyLinked), notice: 'Zaten bağlı olabilir' },
    ] as const
    for (const testCase of cases) {
      cleanup()
      preferCompany(B_ID)
      const user = userEvent.setup()
      const late = held() // the continuation issued BEFORE the failure — it returns after it
      const fresh = held() // the first-page read issued AFTER the failure
      let firstPageReads = 0
      const fetchLog = installFetch({
        capabilities: WITHOUT_DETAIL,
        candidates: (call) => {
          if (cursorOf(call) === 'c1') return late.promise
          firstPageReads += 1
          // 1: the page the choice is made from; 2: the HELD post-failure re-read;
          // later: the re-read a successful link triggers.
          if (firstPageReads === 1) {
            return json(
              candidatePage([candidate(), candidate({ id: PARTY_2, code: 'CAR-000002' })], { hasMore: true, nextCursor: 'c1' }),
            )
          }
          if (firstPageReads === 2) return fresh.promise
          return json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })]))
        },
        // The second attempt links PARTY_2: its 201 must carry THAT id (the decoder verifies it).
        link: (call) =>
          call.url.includes(PARTY_2)
            ? json(linkedView(PARTY_ROLE_FLAG.Supplier, { id: PARTY_2, code: 'CAR-000002' }), 201)
            : testCase.respond(),
      })
      const { queryClient } = renderAt(ROUTE, { retainCache: true })
      await openWorkspace()
      await selectAndChoose(user)
      // A continuation of the current query is in flight when the link is sent; the
      // choice made from the current page keeps its authority meanwhile (same-query
      // continuation), so the link can be submitted.
      await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
      await waitFor(() => expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1']))
      expect(selectionState(), testCase.label).toBe('fresh')
      await user.click(submitButton())
      const notice = await waitFor(() => query(`[data-failure="${testCase.label}"]`))
      expect(notice.textContent, testCase.label).toContain(testCase.notice)
      // The failure issues ONE post-failure first-page read (held); the selection is
      // dropped; nothing is announced as refreshed.
      await waitFor(() => expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', null]), CHAIN)
      expect(selectionState(), testCase.label).toBe('none')
      expect(screen.queryByText('Aday listesi yenilendi.'), testCase.label).toBeNull()

      // The PRE-failure continuation returns now — late, after the failure — carrying
      // the very Party that was chosen. It grants nothing: no announcement, no request,
      // and choosing that Party again (which closes the failed attempt) stays suspended.
      late.release(json(candidatePage([candidate()])))
      await settle(150)
      // React Query dropped that read the moment the workspace left its page at the
      // failure (the abort signal the read consumed was cancelled): its late response
      // never becomes data — the continuation entry stays idle and empty …
      const lateEntry = queryClient
        .getQueryCache()
        .findAll()
        .find((q) => q.queryKey.includes('link-candidates') && (q.queryKey[3] as { readonly cursor?: string }).cursor === 'c1')
      expect(lateEntry?.state.fetchStatus, testCase.label).toBe('idle')
      expect(lateEntry?.state.data, `${testCase.label}: a pre-failure read never becomes data`).toBeUndefined()
      // … and the workspace grants nothing on its account.
      expect(screen.queryByText('Aday listesi yenilendi.'), `${testCase.label}: a pre-failure read announces nothing`).toBeNull()
      expect(fetchLog.candidateReads(), testCase.label).toHaveLength(3)
      await user.click(radioFor('CAR-000001'))
      expect(document.querySelector('[data-failure]'), testCase.label).toBeNull()
      await waitFor(() => expect(selectionState()).toBe('suspended'))
      expect(document.body.textContent, testCase.label).toContain('Seçim güncel aday listesiyle henüz doğrulanmadı.')
      await user.click(submitButton())
      await settle(150)
      expect(fetchLog.links(), `${testCase.label}: no POST on the strength of a pre-failure read`).toHaveLength(1)
      await waitFor(() => expect(document.activeElement).toBe(query('#cari-link-summary')))
      expect(query('#cari-link-selection-error').textContent, testCase.label).toBe('Güncel aday listesinden bir cari seçin.')

      // The read issued AFTER the failure lands: announced now, the old Party is gone,
      // and only a Party present in THAT read can be linked — under a new key.
      fresh.release(json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })])))
      await waitFor(() => expect(screen.queryByRole('row', { name: /CAR-000001/ })).toBeNull(), CHAIN)
      await screen.findByText('Aday listesi yenilendi.')
      expect(selectionState(), testCase.label).toBe('none')
      expect(fetchLog.candidateReads(), testCase.label).toHaveLength(3)
      await user.click(radioFor('CAR-000002'))
      await waitFor(() => expect(selectionState()).toBe('fresh'))
      await user.click(submitButton())
      await waitFor(() => expect(fetchLog.links()).toHaveLength(2))
      expect(fetchLog.links()[1].url, testCase.label).toBe(`/api/v1/counterparties/${PARTY_2}/company-account`)
      expect(fetchLog.links()[1].companyId, testCase.label).toBe(B_ID)
      expect(fetchLog.links()[1].idempotencyKey, testCase.label).not.toBe(fetchLog.links()[0].idempotencyKey)
      await waitFor(() => query('[data-outcome="linked"]'), CHAIN)
      queryClient.clear()
    }
  })

  it('a SURVIVABLE continuation failure keeps the loaded rows and their authority: they are current-query server data', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: (call) => {
        if (cursorOf(call) === 'c1') throw new TypeError('Failed to fetch')
        return json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
      },
    })
    renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await screen.findByText('Daha fazla aday yüklenemedi; mevcut adaylar korunuyor.')

    // The ROWS keep their authority: they are still current-query server data, and
    // the selection made from them stands.
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(selectionState()).toBe('fresh')

    /**
     * The WRITE GATE is a different question, and the continuation failure closed it.
     * Compatibility is proven by a fresh SUCCESSFUL read in this context; the last
     * read never reached the server, so there is nothing current to prove it
     * (spec §4.2, and the accepted 07a4af2 revoke-on-unreachable fix). Row authority
     * and write readiness are separate layers — this case shows both at once.
     */
    await user.click(submitButton())
    expect(fetchLog.links()).toHaveLength(0)
  })
})

describe('CARI-LINK command and success (G-002 checks 3, 5)', () => {
  it('links with roles ONLY under one target X-Company-Id; a 201 opens Cari 360 for the verified id under B', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({ detail: () => json(linkedView()) })
    const { router } = renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(submitButton())

    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    const call = fetchLog.links()[0]
    expect(call.url).toBe(`/api/v1/counterparties/${PARTY}/company-account`)
    expect(call.body).toEqual({ roles: PARTY_ROLE_FLAG.Supplier })
    expect(Object.keys(call.body as object)).toEqual(['roles'])
    expect(call.companyId).toBe(B_ID)
    expect(Object.keys(call.headers).filter((h) => h.toLowerCase() === 'x-company-id')).toHaveLength(1)
    expect(call.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)

    await screen.findByRole('heading', { name: /Örnek Metal Sanayi/ }, CHAIN)
    expect(router.state.location.pathname).toBe(`/cari/${PARTY}`)
    await waitFor(() => expect(fetchLog.detailReads()).toHaveLength(1), CHAIN)
    expect(fetchLog.detailReads()[0].companyId).toBe(B_ID)
    expect(factValue('Cari kodu')).toBe('CAR-000001')
    // Label reconciled with ASSUMPTION-CARI-002: the record's classification is
    // shown as history ("Geçmiş ticari sınıflandırma"), not as the counterparty's
    // current commercial direction (ILK_DILIM §7). The VALUE is unchanged.
    expect(factValue('Geçmiş ticari sınıflandırma')).toBe('Tedarikçi')
    await settle(150)
    expect(fetchLog.links()).toHaveLength(1)
  })

  it('without the detail grant: the target account is summarised, candidates are re-read fresh, and the workspace resets', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({ capabilities: WITHOUT_DETAIL })
    const { router } = renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(submitButton())

    const success = await waitFor(() => query('[data-outcome="linked"]'), CHAIN)
    expect(within(success).getByText('CAR-000001')).toBeTruthy()
    expect(factValue('Hedef şirket hesabı')).toBe('Göymen Sac Profil (GSP)')
    expect(factValue('Hedef şirketteki roller')).toBe('Tedarikçi')
    expect(screen.getAllByText('Cari CAR-000001 hedef şirkete bağlandı.')).toHaveLength(1)
    expect(router.state.location.pathname).toBe(ROUTE)
    expect(fetchLog.detailReads()).toHaveLength(0)
    await waitFor(() => expect(fetchLog.candidateReads().length).toBeGreaterThanOrEqual(2), CHAIN)
    expect(fetchLog.candidateReads().every((c) => c.companyId === B_ID)).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Başka cari bağla' }))
    expect(document.querySelector('[data-outcome]')).toBeNull()
    await findRow('CAR-000001')
    expect(selectionState()).toBe('none')
    expect(roleCheckbox('Tedarikçi').checked).toBe(false)
  })
})

describe('CARI-LINK idempotent attempt (G-002 check 7)', () => {
  it('a dropped connection is UNKNOWN; retry resends the SAME key and body; the replay completes exactly once', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      link: () => {
        attempts += 1
        if (attempts === 1) throw new TypeError('Failed to fetch')
        return json(linkedView(), 201, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-0002' })
      },
    })
    renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(submitButton())

    const notice = await waitFor(() => query('[data-attempt="unknown"]'))
    expect(notice.textContent).toContain('İşlem sonucu belirsiz')
    expect(radioFor('CAR-000001').disabled).toBe(true)
    // The OUTER fieldset carries the freeze; the role picker nests its own fieldset inside it.
    expect((query('form[aria-labelledby="cari-link-form-heading"] > fieldset') as HTMLFieldSetElement).disabled).toBe(true)
    expect(screen.getByRole('searchbox', { name: 'Aday ara' })).toHaveProperty('disabled', true)
    await settle(150)
    expect(fetchLog.links(), 'no automatic resend').toHaveLength(1)

    await user.click(within(notice).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => expect(fetchLog.links()).toHaveLength(2))
    const [first, second] = fetchLog.links()
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)
    expect(second.url).toBe(first.url)
    expect(second.companyId).toBe(B_ID)

    await waitFor(() => expect(document.querySelectorAll('[data-outcome="linked"]')).toHaveLength(1), CHAIN)
    expect(screen.getAllByText('Cari CAR-000001 hedef şirkete bağlandı.')).toHaveLength(1)
    await settle(150)
    expect(document.querySelectorAll('[data-outcome="linked"]')).toHaveLength(1)
    expect(fetchLog.links()).toHaveLength(2)
  })

  it('a double click sends ONE link request', async () => {
    const user = userEvent.setup()
    let release: (r: Response) => void = () => {}
    const heldResponse = new Promise<Response>((resolve) => {
      release = resolve
    })
    const fetchLog = installFetch({ capabilities: WITHOUT_DETAIL, link: () => heldResponse })
    renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    const button = submitButton()
    await user.dblClick(button)
    await user.click(button)
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))
    expect(button.getAttribute('aria-busy')).toBe('true')
    await settle(100)
    expect(fetchLog.links()).toHaveLength(1)
    release(json(linkedView(), 201))
    await waitFor(() => query('[data-outcome="linked"]'), CHAIN)
    expect(fetchLog.links()).toHaveLength(1)
  })

  it('after "İşlemi bırak" the SAME Party with the SAME roles is still the unresolved attempt: refused without a new key, retried under its own; other roles are a NEW attempt', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      link: () => {
        attempts += 1
        if (attempts <= 2) throw new TypeError('Failed to fetch')
        return json(linkedView(PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier), 201)
      },
    })
    renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(submitButton())
    const notice = await waitFor(() => query('[data-attempt="unknown"]'))
    await user.click(within(notice).getByRole('button', { name: 'İşlemi bırak' }))

    const kept = query('[data-attempt="abandoned"]')
    expect(kept.textContent).toContain('Bırakılan işlemin sonucu belirsiz')
    expect(radioFor('CAR-000001').disabled).toBe(false)
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(selectionState()).toBe('fresh')
    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.links(), 'unchanged Party + roles: nothing is sent').toHaveLength(1)
    expect(document.activeElement).toBe(kept)

    // Retried under its OWN key and path; unknown again; abandoned again.
    await user.click(within(kept).getByRole('button', { name: 'Önceki işlemi yeniden dene' }))
    await waitFor(() => expect(fetchLog.links()).toHaveLength(2))
    expect(fetchLog.links()[1].idempotencyKey).toBe(fetchLog.links()[0].idempotencyKey)
    expect(fetchLog.links()[1].url).toBe(fetchLog.links()[0].url)
    expect(fetchLog.links()[1].body).toEqual(fetchLog.links()[0].body)
    const again = await waitFor(() => query('[data-attempt="unknown"]'))
    await user.click(within(again).getByRole('button', { name: 'İşlemi bırak' }))
    expect(query('[data-attempt="abandoned"]')).toBeTruthy()

    // Different roles for the same Party: a different business attempt, new key.
    await user.click(roleCheckbox('Müşteri'))
    expect(document.querySelector('[data-attempt]')).toBeNull()
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(3))
    expect(fetchLog.links()[2].idempotencyKey).not.toBe(fetchLog.links()[0].idempotencyKey)
    expect(fetchLog.links()[2].body).toEqual({ roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier })
    await waitFor(() => query('[data-outcome="linked"]'), CHAIN)
    expect(fetchLog.links()).toHaveLength(3)
  })
})

describe('CARI-LINK reconcile after a definite conflict (G-002 check 8)', () => {
  it('404 after a fresh selection: no success, selection dropped, a FRESH first page is the authority', async () => {
    const user = userEvent.setup()
    let reads = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      candidates: () => {
        reads += 1
        return json(candidatePage(reads === 1 ? [candidate()] : []))
      },
      link: () => problem(404, PROBLEM_CODE.notFound),
    })
    const { router } = renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(submitButton())

    const notice = await waitFor(() => query('[data-failure="not-found"]'))
    expect(notice.textContent).toContain('Cari bulunamadı')
    await waitFor(() => expect(fetchLog.candidateReads()).toHaveLength(2), CHAIN)
    expect(cursorOf(fetchLog.candidateReads()[1])).toBeNull()
    await findStateBlock('empty-no-data')
    expect(selectionState()).toBe('none')
    // Announced by the fresh read landing (C-DESKTOP-002-P2-002), not by the error.
    await screen.findByText('Aday listesi yenilendi.')
    expect(document.querySelector('[data-outcome]')).toBeNull()
    expect(router.state.location.pathname).toBe(ROUTE)
    expect(fetchLog.links()).toHaveLength(1)
    await settle(100)
    expect(fetchLog.links(), 'no automatic resend').toHaveLength(1)
  })

  it('409 already_linked: no fake success; the relation is offered only through a fresh Cari 360 read, only when that grant is held', async () => {
    for (const withDetail of [true, false]) {
      cleanup()
      preferCompany(B_ID)
      const user = userEvent.setup()
      const fetchLog = installFetch({
        capabilities: withDetail ? ALL_CARI_CAPABILITIES : WITHOUT_DETAIL,
        link: () => problem(409, PROBLEM_CODE.alreadyLinked),
      })
      const { router } = renderAt(ROUTE)
      await openWorkspace()
      await selectAndChoose(user)
      await user.click(submitButton())

      const notice = await waitFor(() => query('[data-failure="already-linked"]'))
      expect(notice.textContent, String(withDetail)).toContain('Zaten bağlı olabilir')
      const verify = screen.queryByRole('link', { name: 'Güncel durumu Cari 360 ile doğrula' })
      if (withDetail) expect(verify?.getAttribute('href'), 'verify link').toBe(`/cari/${PARTY}`)
      else expect(verify, 'no verify link without the detail grant').toBeNull()
      expect(document.querySelector('[data-outcome]'), String(withDetail)).toBeNull()
      expect(router.state.location.pathname, String(withDetail)).toBe(ROUTE)
      await waitFor(() => expect(fetchLog.candidateReads().length).toBeGreaterThanOrEqual(2), CHAIN)
      expect(selectionState(), String(withDetail)).toBe('none')
      expect(fetchLog.links(), String(withDetail)).toHaveLength(1)
      expect(fetchLog.detailReads(), 'no detail read is issued on its own').toHaveLength(0)
    }
  })
})

describe('CARI-LINK paging (G-002 check 6)', () => {
  it('invalid_cursor drops the chain and reads a FRESH first page; "recovered" is announced only after it lands', async () => {
    const user = userEvent.setup()
    let firstPageReads = 0
    const fetchLog = installFetch({
      candidates: (call) => {
        if (cursorOf(call) === 'c1') return problem(400, PROBLEM_CODE.invalidCursor)
        firstPageReads += 1
        return firstPageReads === 1
          ? json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
          : json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })]))
      },
    })
    renderAt(ROUTE)
    await openWorkspace()
    await findRow('CAR-000001')
    expect(document.body.textContent).not.toContain('ilk sayfaya dönüldü')

    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await findRow('CAR-000002')
    expect(screen.getByText('Aday listesi yenilendi; ilk sayfaya dönüldü.')).toBeTruthy()
    expect(fetchLog.candidateReads().map(cursorOf)).toEqual([null, 'c1', null])
    expect(screen.queryByRole('row', { name: /CAR-000001/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Daha fazla aday yükle' })).toBeNull()
  })

  it('a failed continuation keeps the rows already loaded and offers a retry', async () => {
    const user = userEvent.setup()
    installFetch({
      candidates: (call) => {
        if (cursorOf(call) === 'c1') throw new TypeError('Failed to fetch')
        return json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
      },
    })
    renderAt(ROUTE)
    await openWorkspace()
    await findRow('CAR-000001')
    await user.click(screen.getByRole('button', { name: 'Daha fazla aday yükle' }))
    await screen.findByText('Daha fazla aday yüklenemedi; mevcut adaylar korunuyor.')
    expect(screen.getByRole('row', { name: /CAR-000001/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
  })
})

describe('CARI-LINK dirty workspace and target switch (G-002 check 9)', () => {
  it('"Kal" keeps target, selection and roles; discard opens the other company EMPTY and never resends the old attempt', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({
      candidates: (call) => json(candidatePage(call.companyId === B_ID ? [candidate()] : [])),
      link: () => {
        throw new TypeError('Failed to fetch')
      },
    })
    const { router } = renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)

    // Ask, then stay.
    await user.selectOptions(companySelect(), A_ID)
    let dialog = await screen.findByRole('dialog')
    const stay = within(dialog).getByRole('button', { name: 'Kal' })
    await waitFor(() => expect(document.activeElement).toBe(stay))
    await user.click(stay)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(companySelect().value).toBe(B_ID)
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(roleCheckbox('Tedarikçi').checked).toBe(true)
    expect(fetchLog.capabilityReads().filter((c) => c.companyId === A_ID)).toHaveLength(0)
    expect(fetchLog.candidateReads().filter((c) => c.companyId === A_ID)).toHaveLength(0)

    // An unknown attempt under B, then discard.
    await user.click(submitButton())
    await waitFor(() => query('[data-attempt="unknown"]'))
    expect(fetchLog.links()).toHaveLength(1)
    await user.selectOptions(companySelect(), A_ID)
    dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))

    await waitFor(() => expect(companySelect().value).toBe(A_ID), CHAIN)
    await screen.findByRole('heading', HEADING, CHAIN)
    await findStateBlock('empty-no-data')
    await settle(200)
    expect(document.body.textContent).toContain('Göymen Demir Çelik (GDC)')
    expect(selectionState()).toBe('none')
    expect(roleCheckbox('Tedarikçi').checked).toBe(false)
    expect(document.querySelector('[data-attempt]')).toBeNull()
    expect(document.querySelector('[data-outcome]')).toBeNull()
    expect(router.state.location.pathname).toBe(ROUTE)
    expect(fetchLog.links()).toHaveLength(1)
    expect(fetchLog.links()[0].companyId).toBe(B_ID)
    expect(fetchLog.links().filter((c) => c.companyId === A_ID)).toHaveLength(0)
    expect(fetchLog.candidateReads().filter((c) => c.companyId === A_ID)).toHaveLength(1)
    expect(fetchLog.candidateReads().filter((c) => c.companyId === A_ID).every((c) => cursorOf(c) === null)).toBe(true)
  })

  it('a link still in flight when the user discards: its late 201 writes nothing into the new target', async () => {
    const user = userEvent.setup()
    let release: (r: Response) => void = () => {}
    const heldResponse = new Promise<Response>((resolve) => {
      release = resolve
    })
    const fetchLog = installFetch({
      candidates: (call) => json(candidatePage(call.companyId === B_ID ? [candidate()] : [])),
      link: () => heldResponse,
    })
    const { router } = renderAt(ROUTE)
    await openWorkspace()
    await selectAndChoose(user)
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.links()).toHaveLength(1))

    await user.selectOptions(companySelect(), A_ID)
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => expect(companySelect().value).toBe(A_ID), CHAIN)
    await screen.findByRole('heading', HEADING, CHAIN)
    await findStateBlock('empty-no-data')

    release(json(linkedView(), 201))
    await settle(200)
    expect(router.state.location.pathname).toBe(ROUTE)
    expect(document.querySelector('[data-outcome]')).toBeNull()
    expect(document.body.textContent).not.toContain('bağlandı.')
    expect(fetchLog.detailReads()).toHaveLength(0)
    expect(fetchLog.links()).toHaveLength(1)
    expect(companySelect().value).toBe(A_ID)
  })
})

describe('CARI-LINK the submit follows current readiness (spec §4.2)', () => {
  it('an UNACKNOWLEDGED candidate read leaves the button closed, with its reason', async () => {
    // Decoder-valid rows from an old node: the list is usable, the write is not.
    // The transport would refuse the POST anyway; a button left enabled invites a
    // submit that cannot go and burns an attempt to say so.
    const user = userEvent.setup()
    const fetchLog = installFetch({ candidates: () => jsonWithoutAck(candidatePage([candidate()])) })
    renderAt(ROUTE)
    await openWorkspace()
    await findRow('CAR-000001')

    await user.click(radioFor('CAR-000001'))
    expect(selectionState()).toBe('fresh')
    expect(submitButton().disabled).toBe(true)
    expect(query('#cari-link-not-ready').textContent).toContain('yazma uyumu şu an doğrulanmadı')

    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.links()).toHaveLength(0)
  })

  it('the rows and the selection survive that — only the write is closed', async () => {
    const user = userEvent.setup()
    installFetch({ candidates: () => jsonWithoutAck(candidatePage([candidate()])) })
    renderAt(ROUTE)
    await openWorkspace()
    await findRow('CAR-000001')
    await user.click(radioFor('CAR-000001'))

    // Row authority and write readiness are separate layers: the candidate rows are
    // still server data for the current query.
    expect(radioFor('CAR-000001').checked).toBe(true)
    expect(document.body.textContent).toContain('Seçili aday: CAR-000001')
  })
})

describe('CARI-LINK a pending retry check cannot outlive what it was asked about', () => {
  async function unknownLinkAttempt(user: ReturnType<typeof userEvent.setup>, candidates: () => Response | Promise<Response>) {
    const fetchLog = installFetch({ candidates, link: () => jsonWithoutAck(linkedView(), 201) })
    renderAt(ROUTE)
    await openWorkspace()
    await findRow('CAR-000001')
    await user.click(radioFor('CAR-000001'))
    await user.click(submitButton())
    await screen.findByText(/Sunucu bu işlemi onaylamadı/)
    return fetchLog
  }

  it('changing the candidate search withdraws it: no POST on the late answer', async () => {
    const user = userEvent.setup()
    const recheck = held()
    let reads = 0
    const fetchLog = await unknownLinkAttempt(user, () => {
      reads += 1
      return reads === 1 ? json(candidatePage([candidate()])) : recheck.promise
    })

    /**
     * From ABANDONED, which is the state in which the search is reachable at all:
     * while the attempt is still `unknown` the workspace is frozen, so the sequence
     * the review describes starts by leaving the attempt.
     */
    await user.click(screen.getByRole('button', { name: 'İşlemi bırak' }))
    await user.click(screen.getByRole('button', { name: 'Önceki işlemi yeniden dene' }))
    // The user changes what the list is asking for while the check is in flight.
    await user.type(screen.getByRole('searchbox'), 'beta')
    await user.click(screen.getByRole('button', { name: 'Ara' }))

    /**
     * Even if the held re-check now resolves — and TanStack can resolve it from
     * cache or by cancelling and reverting it — the question it answered was about
     * the previous query. A write for the list the user is looking at NOW cannot be
     * licensed by it.
     */
    recheck.release(json(candidatePage([candidate()])))
    await settle(250)
    expect(fetchLog.links()).toHaveLength(1)
  })

  it('abandoning during the check sends no second link', async () => {
    const user = userEvent.setup()
    const recheck = held()
    let reads = 0
    const fetchLog = await unknownLinkAttempt(user, () => {
      reads += 1
      return reads === 1 ? json(candidatePage([candidate()])) : recheck.promise
    })

    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await user.click(screen.getByRole('button', { name: 'İşlemi bırak' }))

    recheck.release(json(candidatePage([candidate()])))
    await settle(250)
    expect(fetchLog.links()).toHaveLength(1)
  })
})
