/**
 * "Denetim kayıtları" component cases.
 *
 * COMPONENT/DOM EVIDENCE ONLY: the harness stubs `fetch` with synthetic fixtures, so nothing here is evidence of real
 * API, real SQL or browser acceptance. What these cases establish: default-deny (no capability → no read), every
 * read is a GET naming the active company, filters and cursor travel through the URL to the server's own query
 * parameters, the hidden fields never reach the DOM, and each failure has its own state.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  COMPANY_A,
  SITE_ID,
  auditItem,
  auditPage,
  auditReads,
  installFetch,
  json,
  problem,
  renderAt,
} from './testHarness'
import { auditEventsPath } from './capabilities'

afterEach(() => {
  vi.unstubAllGlobals()
})

const table = () => screen.getByRole('table', { name: 'Denetim kayıtları' })

function onAudit(answer: (url: URL) => Response) {
  return (raw: string) => (raw.includes('/audit/events') ? answer(new URL(raw, 'http://localhost')) : undefined)
}

describe('Denetim kayıtları — access', () => {
  it('without the capability shows the neutral not-found block and sends no audit read', async () => {
    const recorded = installFetch({ capabilities: [], respond: onAudit(() => json(auditPage([auditItem()]))) })
    renderAt(auditEventsPath())
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByRole('heading', { name: 'Denetim kayıtları', level: 1 })).toBeNull()
    expect(auditReads(recorded)).toHaveLength(0)
  })

  it('a 403 from the server goes to the shell auth boundary and closes the screen, never an empty list', async () => {
    const recorded = installFetch({ respond: onAudit(() => problem(403)) })
    renderAt(auditEventsPath())
    await waitFor(() => expect(auditReads(recorded).length).toBeGreaterThan(0))
    // The boundary re-judges the grant; whichever closed block it lands on, it is never the empty or ready view.
    await waitFor(() =>
      expect(document.querySelector('[data-state="forbidden"], [data-state="not-found"]')).not.toBeNull(),
    )
    expect(document.querySelector('[data-state^="empty"]')).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('Denetim kayıtları — list', () => {
  it('shows the minimal fields, newest first as given, and never the hidden ones', async () => {
    const recorded = installFetch({
      respond: onAudit(() =>
        json(
          auditPage([
            auditItem({ eventType: 'organization.site.updated', aiInvolvement: 'Proposed' }),
            auditItem({ eventType: 'organization.site.created', entityVersionBefore: null, entityVersionAfter: 1, actorType: 'Robot' }),
          ]),
        ),
      ),
    })
    renderAt(auditEventsPath())

    expect(await screen.findByRole('heading', { name: 'Denetim kayıtları', level: 1 })).toBeTruthy()
    await waitFor(() => expect(within(table()).getAllByRole('row')).toHaveLength(3))
    const rows = within(table()).getAllByRole('row').slice(1)
    expect(rows[0].textContent).toContain('organization.site.updated')
    expect(rows[0].textContent).toContain('3 → 4')
    expect(rows[0].textContent).toContain('Kullanıcı')
    expect(rows[0].textContent).toContain('Önerdi')
    expect(rows[0].textContent).toContain('Başarılı')
    expect(rows[1].textContent).toContain('— → 1')
    // An unknown actor token is shown verbatim, never guessed.
    expect(rows[1].textContent).toContain('Robot')
    expect(within(rows[0]).getByText('24.10.2026 12:15:30')).toBeTruthy()

    for (const hidden of ['GİZLİ', '203.0.113.7', 'sha256:abc', 'ORG_ADMIN']) {
      expect(document.body.textContent).not.toContain(hidden)
    }
    expect(screen.getByText('Daha eski kayıt yok.')).toBeTruthy()

    const reads = auditReads(recorded)
    expect(reads).toHaveLength(1)
    expect(reads[0].method).toBe('GET')
    expect(reads[0].headers['X-Company-Id']).toBe(COMPANY_A)
    // No paging policy of its own: no pageSize is sent.
    expect(new URL(reads[0].url, 'http://localhost').search).toBe('')
    expect(recorded.every((request) => request.method === 'GET')).toBe(true)
  })

  it('pages to older records with the server cursor and back to the newest', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({
      respond: onAudit((url) =>
        url.searchParams.get('cursor') === 'next-1'
          ? json(auditPage([auditItem({ eventType: 'older.event' })]))
          : json(auditPage([auditItem({ eventType: 'newest.event' })], 'next-1')),
      ),
    })
    const { router } = renderAt(auditEventsPath())

    await user.click(await screen.findByRole('link', { name: 'Daha eski kayıtlar' }))
    await waitFor(() => expect(table().textContent).toContain('older.event'))
    expect(router.state.location.search).toBe('?imlec=next-1')
    expect(new URL(auditReads(recorded).at(-1)!.url, 'http://localhost').searchParams.get('cursor')).toBe('next-1')

    await user.click(screen.getByRole('link', { name: 'En yeni kayıtlara dön' }))
    await waitFor(() => expect(table().textContent).toContain('newest.event'))
    expect(router.state.location.search).toBe('')
  })

  it('filters through the URL and starts again from the newest page', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({
      respond: onAudit((url) =>
        url.searchParams.has('correlationId')
          ? json(auditPage([]))
          : json(auditPage([auditItem()], 'next-1')),
      ),
    })
    const { router } = renderAt(auditEventsPath({ cursor: 'next-1' }))

    await user.type(await screen.findByLabelText('Korelasyon kimliği'), '  corr-9  ')
    await user.click(screen.getByRole('button', { name: 'Süz' }))

    await waitFor(() => expect(document.querySelector('[data-state="empty-filtered"]')).not.toBeNull())
    expect(router.state.location.search).toBe('?korelasyon=corr-9')
    const sent = new URL(auditReads(recorded).at(-1)!.url, 'http://localhost').searchParams
    expect(sent.get('correlationId')).toBe('corr-9')
    expect(sent.has('cursor')).toBe(false)

    await user.click(screen.getByRole('link', { name: 'Süzmeyi kaldır' }))
    await waitFor(() => expect(router.state.location.search).toBe(''))
  })

  it("follows a row's record to its own history", async () => {
    const user = userEvent.setup()
    const recorded = installFetch({ respond: onAudit(() => json(auditPage([auditItem()]))) })
    const { router } = renderAt(auditEventsPath())

    await user.click(await screen.findByRole('link', { name: `Bu kaydın geçmişi: Organization.Site ${SITE_ID}` }))
    await waitFor(() =>
      expect(router.state.location.search).toBe(`?varlikTipi=Organization.Site&varlikId=${SITE_ID}`),
    )
    await waitFor(() => {
      const sent = new URL(auditReads(recorded).at(-1)!.url, 'http://localhost').searchParams
      expect(sent.get('entityType')).toBe('Organization.Site')
      expect(sent.get('entityId')).toBe(SITE_ID)
    })
    expect((screen.getByLabelText('Kayıt tipi') as HTMLInputElement).value).toBe('Organization.Site')
  })

  it('refuses an over-long filter in the form and sends nothing', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({ respond: onAudit(() => json(auditPage([auditItem()]))) })
    renderAt(auditEventsPath())
    const input = await screen.findByLabelText('Kayıt kimliği')
    await waitFor(() => expect(auditReads(recorded)).toHaveLength(1))

    await user.click(input)
    await user.paste('x'.repeat(129))
    await user.click(screen.getByRole('button', { name: 'Süz' }))

    expect(await screen.findByText('En çok 128 karakter.')).toBeTruthy()
    expect(input.getAttribute('aria-invalid')).toBe('true')
    expect(auditReads(recorded)).toHaveLength(1)
  })

  it('an over-long value in the URL is dropped, never sent', async () => {
    const recorded = installFetch({ respond: onAudit(() => json(auditPage([auditItem()]))) })
    renderAt(`${auditEventsPath()}?varlikId=${'x'.repeat(129)}`)
    await waitFor(() => expect(auditReads(recorded)).toHaveLength(1))
    expect(new URL(auditReads(recorded)[0].url, 'http://localhost').search).toBe('')
  })
})

describe('Denetim kayıtları — states', () => {
  it('shows the no-data state for an empty unfiltered page', async () => {
    installFetch({ respond: onAudit(() => json(auditPage([]))) })
    renderAt(auditEventsPath())
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
  })

  it('an expired cursor (400) offers the first page of the same filter', async () => {
    const user = userEvent.setup()
    installFetch({
      respond: onAudit((url) => (url.searchParams.has('cursor') ? problem(400) : json(auditPage([auditItem()])))),
    })
    const { router } = renderAt(auditEventsPath({ entityType: 'Organization.Site', cursor: 'stale' }))
    await waitFor(() => expect(document.querySelector('[data-state="invalid-query"]')).not.toBeNull())
    await user.click(screen.getByRole('link', { name: 'İlk sayfayı aç' }))
    await waitFor(() => expect(router.state.location.search).toBe('?varlikTipi=Organization.Site'))
    await waitFor(() => expect(within(table()).getAllByRole('row')).toHaveLength(2))
  })

  it('a malformed page fails closed with no rows', async () => {
    installFetch({ respond: onAudit(() => json(auditPage([{ ...auditItem(), extra: true }]))) })
    renderAt(auditEventsPath())
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('a 503 is the unavailable state, and retry reads again', async () => {
    const user = userEvent.setup()
    let calls = 0
    const recorded = installFetch({
      respond: onAudit(() => {
        calls += 1
        return calls === 1 ? problem(503) : json(auditPage([auditItem()]))
      }),
    })
    renderAt(auditEventsPath())
    await waitFor(() => expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(within(table()).getAllByRole('row')).toHaveLength(2))
    expect(auditReads(recorded)).toHaveLength(2)
  })
})
