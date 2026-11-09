/**
 * UI-K02 "Denetim kaydı" — component cases through the REAL shell (bootstrap, capability gate, auth boundary).
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures; nothing here is evidence of real API, real
 * SQL or browser acceptance. What these cases establish: what is shown and what is never shown (client IP, user
 * agent, unmasked payloads), what is sent (one GET with the active company, the page size, the exact filters and the
 * opaque cursor — never an over-long filter), and the paging rules.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, COMPANY_B, auditEvent, auditReads, installFetch, json, page, problem, renderAt } from './testHarness'
import { auditListPath } from './capabilities'
import { MASK } from './mask'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const table = () => screen.getByRole('table', { name: 'Denetim olayları' })
const eventRows = () => [...table().querySelectorAll('tr[data-audit-id]')]

describe('Denetim kaydı — list, privacy and details', () => {
  it('lists the events with every column, sends one GET for the active company and never shows IP or user agent', async () => {
    const event = auditEvent({ reason: 'SENTETIK onay gerekçesi', correlationId: 'SENTETIK-corr-x' })
    const recorded = installFetch({ respond: () => json(page([event])) })
    renderAt(auditListPath())

    expect(await screen.findByRole('heading', { name: 'Denetim kaydı', level: 1 })).toBeTruthy()
    await waitFor(() => expect(eventRows()).toHaveLength(1))
    const row = eventRows()[0] as HTMLElement
    for (const text of [
      'SalesOrderApproved',
      'SalesOrder',
      String(event.entityId),
      'Update',
      '3 → 4',
      'User',
      'SENTETIK-user-1',
      'sales.orders.approve',
      'SENTETIK onay gerekçesi',
      'SENTETIK-corr-x',
    ]) {
      expect(row.textContent, text).toContain(text)
    }
    expect(within(row).getByText('01.11.2026 12:30:00')).toBeTruthy()
    expect(screen.getByText(/IP adresi ve tarayıcı bilgisi gösterilmez/)).toBeTruthy()
    expect(document.body.textContent).not.toContain('203.0.113.77')
    expect(document.body.textContent).not.toContain('SENTETIK-Agent')

    const reads = auditReads(recorded)
    expect(reads).toHaveLength(1)
    expect(reads[0].method).toBe('GET')
    expect(reads[0].headers['X-Company-Id']).toBe(COMPANY_A)
    expect([...reads[0].query.keys()]).toEqual(['pageSize'])
    expect(recorded.every((request) => request.method === 'GET')).toBe(true)
  })

  it('opens a row disclosure with the masked before, after and approval payloads', async () => {
    const event = auditEvent({
      beforeJson: JSON.stringify({ iban: 'TR330006100519786457841326', vkn: '1234567890', status: 'Draft' }),
      afterJson: JSON.stringify({ contact: { email: 'kisi@sentetik.invalid', phone: '+90 555 000 00 00' }, status: 'Approved' }),
      approvalContextJson: JSON.stringify({ note: 'IBAN TR33 0006 1005 1978 6457 8413 26 kontrol edildi' }),
    })
    installFetch({ respond: () => json(page([event, auditEvent({ beforeJson: 'not json' })])) })
    renderAt(auditListPath())
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(2))

    // Nothing of a payload is in the document before its disclosure is opened.
    expect(document.body.textContent).not.toContain('Draft')
    const [first, second] = screen.getAllByText('Ayrıntı')
    await user.click(first)

    const before = await screen.findByLabelText('Önceki değer')
    expect(before.textContent).toContain(`"iban": "${MASK}1326"`)
    expect(before.textContent).toContain(`"vkn": "${MASK}7890"`)
    expect(before.textContent).toContain('"status": "Draft"')
    const after = screen.getByLabelText('Sonraki değer')
    expect(after.textContent).toContain(`"email": "${MASK}"`)
    expect(after.textContent).toContain(`"phone": "${MASK}"`)
    const approval = screen.getByLabelText('Onay bağlamı')
    expect(approval.textContent).toContain(`IBAN ${MASK}1326 kontrol edildi`)
    for (const secret of ['0006100519786457841326', '1234567890', 'kisi@sentetik.invalid', '555 000', '0006 1005']) {
      expect(document.body.textContent).not.toContain(secret)
    }

    await user.click(second)
    expect(await screen.findByText(/JSON olarak okunamadı/)).toBeTruthy()
    expect(screen.getByText('Onay bağlamı yok.')).toBeTruthy()
  })

  it('treats an item of another company as a malformed answer and shows no row', async () => {
    installFetch({ respond: () => json(page([auditEvent(), auditEvent({ companyId: COMPANY_B })])) })
    renderAt(auditListPath())
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('mounts nothing and reads nothing without the capability', async () => {
    const recorded = installFetch({ capabilities: ['identity.assignments.list'], respond: () => json(page([auditEvent()])) })
    renderAt(auditListPath())
    expect(await screen.findByText(/Bulunamadı/)).toBeTruthy()
    expect(auditReads(recorded)).toHaveLength(0)
    expect(screen.queryByRole('heading', { name: 'Denetim kaydı' })).toBeNull()
  })
})

describe('Denetim kaydı — filters', () => {
  it('tells an empty trail apart from a filter that excluded everything', async () => {
    installFetch({ respond: () => json(page([])) })
    renderAt(auditListPath())
    expect((await screen.findByText(/Kayıt yok/)).closest('[data-state]')?.getAttribute('data-state')).toBe('empty-no-data')
    cleanup()

    const recorded = installFetch({ respond: () => json(page([])) })
    renderAt(auditListPath({ correlationId: 'SENTETIK-corr-none', entityType: '', entityId: '' }))
    expect((await screen.findByText(/Sonuç yok/)).closest('[data-state]')?.getAttribute('data-state')).toBe('empty-filtered')
    expect(auditReads(recorded)[0].query.get('correlationId')).toBe('SENTETIK-corr-none')
  })

  it('applies trimmed exact-match filters through the URL and clears them again', async () => {
    const recorded = installFetch({ respond: () => json(page([auditEvent()])) })
    const { router } = renderAt(auditListPath())
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(1))

    await user.type(screen.getByLabelText('Kayıt türü'), '  SalesOrder  ')
    await user.type(screen.getByLabelText('Kayıt kimliği'), ' SENTETIK-SO-1 ')
    await user.click(screen.getByRole('button', { name: 'Filtrele' }))

    await waitFor(() => expect(auditReads(recorded)).toHaveLength(2))
    expect(router.state.location.search).toBe('?kayitTuru=SalesOrder&kayitNo=SENTETIK-SO-1')
    const filtered = auditReads(recorded)[1].query
    expect(filtered.get('entityType')).toBe('SalesOrder')
    expect(filtered.get('entityId')).toBe('SENTETIK-SO-1')
    expect(filtered.has('correlationId')).toBe(false)
    expect((screen.getByLabelText('Kayıt türü') as HTMLInputElement).value).toBe('SalesOrder')

    await user.click(screen.getByRole('button', { name: 'Filtreyi temizle' }))
    await waitFor(() => expect(auditReads(recorded)).toHaveLength(3))
    expect(router.state.location.search).toBe('')
    expect([...auditReads(recorded)[2].query.keys()]).toEqual(['pageSize'])
  })

  it('refuses an over-long filter in the form and sends nothing', async () => {
    const recorded = installFetch({ respond: () => json(page([auditEvent()])) })
    const { router } = renderAt(auditListPath())
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(1))

    const input = screen.getByLabelText('Korelasyon kimliği')
    await user.click(input)
    await user.paste('x'.repeat(129))
    await user.click(screen.getByRole('button', { name: 'Filtrele' }))

    expect(await screen.findByText('En fazla 128 karakter girilebilir.')).toBeTruthy()
    expect(input.getAttribute('aria-invalid')).toBe('true')
    expect(router.state.location.search).toBe('')
    expect(auditReads(recorded)).toHaveLength(1)
  })

  it('shows the invalid-filter state for an over-long filter in the URL without reading, and for a server 400', async () => {
    const recorded = installFetch({ respond: () => json(page([auditEvent()])) })
    renderAt(`/ayarlar/denetim-kaydi?kayitTuru=${'y'.repeat(161)}`)
    expect(await screen.findByText(/Filtre geçersiz/)).toBeTruthy()
    expect(auditReads(recorded)).toHaveLength(0)
    cleanup()

    installFetch({ respond: () => problem(400) })
    renderAt(auditListPath({ correlationId: 'SENTETIK', entityType: '', entityId: '' }))
    expect((await screen.findByText(/Filtre geçersiz/)).closest('[data-state]')?.getAttribute('data-state')).toBe('invalid-filter')
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('Denetim kaydı — paging', () => {
  it('appends the next page by its opaque cursor, without repeating an id, until the end', async () => {
    const first = auditEvent()
    const second = auditEvent()
    const third = auditEvent()
    const recorded = installFetch({
      respond: (url) =>
        url.searchParams.get('cursor') === 'SENTETIK+cursor/1'
          ? json(page([second, third], null))
          : json(page([first, second], 'SENTETIK+cursor/1')),
    })
    renderAt(auditListPath({ correlationId: '', entityType: 'SalesOrder', entityId: '' }))
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(2))
    expect(screen.queryByText('Listenin sonu.')).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Daha fazla' }))

    await waitFor(() => expect(eventRows()).toHaveLength(3))
    expect(eventRows().map((row) => row.getAttribute('data-audit-id'))).toEqual([first.id, second.id, third.id])
    expect(screen.getByText('3 olay gösteriliyor.')).toBeTruthy()
    expect(screen.getByText('Listenin sonu.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Daha fazla' })).toBeNull()
    const next = auditReads(recorded)[1].query
    expect(next.get('cursor')).toBe('SENTETIK+cursor/1')
    expect(next.get('entityType')).toBe('SalesOrder')
  })

  it('keeps the rows on a failed next page and retries the same cursor', async () => {
    let attempts = 0
    const recorded = installFetch({
      respond: (url) => {
        if (url.searchParams.get('cursor') !== 'SENTETIK-c1') return json(page([auditEvent()], 'SENTETIK-c1'))
        attempts += 1
        return attempts === 1 ? problem(503) : json(page([auditEvent()], null))
      },
    })
    renderAt(auditListPath())
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(1))

    await user.click(screen.getByRole('button', { name: 'Daha fazla' }))
    expect(await screen.findByText(/Sonraki sayfa getirilemedi/)).toBeTruthy()
    expect(eventRows()).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(eventRows()).toHaveLength(2))
    expect(auditReads(recorded).map((request) => request.query.get('cursor'))).toEqual([null, 'SENTETIK-c1', 'SENTETIK-c1'])
  })

  it('restarts from the first page with a message when the server refuses the cursor', async () => {
    let firstReads = 0
    const recorded = installFetch({
      respond: (url) => {
        if (url.searchParams.get('cursor') === 'SENTETIK-expired') return problem(400)
        firstReads += 1
        return json(page([auditEvent()], firstReads === 1 ? 'SENTETIK-expired' : null))
      },
    })
    renderAt(auditListPath())
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(1))

    await user.click(screen.getByRole('button', { name: 'Daha fazla' }))

    expect(await screen.findByText(/liste ilk sayfadan yeniden açıldı/)).toBeTruthy()
    await waitFor(() => expect(eventRows()).toHaveLength(1))
    expect(auditReads(recorded).map((request) => request.query.get('cursor'))).toEqual([null, 'SENTETIK-expired', null])
    expect(screen.getByText('Listenin sonu.')).toBeTruthy()
  })
})
