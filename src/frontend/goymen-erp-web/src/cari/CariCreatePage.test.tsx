/**
 * C-02 Yeni Cari — component evidence through the REAL shell: provider,
 * memory router, capability gate, 401/403 boundary and the dirty-switch dialog.
 * `fetch` is stubbed with synthetic fixtures (no business database, no secret).
 *
 * G-002 mapping: check 1 (single success → detail), 4 (fields/trim/400 codes),
 * 5 (one X-Company-Id, capability gating, 403), 7 (double submit, same-key
 * retry, replay once, idempotency_conflict), 9 (Kal / Değişiklikleri bırak,
 * late response), 10 (malformed 201 is not success).
 *
 * None of this is browser acceptance (handoff §7.8): jsdom + a fetch stub
 * prove the component contract, not the A→B chain against the real API.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  ALL_CARI_CAPABILITIES,
  A_ID,
  B_ID,
  CHAIN,
  LEGAL_NAME,
  PARTY,
  findStateBlock,
  held,
  installFetch,
  json,
  jsonWithoutAck,
  linkedView,
  mutationView,
  problem,
  renderAt,
  settle,
} from './commandPageHarness'
import { CAPABILITY } from '../shell/capabilities'
import { PROBLEM_CODE } from './contract'

const WITHOUT_DETAIL = ALL_CARI_CAPABILITIES.filter((c) => c !== CAPABILITY.cariPartiesDetail)
const HEADING = { name: 'Yeni Cari' } as const

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

async function openCreate(): Promise<void> {
  await screen.findByRole('heading', HEADING, CHAIN)
}

function kindSelect(): HTMLSelectElement {
  return screen.getByLabelText('Taraf türü') as HTMLSelectElement
}
function legalNameInput(): HTMLInputElement {
  return screen.getByLabelText('Hukuki ad') as HTMLInputElement
}
function tradeNameInput(): HTMLInputElement {
  return screen.getByLabelText(/^Ticari ad/) as HTMLInputElement
}
function submitButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /^(Cari oluştur|Gönderiliyor…)$/ }) as HTMLButtonElement
}
function companySelect(): HTMLSelectElement {
  return screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement
}
function query(selector: string): HTMLElement {
  const node = document.querySelector<HTMLElement>(selector)
  if (node === null) throw new Error(`${selector} not rendered`)
  return node
}
function factValue(label: string): string | null {
  const dt = [...document.querySelectorAll('dt')].find((n) => n.textContent === label)
  return dt?.nextElementSibling?.textContent ?? null
}

async function fillValid(user: ReturnType<typeof userEvent.setup>): Promise<void> {
  await user.selectOptions(kindSelect(), '1')
  await user.type(legalNameInput(), LEGAL_NAME)
  await user.click(screen.getByRole('checkbox', { name: 'Müşteri' }))
  await user.click(screen.getByRole('checkbox', { name: 'Tedarikçi' }))
}

describe('C-02 route and capability (G-002 check 5)', () => {
  it('without cari.parties.create the route closes non-disclosingly, has no nav entry and sends nothing', async () => {
    const fetchLog = installFetch({ capabilities: [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail] })
    renderAt('/cari/yeni')
    await findStateBlock('not-found')
    expect(screen.queryByRole('heading', HEADING)).toBeNull()
    expect(screen.queryByRole('link', { name: 'Yeni Cari' })).toBeNull()
    // Inside <main>: the shell toolbar's global search box (AT-002) is not this route's content.
    expect(within(screen.getByRole('main')).queryByRole('textbox')).toBeNull()
    expect(fetchLog.calls.some((c) => c.url.includes('/counterparties'))).toBe(false)
  })

  it('renders exactly the four command fields, the company read-only from the directory, no raw id, nothing else', async () => {
    installFetch()
    renderAt('/cari/yeni')
    await openCreate()
    expect(screen.queryByRole('link', { name: 'Yeni Cari' })).toBeNull()
    expect(kindSelect().tagName).toBe('SELECT')
    expect(kindSelect().getAttribute('aria-required')).toBe('true')
    expect(legalNameInput().getAttribute('aria-required')).toBe('true')
    expect(tradeNameInput().hasAttribute('required')).toBe(false)
    // Inside <main>: the shell toolbar's global search box (AT-002) is not one of the form's fields.
    expect(within(screen.getByRole('main')).getAllByRole('textbox')).toHaveLength(2)
    expect(screen.getAllByRole('checkbox').map((c) => c.getAttribute('id'))).toEqual([
      'cari-create-role-customer',
      'cari-create-role-supplier',
      'cari-create-role-tolling',
      'cari-create-role-service',
    ])
    await waitFor(() => expect(document.body.textContent).toContain('Göymen Demir Çelik (GDC)'), CHAIN)
    const text = document.body.textContent ?? ''
    for (const forbidden of ['VKN', 'TCKN', 'Banka', 'Adres', 'Risk', 'Limit', 'Bakiye', 'Gerekçe']) {
      expect(text, forbidden).not.toContain(forbidden)
    }
    expect(text).not.toContain(A_ID)
    expect(document.querySelectorAll('h1')).toHaveLength(1)
  })
})

describe('C-02 client validation (G-002 check 4; UX-AC-001)', () => {
  it('a client-invalid submit renders the summary, moves focus to the SUMMARY (not a field), links each field error to its control and mints NO key', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch()
    renderAt('/cari/yeni')
    await openCreate()

    await user.click(submitButton())
    const summary = await waitFor(() => query('#cari-create-summary'))
    expect(summary.getAttribute('role')).toBe('alert')
    expect(summary.getAttribute('tabindex')).toBe('-1')
    // Two, not three: the classification is optional since the 10 September decision,
    // so an untouched picker is no longer listed as something the user left out.
    expect(within(summary).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'Taraf türü seçin.',
      'Hukuki ad zorunludur.',
    ])
    expect(legalNameInput().closest('form')?.getAttribute('aria-describedby')).toBe('cari-create-summary')
    // UX-AC-001: the summary takes focus on the first invalid submit — not the first invalid field.
    await waitFor(() => expect(document.activeElement).toBe(summary))
    // Each field error stays linked to its own control, with the text the summary lists.
    expect(kindSelect().getAttribute('aria-invalid')).toBe('true')
    expect(kindSelect().getAttribute('aria-describedby')).toBe('cari-create-kind-error')
    expect(query('#cari-create-kind-error').textContent).toBe('Taraf türü seçin.')
    expect(legalNameInput().getAttribute('aria-invalid')).toBe('true')
    expect(legalNameInput().getAttribute('aria-describedby')).toBe('cari-create-legal-name-error')
    expect(query('#cari-create-legal-name-error').textContent).toBe('Hukuki ad zorunludur.')
    expect(tradeNameInput().getAttribute('aria-invalid')).toBeNull()
    expect(tradeNameInput().getAttribute('aria-describedby')).toBe('cari-create-trade-name-hint')
    // The classification picker is NOT marked invalid and carries no error: leaving
    // it untouched is a complete answer, so flagging it would accuse the user of an
    // omission that is not one.
    const customer = screen.getByRole('checkbox', { name: 'Müşteri' })
    expect(customer.getAttribute('aria-invalid')).toBeNull()
    expect(customer.getAttribute('aria-describedby')).toBeNull()
    expect(document.querySelector('#cari-create-roles-error')).toBeNull()
    expect(fetchLog.creates()).toHaveLength(0)

    // The summary precedes the fields: Tab from it reaches the first control to fix.
    await user.tab()
    expect(document.activeElement).toBe(kindSelect())

    /**
     * Correcting the two REQUIRED fields completes the form. The classification is
     * still untouched, and that is now a finished answer, not a remaining error —
     * this block used to assert "En az bir rol seçin.", the precondition the
     * 10 September decision removed.
     */
    await user.selectOptions(kindSelect(), '1')
    await user.type(legalNameInput(), 'X')
    await user.click(submitButton())

    await waitFor(() => expect(fetchLog.creates()).toHaveLength(1))
    // `roles` is OMITTED rather than sent as a fabricated classification. Absent and
    // numeric 0 are the same normalized payload; absence is what "the user chose
    // none" looks like (spec §3.1).
    expect(fetchLog.creates()[0].body).toEqual({ kind: 1, legalName: 'X', tradeName: null })
    // The form is replaced by the success state, so the summary and the roles error
    // are gone — there is nothing left to correct.
    expect(document.querySelector('#cari-create-summary')).toBeNull()
    expect(document.querySelector('#cari-create-roles-error')).toBeNull()
  })
})

describe('C-02 command wire (G-002 checks 4, 5)', () => {
  it('sends ONE X-Company-Id from the shell, one key, and exactly the four trimmed fields', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({ capabilities: WITHOUT_DETAIL })
    renderAt('/cari/yeni')
    await openCreate()

    await user.selectOptions(kindSelect(), '2')
    await user.type(legalNameInput(), `  ${LEGAL_NAME}  `)
    await user.type(tradeNameInput(), '   ')
    await user.click(screen.getByRole('checkbox', { name: 'Fason' }))
    await user.click(screen.getByRole('checkbox', { name: 'Hizmet' }))
    await user.click(submitButton())

    await waitFor(() => expect(fetchLog.creates()).toHaveLength(1))
    const call = fetchLog.creates()[0]
    expect(call.url).toBe('/api/v1/counterparties')
    expect(call.body).toEqual({ kind: 2, legalName: LEGAL_NAME, tradeName: null, roles: 12 })
    expect(Object.keys(call.body as object)).toEqual(['kind', 'legalName', 'tradeName', 'roles'])
    expect(call.companyId).toBe(A_ID)
    expect(Object.keys(call.headers).filter((h) => h.toLowerCase() === 'x-company-id')).toHaveLength(1)
    expect(call.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(call.headers['Content-Type']).toBe('application/json')
    expect(call.url).not.toContain(A_ID)
  })
})

describe('C-02 success (G-002 checks 1, 10)', () => {
  it('a 201 completes ONCE: the verified id opens Cari 360 under the same company; one create, one detail read', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch()
    const { router } = renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())

    await screen.findByRole('heading', { name: /Örnek Metal Sanayi/ }, CHAIN)
    expect(router.state.location.pathname).toBe(`/cari/${PARTY}`)
    await waitFor(() => expect(fetchLog.detailReads()).toHaveLength(1), CHAIN)
    expect(fetchLog.detailReads()[0].companyId).toBe(A_ID)
    expect(fetchLog.detailReads()[0].url).toContain(`/counterparties/${PARTY}`)
    expect(factValue('Cari kodu')).toBe('CAR-000001')
    // Label reconciled with ASSUMPTION-CARI-002: the record's classification is
    // shown as history ("Geçmiş ticari sınıflandırma"), not as the counterparty's
    // current commercial direction (ILK_DILIM §7). The VALUE is unchanged.
    expect(factValue('Geçmiş ticari sınıflandırma')).toBe('Müşteri, Tedarikçi')
    await settle(150)
    expect(fetchLog.creates()).toHaveLength(1)
    expect(fetchLog.detailReads()).toHaveLength(1)
  })

  it('without the detail grant: the record is summarised with the server code, no detail read, and can be reset', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({ capabilities: WITHOUT_DETAIL })
    const { router } = renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())

    const success = await waitFor(() => query('[data-outcome="created"]'), CHAIN)
    expect(within(success).getByText('CAR-000001')).toBeTruthy()
    expect(factValue('Roller')).toBe('Müşteri, Tedarikçi')
    expect(factValue('Şirket hesabı')).toBe('Göymen Demir Çelik (GDC)')
    const announcement = screen.getByText('Cari CAR-000001 oluşturuldu.')
    expect(announcement.closest('[role="status"]')).not.toBeNull()
    expect(router.state.location.pathname).toBe('/cari/yeni')
    expect(fetchLog.detailReads()).toHaveLength(0)
    expect(screen.queryByRole('link', { name: /Cari 360/ })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Yeni cari oluştur' }))
    expect(document.querySelector('[data-outcome="created"]')).toBeNull()
    expect(legalNameInput().value).toBe('')
    expect(kindSelect().value).toBe('')
  })

  it('a 201 the decoder rejects (foreign account) is NOT success: no navigation, no summary, outcome unknown', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({ create: () => json(linkedView(), 201) })
    const { router } = renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())

    const notice = await waitFor(() => query('[data-attempt="unknown"]'))
    expect(notice.textContent).toContain('Sunucu yanıtı beklenen biçimde değil.')
    expect(router.state.location.pathname).toBe('/cari/yeni')
    expect(document.querySelector('[data-outcome]')).toBeNull()
    expect(fetchLog.detailReads()).toHaveLength(0)
    expect(document.body.textContent).not.toContain(B_ID)
  })
})

describe('C-02 idempotent attempt (G-002 check 7)', () => {
  it('a double click sends ONE request; the button is busy and the fields are frozen meanwhile', async () => {
    const user = userEvent.setup()
    const gate = held()
    const fetchLog = installFetch({ capabilities: WITHOUT_DETAIL, create: () => gate.promise })
    renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)

    const button = submitButton()
    await user.dblClick(button)
    await user.click(button)
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(1))
    expect(button.disabled).toBe(true)
    expect(button.getAttribute('aria-busy')).toBe('true')
    expect(button.textContent).toBe('Gönderiliyor…')
    expect(legalNameInput().closest('fieldset')?.disabled).toBe(true)
    await settle(100)
    expect(fetchLog.creates()).toHaveLength(1)

    gate.release(json(mutationView(), 201))
    await waitFor(() => query('[data-outcome="created"]'), CHAIN)
    expect(fetchLog.creates()).toHaveLength(1)
  })

  it('a dropped connection leaves the outcome UNKNOWN; retry resends the SAME key and body; the replay completes exactly once', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      create: () => {
        attempts += 1
        if (attempts === 1) throw new TypeError('Failed to fetch')
        return json(mutationView(), 201, {
          'Idempotency-Replayed': 'true',
          'Idempotency-Original-Correlation-Id': 'corr-0001',
        })
      },
    })
    renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())

    const notice = await waitFor(() => query('[data-attempt="unknown"]'))
    expect(notice.getAttribute('role')).toBe('alert')
    expect(notice.textContent).toContain('İşlem sonucu belirsiz')
    expect(notice.textContent).toContain('Sunucuya ulaşılamadı.')
    expect(legalNameInput().closest('fieldset')?.disabled).toBe(true)
    expect(submitButton().disabled).toBe(true)
    await settle(150)
    expect(fetchLog.creates(), 'no automatic resend').toHaveLength(1)

    await user.click(within(notice).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(2))
    const [first, second] = fetchLog.creates()
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)
    expect(second.companyId).toBe(A_ID)

    await waitFor(() => expect(document.querySelectorAll('[data-outcome="created"]')).toHaveLength(1), CHAIN)
    expect(screen.getAllByText('Cari CAR-000001 oluşturuldu.')).toHaveLength(1)
    await settle(150)
    expect(document.querySelectorAll('[data-outcome="created"]')).toHaveLength(1)
    expect(fetchLog.creates()).toHaveLength(2)
    expect(document.querySelector('[data-attempt]')).toBeNull()
  })

  it('503, a non-201 2xx and offline are all UNKNOWN: frozen snapshot, no navigation, no key change on retry', async () => {
    const cases: readonly { readonly label: string; readonly respond: () => Response; readonly reason: string; readonly offline?: boolean }[] = [
      { label: '503', respond: () => problem(503, PROBLEM_CODE.serviceUnavailable), reason: 'Servis geçici olarak yanıt veremedi.' },
      { label: '200', respond: () => json(mutationView(), 200), reason: 'Sunucu yanıtı beklenen biçimde değil.' },
      {
        label: 'offline',
        respond: () => {
          throw new TypeError('Failed to fetch')
        },
        reason: 'Bağlantı yok.',
        offline: true,
      },
    ]
    for (const testCase of cases) {
      cleanup()
      vi.stubGlobal('navigator', { onLine: true })
      const user = userEvent.setup()
      const fetchLog = installFetch({ create: testCase.respond })
      const { router } = renderAt('/cari/yeni')
      await openCreate()
      await fillValid(user)
      if (testCase.offline) vi.stubGlobal('navigator', { onLine: false })
      await user.click(submitButton())

      const notice = await waitFor(() => query('[data-attempt="unknown"]'))
      expect(notice.textContent, testCase.label).toContain(testCase.reason)
      expect(router.state.location.pathname, testCase.label).toBe('/cari/yeni')
      expect(document.querySelector('[data-outcome]'), testCase.label).toBeNull()
      expect(fetchLog.detailReads(), testCase.label).toHaveLength(0)
      await settle(100)
      expect(fetchLog.creates(), `${testCase.label}: no automatic resend`).toHaveLength(1)

      vi.stubGlobal('navigator', { onLine: true })
      await user.click(within(notice).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
      await waitFor(() => expect(fetchLog.creates()).toHaveLength(2))
      expect(fetchLog.creates()[1].idempotencyKey, testCase.label).toBe(fetchLog.creates()[0].idempotencyKey)
    }
  })

  it('"İşlemi bırak" unfreezes the form but keeps the unresolved attempt; a CHANGED payload is a NEW attempt with a NEW key', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      create: () => {
        attempts += 1
        if (attempts === 1) throw new TypeError('Failed to fetch')
        return json(mutationView(), 201)
      },
    })
    renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())
    const notice = await waitFor(() => query('[data-attempt="unknown"]'))
    await user.click(within(notice).getByRole('button', { name: 'İşlemi bırak' }))
    expect(document.querySelector('[data-attempt="unknown"]')).toBeNull()
    // Unfrozen — but the unchanged payload still names the abandoned, unresolved
    // attempt, and the form says so (C-004-P1-001).
    expect(query('[data-attempt="abandoned"]').textContent).toContain('Bırakılan işlemin sonucu belirsiz')
    expect(legalNameInput().closest('fieldset')?.disabled).toBe(false)
    expect(submitButton().disabled).toBe(false)
    // The payload was edited: this is a different business attempt, and the box goes.
    await user.type(legalNameInput(), ' Ltd.')
    expect(document.querySelector('[data-attempt]')).toBeNull()
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(2))
    const [first, second] = fetchLog.creates()
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey)
    expect(second.body).toEqual({ ...(first.body as object), legalName: `${LEGAL_NAME} Ltd.` })
    await waitFor(() => query('[data-outcome="created"]'), CHAIN)
  })

  it('after "İşlemi bırak" the UNCHANGED payload is never re-sent under a new key: submit sends nothing and focuses the explanation; "Önceki işlemi yeniden dene" resends the SAME key and completes once', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      create: () => {
        attempts += 1
        if (attempts === 1) throw new TypeError('Failed to fetch')
        return json(mutationView(), 201, {
          'Idempotency-Replayed': 'true',
          'Idempotency-Original-Correlation-Id': 'corr-0003',
        })
      },
    })
    renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())
    const notice = await waitFor(() => query('[data-attempt="unknown"]'))
    await user.click(within(notice).getByRole('button', { name: 'İşlemi bırak' }))

    const kept = query('[data-attempt="abandoned"]')
    expect(kept.getAttribute('role')).toBe('alert')
    expect(kept.getAttribute('tabindex')).toBe('-1')
    expect(kept.textContent).toContain('aynı bilgilerle yeni bir işlem başlatılmaz')
    expect(within(kept).queryByRole('button', { name: 'Aynı işlemi yeniden dene' })).toBeNull()
    expect(legalNameInput().value).toBe(LEGAL_NAME)

    // Unchanged payload, submit: NO request, NO new key, focus on the explanation.
    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.creates(), 'unchanged payload is not re-sent').toHaveLength(1)
    expect(document.activeElement).toBe(kept)
    expect(document.querySelector('[data-outcome]')).toBeNull()
    expect(document.querySelector('#cari-create-summary')).toBeNull()

    // What is compared is the PAYLOAD: a role toggled away and back is unchanged again.
    await user.click(screen.getByRole('checkbox', { name: 'Fason' }))
    expect(document.querySelector('[data-attempt]')).toBeNull()
    await user.click(screen.getByRole('checkbox', { name: 'Fason' }))
    expect(query('[data-attempt="abandoned"]')).toBeTruthy()
    await user.click(submitButton())
    await settle(100)
    expect(fetchLog.creates()).toHaveLength(1)

    // The kept attempt is retried under its OWN key; the replay completes it once.
    await user.click(within(query('[data-attempt="abandoned"]')).getByRole('button', { name: 'Önceki işlemi yeniden dene' }))
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(2))
    const [first, second] = fetchLog.creates()
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)
    expect(second.companyId).toBe(A_ID)
    await waitFor(() => expect(document.querySelectorAll('[data-outcome="created"]')).toHaveLength(1), CHAIN)
    expect(screen.getAllByText('Cari CAR-000001 oluşturuldu.')).toHaveLength(1)
    await settle(150)
    expect(fetchLog.creates()).toHaveLength(2)
    expect(document.querySelectorAll('[data-outcome="created"]')).toHaveLength(1)
    expect(document.querySelector('[data-attempt]')).toBeNull()
  })
})

describe('C-02 definite failures by stable code (G-002 checks 4, 7)', () => {
  it('400 validation_failed: form preserved, no field error from localized text, no resend; an edited resubmit uses a NEW key', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetchLog = installFetch({
      capabilities: WITHOUT_DETAIL,
      create: () => {
        attempts += 1
        return attempts === 1
          ? problem(400, PROBLEM_CODE.validationFailed, 'Doğrulama hatası')
          : json(mutationView(), 201)
      },
    })
    renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())

    const notice = await waitFor(() => query('[data-failure="validation"]'))
    expect(notice.textContent).toContain('Sunucu doğrulaması başarısız')
    expect(document.body.textContent).not.toContain('Yerel ayrıntı metni')
    expect(document.body.textContent).not.toContain('Doğrulama hatası')
    expect(legalNameInput().value).toBe(LEGAL_NAME)
    expect((screen.getByRole('checkbox', { name: 'Müşteri' }) as HTMLInputElement).checked).toBe(true)
    expect(legalNameInput().closest('fieldset')?.disabled).toBe(false)
    expect(document.querySelector('[data-outcome]')).toBeNull()
    await settle(100)
    expect(fetchLog.creates()).toHaveLength(1)

    await user.type(legalNameInput(), ' Ltd.')
    expect(document.querySelector('[data-failure]'), 'editing closes the failed attempt').toBeNull()
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(2))
    const [first, second] = fetchLog.creates()
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey)
    expect(second.body).toEqual({ ...(first.body as object), legalName: `${LEGAL_NAME} Ltd.` })
  })

  it('command_metadata_invalid, idempotency_conflict and a code-less 400 are shown by code, never resent, and acknowledged', async () => {
    const cases: readonly { readonly respond: () => Response; readonly failure: string; readonly title: string }[] = [
      { respond: () => problem(400, PROBLEM_CODE.commandMetadataInvalid), failure: 'metadata', title: 'İşlem bilgisi geçersiz' },
      { respond: () => problem(409, PROBLEM_CODE.idempotencyConflict), failure: 'idempotency-conflict', title: 'İşlem çakışması' },
      { respond: () => new Response(null, { status: 400 }), failure: 'unexpected', title: 'İşlem tamamlanamadı' },
    ]
    for (const testCase of cases) {
      cleanup()
      const user = userEvent.setup()
      const fetchLog = installFetch({ capabilities: WITHOUT_DETAIL, create: testCase.respond })
      renderAt('/cari/yeni')
      await openCreate()
      await fillValid(user)
      await user.click(submitButton())

      const notice = await waitFor(() => query(`[data-failure="${testCase.failure}"]`))
      expect(notice.textContent, testCase.failure).toContain(testCase.title)
      expect(document.querySelector('[data-outcome]'), testCase.failure).toBeNull()
      expect(submitButton().disabled, testCase.failure).toBe(true)
      await settle(100)
      expect(fetchLog.creates(), `${testCase.failure}: no automatic resend`).toHaveLength(1)

      await user.click(within(notice).getByRole('button', { name: 'Anladım' }))
      expect(document.querySelector('[data-failure]'), testCase.failure).toBeNull()
      expect(submitButton().disabled, testCase.failure).toBe(false)
      expect(legalNameInput().value, testCase.failure).toBe(LEGAL_NAME)
    }
  })

  it('403 on the command closes the form onto a non-disclosing block; the session is not ended', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({ create: () => problem(403, PROBLEM_CODE.forbidden) })
    renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())

    await waitFor(() => {
      const block = document.querySelector('[data-state="forbidden"], [data-state="not-found"]')
      if (block === null) throw new Error('no closed block')
    }, CHAIN)
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /Cari oluştur/ })).toBeNull()
    expect(screen.queryByText(/Oturum sonlandı/)).toBeNull()
    expect(fetchLog.creates()).toHaveLength(1)
  })
})

describe('C-02 dirty form and company switch (G-002 check 9)', () => {
  it('a dirty form makes the switch ASK: "Kal" keeps company and form; nothing is fetched for the other company', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch()
    renderAt('/cari/yeni')
    await openCreate()
    await user.type(legalNameInput(), 'Taslak Cari')

    await user.selectOptions(companySelect(), B_ID)
    const dialog = await screen.findByRole('dialog')
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(dialog.textContent).toContain('Göymen Sac Profil')
    const stay = within(dialog).getByRole('button', { name: 'Kal' })
    await waitFor(() => expect(document.activeElement).toBe(stay))
    // The form is still mounted underneath, untouched.
    expect(legalNameInput().value).toBe('Taslak Cari')

    await user.click(stay)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(companySelect().value).toBe(A_ID)
    expect(legalNameInput().value).toBe('Taslak Cari')
    expect(fetchLog.capabilityReads().filter((c) => c.companyId === B_ID)).toHaveLength(0)
    expect(fetchLog.creates()).toHaveLength(0)
    expect(document.activeElement).toBe(companySelect())
  })

  it('Escape in the dialog means "Kal"; Tab cycles inside the two actions', async () => {
    const user = userEvent.setup()
    installFetch()
    renderAt('/cari/yeni')
    await openCreate()
    await user.type(legalNameInput(), 'Taslak Cari')
    await user.selectOptions(companySelect(), B_ID)
    const dialog = await screen.findByRole('dialog')
    const stay = within(dialog).getByRole('button', { name: 'Kal' })
    const discard = within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' })
    await waitFor(() => expect(document.activeElement).toBe(stay))
    await user.tab()
    expect(document.activeElement).toBe(discard)
    await user.tab()
    expect(document.activeElement).toBe(stay)
    await user.tab({ shift: true })
    expect(document.activeElement).toBe(discard)

    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(companySelect().value).toBe(A_ID)
    expect(legalNameInput().value).toBe('Taslak Cari')
  })

  it('"Değişiklikleri bırak" opens the new company with an EMPTY form; nothing typed crosses over and nothing is sent', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch()
    renderAt('/cari/yeni')
    await openCreate()
    await user.selectOptions(kindSelect(), '1')
    await user.type(legalNameInput(), 'Taslak Cari')
    await user.click(screen.getByRole('checkbox', { name: 'Müşteri' }))

    await user.selectOptions(companySelect(), B_ID)
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))

    await waitFor(() => expect(companySelect().value).toBe(B_ID), CHAIN)
    await screen.findByRole('heading', HEADING, CHAIN)
    await waitFor(() => expect(legalNameInput().value).toBe(''), CHAIN)
    expect(kindSelect().value).toBe('')
    expect((screen.getByRole('checkbox', { name: 'Müşteri' }) as HTMLInputElement).checked).toBe(false)
    expect(document.body.textContent).not.toContain('Taslak Cari')
    await waitFor(() => expect(document.body.textContent).toContain('Göymen Sac Profil (GSP)'), CHAIN)
    expect(fetchLog.capabilityReads().some((c) => c.companyId === B_ID)).toBe(true)
    expect(fetchLog.creates()).toHaveLength(0)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('a write still in flight when the user discards: its late 201 writes nothing into the new company', async () => {
    const user = userEvent.setup()
    const gate = held()
    const fetchLog = installFetch({ create: () => gate.promise })
    const { router } = renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(1))

    await user.selectOptions(companySelect(), B_ID)
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => expect(companySelect().value).toBe(B_ID), CHAIN)
    await screen.findByRole('heading', HEADING, CHAIN)
    await waitFor(() => expect(legalNameInput().value).toBe(''), CHAIN)

    // Delivered, not prevented: the old company's response arrives now.
    gate.release(json(mutationView(), 201))
    await settle(200)
    expect(router.state.location.pathname).toBe('/cari/yeni')
    expect(document.querySelector('[data-outcome]')).toBeNull()
    expect(document.body.textContent).not.toContain('oluşturuldu')
    expect(fetchLog.detailReads()).toHaveLength(0)
    expect(fetchLog.creates()).toHaveLength(1)
    expect(fetchLog.creates()[0].companyId).toBe(A_ID)
    expect(legalNameInput().value).toBe('')
    expect(companySelect().value).toBe(B_ID)
  })

  it('an UNKNOWN attempt discarded with the company is never resent under the new company', async () => {
    const user = userEvent.setup()
    const fetchLog = installFetch({
      create: () => {
        throw new TypeError('Failed to fetch')
      },
    })
    renderAt('/cari/yeni')
    await openCreate()
    await fillValid(user)
    await user.click(submitButton())
    await waitFor(() => query('[data-attempt="unknown"]'))

    await user.selectOptions(companySelect(), B_ID)
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => expect(companySelect().value).toBe(B_ID), CHAIN)
    await screen.findByRole('heading', HEADING, CHAIN)
    await waitFor(() => expect(legalNameInput().value).toBe(''), CHAIN)
    await settle(200)

    expect(document.querySelector('[data-attempt]')).toBeNull()
    expect(fetchLog.creates()).toHaveLength(1)
    expect(fetchLog.creates().filter((c) => c.companyId === B_ID)).toHaveLength(0)
    expect(submitButton().disabled).toBe(false)
  })
})

describe('C-02 the write gate in the real form (spec §4.2, §4.5)', () => {
  /** An OLD node: the probe read succeeds and is valid, but never acknowledges. */
  function silentServer() {
    return installFetch({
      counterparties: () =>
        jsonWithoutAck({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null }),
    })
  }

  it('an unacknowledged server closes the form and NO create is sent', async () => {
    const user = userEvent.setup()
    const fetchLog = silentServer()
    renderAt('/cari/yeni')
    await screen.findByText('Uyumlu istemci gerekli')

    await fillValid(user)
    expect(submitButton().disabled).toBe(true)
    await user.click(submitButton())
    await settle(150)
    expect(fetchLog.creates()).toHaveLength(0)
  })

  it('a probe 403 is a capability failure, not a version problem', async () => {
    // Every probe failure used to read as "incompatible client". A 403 is an
    // authorization fact and must reach the central boundary, not be relabelled.
    installFetch({ counterparties: () => problem(403, PROBLEM_CODE.forbidden) })
    renderAt('/cari/yeni')

    // The central boundary re-fences the capability, so the route itself closes —
    // non-disclosingly, the same way it would for a screen that does not exist.
    // Before this the probe's 403 was stored as a query result and the screen said
    // "incompatible client" while the old shell authority stayed standing.
    await findStateBlock('not-found')
    expect(screen.queryByText('Uyumlu istemci gerekli')).toBeNull()
  })

  it('a probe 503 shows the service failure, not a compatibility verdict', async () => {
    installFetch({ counterparties: () => problem(503, PROBLEM_CODE.serviceUnavailable) })
    renderAt('/cari/yeni')

    await findStateBlock('service-unavailable')
    expect(screen.queryByText('Uyumlu istemci gerekli')).toBeNull()
  })

  it('a probe 401 ends the session instead of blaming the client version', async () => {
    installFetch({ counterparties: () => problem(401, PROBLEM_CODE.authenticationRequired) })
    renderAt('/cari/yeni')

    // The central boundary owns this: the shell reports the ended session.
    await screen.findByText(/Oturum sonlandı/)
    expect(screen.queryByText('Uyumlu istemci gerekli')).toBeNull()
  })

  it('an unacknowledged 201 keeps the attempt in the screen — the recovery never says "reload"', async () => {
    const user = userEvent.setup()
    installFetch({ create: () => jsonWithoutAck(mutationView(), 201) })
    renderAt('/cari/yeni')
    await screen.findByLabelText('Taraf türü')
    await fillValid(user)
    await user.click(submitButton())

    // Unknown, not created — and the recovery offered is the same-key retry that
    // lives on this screen. Telling the user to reload would drop the attempt and
    // its key out of memory, and the next submit could mint a new one.
    const notice = await screen.findByText(/Sunucu bu işlemi onaylamadı/)
    expect(notice.textContent).not.toMatch(/yenile/i)
    expect(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' })).toBeTruthy()
  })
})

describe('C-02 an unknown attempt is re-validated before it is re-sent (spec §4.2, §4.5)', () => {
  async function unknownAttempt(user: ReturnType<typeof userEvent.setup>, config = {}) {
    const fetchLog = installFetch({ create: () => jsonWithoutAck(mutationView(), 201), ...config })
    renderAt('/cari/yeni')
    await screen.findByLabelText('Taraf türü')
    await fillValid(user)
    await user.click(submitButton())
    await screen.findByText(/Sunucu bu işlemi onaylamadı/)
    return fetchLog
  }

  it('re-reads before the second POST — a cached grant is not enough', async () => {
    const user = userEvent.setup()
    const fetchLog = await unknownAttempt(user)
    const readsBefore = fetchLog.calls.filter((c) => c.method === 'GET' && /\/counterparties(\?|$)/.test(c.url)).length

    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(2))

    // A command failure does not revoke readiness, so without this the retry would
    // ride the grant the FIRST send earned and post to a server nobody re-checked.
    const readsAfter = fetchLog.calls.filter((c) => c.method === 'GET' && /\/counterparties(\?|$)/.test(c.url)).length
    expect(readsAfter).toBeGreaterThan(readsBefore)
    // Same attempt, same key.
    expect(fetchLog.creates()[1].headers['Idempotency-Key']).toBe(fetchLog.creates()[0].headers['Idempotency-Key'])
  })

  it('sends NOTHING when the re-validation fails, and keeps the attempt', async () => {
    const user = userEvent.setup()
    let silent = false
    const fetchLog = await unknownAttempt(user, {
      counterparties: () =>
        silent
          ? jsonWithoutAck({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
          : json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null }),
    })

    // The server stops acknowledging between the first send and the retry.
    silent = true
    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await settle(200)

    expect(fetchLog.creates()).toHaveLength(1)
    // The uncertainty — and the key that belongs to it — survive the failed check.
    expect(screen.getByText(/Sunucu bu işlemi onaylamadı/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' })).toBeTruthy()
  })

  it('a compatibility 409 on the retry does NOT let a new key be minted', async () => {
    const user = userEvent.setup()
    let firstSend = true
    const fetchLog = installFetch({
      create: () => {
        if (firstSend) {
          firstSend = false
          return jsonWithoutAck(mutationView(), 201)
        }
        return problem(409, 'client_contract_unsupported')
      },
    })
    renderAt('/cari/yeni')
    await screen.findByLabelText('Taraf türü')
    await fillValid(user)
    await user.click(submitButton())
    await screen.findByText(/Sunucu bu işlemi onaylamadı/)

    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => expect(fetchLog.creates()).toHaveLength(2))

    /**
     * The 409 describes the SECOND request. The first may already have been written,
     * so the attempt stays unresolved under its own key: there is no "Anladım" that
     * clears it to idle and lets the same payload go out under a new one.
     */
    await settle(150)
    expect(screen.getByText(/Sunucu bu işlemi onaylamadı/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Anladım' })).toBeNull()
    expect(fetchLog.creates()[1].headers['Idempotency-Key']).toBe(fetchLog.creates()[0].headers['Idempotency-Key'])
  })
})

describe('C-02 a pending retry check loses to the user (spec §4.5)', () => {
  it('abandoning while the re-check is in flight sends NO second POST', async () => {
    const user = userEvent.setup()
    // The probe is held open so the re-check is genuinely pending when the user acts.
    const probe = held()
    let probeCalls = 0
    const fetchLog = installFetch({
      create: () => jsonWithoutAck(mutationView(), 201),
      counterparties: () => {
        probeCalls += 1
        return probeCalls === 1
          ? json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
          : probe.promise
      },
    })
    renderAt('/cari/yeni')
    await screen.findByLabelText('Taraf türü')
    await fillValid(user)
    await user.click(submitButton())
    await screen.findByText(/Sunucu bu işlemi onaylamadı/)

    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    // …the check has not answered yet, and the user leaves the attempt.
    await user.click(screen.getByRole('button', { name: 'İşlemi bırak' }))

    // The late success must not revive a retry the user withdrew.
    probe.release(json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null }))
    await settle(250)
    expect(fetchLog.creates()).toHaveLength(1)
  })

  it('the compatibility warning never says "no request was sent" once one was', async () => {
    const user = userEvent.setup()
    let probeCalls = 0
    installFetch({
      create: () => jsonWithoutAck(mutationView(), 201),
      counterparties: () => {
        probeCalls += 1
        // The first probe acknowledges (so the form opens); afterwards the server
        // goes silent, which is what makes the compatibility warning appear.
        return probeCalls === 1
          ? json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
          : jsonWithoutAck({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
      },
    })
    renderAt('/cari/yeni')
    await screen.findByLabelText('Taraf türü')
    await fillValid(user)
    await user.click(submitButton())
    await screen.findByText(/Sunucu bu işlemi onaylamadı/)

    // Force the probe to re-run and find the silent server.
    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    const warning = await waitFor(() => query('[data-state="client-contract"]'))

    /**
     * A request WAS sent and its outcome is unknown. Saying "no request was sent"
     * would be false, and "reload the page" would drop the attempt and its key out
     * of this screen's memory — after a POST the server may already have processed.
     */
    expect(warning.textContent).not.toContain('hiçbir istek gönderilmedi')
    expect(warning.textContent).not.toMatch(/Sayfayı yenileyin/)
    expect(warning.textContent).toContain('Sayfayı yenilemeyin')
    expect(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' })).toBeTruthy()
  })
})

describe('C-02 the compatibility warning names only what is on screen (ca19 P2)', () => {
  async function unresolvedThenSilent(user: ReturnType<typeof userEvent.setup>) {
    let probeCalls = 0
    installFetch({
      create: () => jsonWithoutAck(mutationView(), 201),
      counterparties: () => {
        probeCalls += 1
        return probeCalls === 1
          ? json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
          : jsonWithoutAck({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null })
      },
    })
    renderAt('/cari/yeni')
    await screen.findByLabelText('Taraf türü')
    await fillValid(user)
    await user.click(submitButton())
    await screen.findByText(/Sunucu bu işlemi onaylamadı/)
    // The re-check finds a silent server: the compatibility warning appears and the
    // attempt is still unresolved.
    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => query('[data-state="client-contract"]'))
  }

  it('points at the retry button while that button is actually offered', async () => {
    const user = userEvent.setup()
    await unresolvedThenSilent(user)

    const warning = query('[data-state="client-contract"]')
    expect(warning.textContent).toContain('Aynı işlemi yeniden dene')
    expect(screen.getByRole('button', { name: 'Aynı işlemi yeniden dene' })).toBeTruthy()
  })

  it('stops naming it once the edited payload withdraws that offer', async () => {
    const user = userEvent.setup()
    await unresolvedThenSilent(user)

    // Leave the attempt, then edit the legal name. The abandoned box only shows for a
    // payload that REPEATS the attempt, so the retry control disappears — and a
    // warning that still names it sends the user looking for a button that is gone.
    await user.click(screen.getByRole('button', { name: 'İşlemi bırak' }))
    await user.type(legalNameInput(), ' A.Ş.')

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /yeniden dene/i })).toBeNull(),
    )
    const warning = query('[data-state="client-contract"]')
    expect(warning.textContent).not.toMatch(/yeniden dene[”"]? düğmesini/i)
    // What it says instead is true and actionable: the attempt and its key are still
    // held here, a reload would lose them, and the way back to the retry is to
    // restore the values.
    expect(warning.textContent).toContain('Sayfayı yenilemeyin')
    expect(warning.textContent).toContain('eski değerlerine geri getirin')
  })
})
