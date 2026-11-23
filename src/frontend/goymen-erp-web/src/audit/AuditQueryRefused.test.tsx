/**
 * F023-5b "Denetim kaydı" — the "Sonuç" filter and refused-attempt rows, through the REAL shell (bootstrap,
 * capability gate, auth boundary). Spec acceptance 15: the "Reddedilen" filter lists only refused attempts with their
 * Turkish reason.
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures shaped like the row the Audit module writes;
 * nothing here is evidence of real API, real SQL or browser acceptance.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { auditEvent, auditReads, installFetch, json, page, refusalJson, refusedAuditEvent, renderAt } from './testHarness'
import { EMPTY_FILTERS, auditListPath } from './capabilities'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const table = () => screen.getByRole('table', { name: 'Denetim olayları' })
const eventRows = () => [...table().querySelectorAll('tr[data-audit-id]')] as HTMLElement[]
const stateOf = async (text: RegExp) => (await screen.findByText(text)).closest('[data-state]')?.getAttribute('data-state')

const CONCURRENCY_REASON = 'Kayıt bu sırada başka bir işlemle değiştiği için reddedildi.'
const VALIDATION_REASON = 'İstek doğrulama kuralına uymadığı için reddedildi.'

describe('Denetim kaydı — "Sonuç" filter (F023-5b)', () => {
  it('applies "Reddedilen" from the form: sends outcome=Refused, keeps it in the URL and lists only refused rows', async () => {
    const succeeded = auditEvent()
    const refused = refusedAuditEvent()
    const recorded = installFetch({
      respond: (url) => json(page(url.searchParams.get('outcome') === 'Refused' ? [refused] : [succeeded, refused])),
    })
    const { router } = renderAt(auditListPath())
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(2))

    await user.selectOptions(screen.getByLabelText('Sonuç'), 'Refused')
    await user.click(screen.getByRole('button', { name: 'Filtrele' }))

    await waitFor(() => expect(router.state.location.search).toBe('?sonuc=reddedilen'))
    await waitFor(() => expect(eventRows()).toHaveLength(1))
    const row = eventRows()[0]
    expect(row.getAttribute('data-outcome')).toBe('Refused')
    expect(row.textContent).toContain('Reddedildi')
    expect(row.textContent).toContain(CONCURRENCY_REASON)
    expect(row.textContent).toContain('Eşzamanlılık · HTTP 409')
    expect(row.textContent).toContain('Sürüm çakışması')
    expect(row.textContent).toContain('3 → —')

    const reads = auditReads(recorded)
    expect(reads.at(-1)?.query.get('outcome')).toBe('Refused')
    expect(reads[0].query.has('outcome')).toBe(false)
    expect(recorded.every((request) => request.method === 'GET')).toBe(true)
    expect((screen.getByLabelText('Sonuç') as HTMLSelectElement).value).toBe('Refused')
  })

  it('reads "Başarılı" from a shared link and marks each row with a text outcome, not colour alone', async () => {
    const recorded = installFetch({ respond: () => json(page([auditEvent()])) })
    renderAt(auditListPath({ ...EMPTY_FILTERS, outcome: 'Succeeded' }))
    await waitFor(() => expect(eventRows()).toHaveLength(1))
    expect(within(eventRows()[0]).getByText('Başarılı')).toBeTruthy()
    expect(auditReads(recorded)[0].query.get('outcome')).toBe('Succeeded')
  })

  it('shows the invalid-filter state for an unknown outcome in the URL without reading, and offers "Tümü"', async () => {
    const recorded = installFetch({ respond: () => json(page([auditEvent()])) })
    renderAt('/ayarlar/denetim-kaydi?sonuc=Refused')
    expect(await stateOf(/Filtre geçersiz/)).toBe('invalid-filter')
    expect(auditReads(recorded)).toHaveLength(0)
    expect((screen.getByLabelText('Sonuç') as HTMLSelectElement).value).toBe('')
    expect(screen.getByRole('button', { name: 'Filtreyi temizle' })).toBeTruthy()
  })

  it('shows the filtered-empty state when no refused attempt matches', async () => {
    installFetch({ respond: () => json(page([])) })
    renderAt(auditListPath({ ...EMPTY_FILTERS, outcome: 'Refused' }))
    expect(await stateOf(/Sonuç yok/)).toBe('empty-filtered')
  })

  it('refuses the whole page as malformed when a succeeded row comes back under "Reddedilen"', async () => {
    installFetch({ respond: () => json(page([refusedAuditEvent(), auditEvent()])) })
    renderAt(auditListPath({ ...EMPTY_FILTERS, outcome: 'Refused' }))
    expect(await stateOf(/Beklenmeyen yanıt/)).toBe('malformed-response')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('refuses the whole page as malformed when a refused row carries no valid refusal payload', async () => {
    installFetch({ respond: () => json(page([auditEvent(), refusedAuditEvent({ afterJson: '{"status":"Approved"}' })])) })
    renderAt(auditListPath())
    expect(await stateOf(/Beklenmeyen yanıt/)).toBe('malformed-response')
    expect(document.body.textContent).not.toContain('SENTETIK-SO')
  })
})

describe('Denetim kaydı — refusal detail (F023-5b)', () => {
  it('lists the refusal class, problem code, HTTP status and field errors inside "Ayrıntı"', async () => {
    const refused = refusedAuditEvent({
      afterJson: refusalJson({
        httpStatus: 400,
        problemCode: 'validation_failed',
        refusalClass: 'Validation',
        fieldErrors: [
          { field: 'lines[0].quantity', code: 'out_of_range' },
          { field: 'expectedVersion', code: 'expected_version_invalid' },
        ],
      }),
      reason: VALIDATION_REASON,
    })
    installFetch({ respond: () => json(page([refused])) })
    renderAt(auditListPath({ ...EMPTY_FILTERS, outcome: 'Refused' }))
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(1))
    expect(eventRows()[0].textContent).toContain(VALIDATION_REASON)
    expect(eventRows()[0].textContent).toContain('Doğrulama · HTTP 400')

    await user.click(screen.getByText('Ayrıntı'))

    expect(await screen.findByRole('heading', { name: 'Ret ayrıntısı', level: 3 })).toBeTruthy()
    const errors = screen.getByRole('list', { name: 'Alan hataları' })
    const items = within(errors).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0].textContent).toContain('lines[0].quantity')
    expect(items[0].textContent).toContain('out_of_range')
    expect(items[1].textContent).toContain('Beklenen sürüm geçersiz')
    expect(screen.getByText('HTTP durumu')).toBeTruthy()
  })

  it('says when a refusal recorded no field error', async () => {
    installFetch({ respond: () => json(page([refusedAuditEvent()])) })
    renderAt(auditListPath({ ...EMPTY_FILTERS, outcome: 'Refused' }))
    const user = userEvent.setup()
    await waitFor(() => expect(eventRows()).toHaveLength(1))
    await user.click(screen.getByText('Ayrıntı'))
    expect(await screen.findByText('Alan hatası kaydedilmedi.')).toBeTruthy()
  })

  it('shows an F023-4 summary row as "N kez reddedildi (pencere: …)"', async () => {
    const summary = refusedAuditEvent({
      afterJson: JSON.stringify({
        count: 12,
        windowStartUtc: '2026-11-01T09:00:00+00:00',
        windowEndUtc: '2026-11-01T10:00:00+00:00',
        refusalClass: 'Idempotency',
        problemCode: 'same_key_payload_mismatch',
      }),
    })
    installFetch({ respond: () => json(page([summary])) })
    renderAt(auditListPath({ ...EMPTY_FILTERS, outcome: 'Refused' }))
    await waitFor(() => expect(eventRows()).toHaveLength(1))
    const row = eventRows()[0]
    expect(row.textContent).toContain('12 kez reddedildi (pencere: 01.11.2026 12:00:00 – 01.11.2026 13:00:00)')
    expect(row.textContent).toContain('Aynı işlem anahtarıyla farklı istek')
  })
})
