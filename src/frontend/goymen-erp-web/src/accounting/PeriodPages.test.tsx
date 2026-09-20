/**
 * M-07 period screens — component/DOM tests through the real shell, gate and fence, over SENTETIK
 * fixtures. NOT real-API, real-SQL or browser acceptance, and never reported as such.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  APPROVER_CAPABILITIES,
  PERIOD_SEPTEMBER,
  PREPARER_CAPABILITIES,
  READER_CAPABILITIES,
  installFetch,
  json,
  openPeriod,
  period,
  periodPage,
  problem,
  renderAt,
  requestsFor,
  type RecordedRequest,
} from './testHarness'

afterEach(() => {
  vi.unstubAllGlobals()
})

function stub(options: {
  capabilities: readonly string[]
  reads?: Array<() => Response>
  commands?: Array<() => Response>
}): RecordedRequest[] {
  const reads = options.reads ?? [() => json(periodPage([period()]))]
  const commands = options.commands ?? [() => json(openPeriod(), 200)]
  let readIndex = 0
  let commandIndex = 0
  return installFetch({
    capabilities: options.capabilities,
    respond: (url, init) => {
      if (!url.includes('/accounting/periods')) return undefined
      if ((init?.method ?? 'GET') === 'POST') {
        const respond = commands[Math.min(commandIndex, commands.length - 1)]
        commandIndex += 1
        return respond()
      }
      const respond = reads[Math.min(readIndex, reads.length - 1)]
      readIndex += 1
      return respond()
    },
  })
}

describe('M-07 period list', () => {
  it('shows the preparer’s own proposal with its note and NO approval (Ö4)', async () => {
    stub({ capabilities: PREPARER_CAPABILITIES, reads: [() => json(periodPage([period({ isOwnProposal: true })]))] })
    renderAt('/muhasebe/donemler')
    expect(await screen.findByText('Sizin öneriniz — başka bir yetkili onaylamalı')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Onayla/ })).toBeNull()
    expect(screen.getByRole('link', { name: 'Yeni dönem öner' })).toBeTruthy()
  })

  it('approves another user’s proposal at the version read, then RE-READS the list', async () => {
    const user = userEvent.setup()
    const recorded = stub({
      capabilities: APPROVER_CAPABILITIES,
      reads: [() => json(periodPage([period()])), () => json(periodPage([openPeriod()]))],
    })
    renderAt('/muhasebe/donemler')
    await user.click(await screen.findByRole('button', { name: 'Onayla: 2026-09-01 – 2026-09-30' }))
    await user.click(screen.getByRole('button', { name: 'Evet, onayla' }))

    expect(await screen.findByText('Dönem açıldı. Liste sunucudan yeniden okundu.')).toBeTruthy()
    const posts = requestsFor(recorded, `/accounting/periods/${PERIOD_SEPTEMBER}/approval`, 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0].body).toEqual({ expectedVersion: 1 })
    expect(posts[0].headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    await waitFor(() => expect(requestsFor(recorded, '/accounting/periods', 'GET')).toHaveLength(2))
    expect(await screen.findByText('Açık')).toBeTruthy()
  })

  it('freezes on an unknown outcome and re-sends the SAME key and body', async () => {
    const user = userEvent.setup()
    const recorded = stub({
      capabilities: APPROVER_CAPABILITIES,
      commands: [() => problem(503, { code: 'service_unavailable' }), () => json(openPeriod(), 200)],
    })
    renderAt('/muhasebe/donemler')
    await user.click(await screen.findByRole('button', { name: 'Onayla: 2026-09-01 – 2026-09-30' }))
    await user.click(screen.getByRole('button', { name: 'Evet, onayla' }))

    expect(await screen.findByText('Sonuç bilinmiyor')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Onayla: 2026-09-01 – 2026-09-30' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden gönder' }))

    await waitFor(() => expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(2))
    const [first, second] = requestsFor(recorded, '/approval', 'POST')
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('offers a reader no command and no propose link', async () => {
    const recorded = stub({ capabilities: READER_CAPABILITIES, reads: [() => json(periodPage([period(), openPeriod({ periodId: 'a1000000-0000-4000-8000-000000000007', startsOn: '2026-08-01', endsOn: '2026-08-31' })]))] })
    renderAt('/muhasebe/donemler')
    expect(await screen.findByText('Önerildi — onay bekliyor')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Onayla|Kapat/ })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Yeni dönem öner' })).toBeNull()
    expect(requestsFor(recorded, '/accounting/periods', 'POST')).toHaveLength(0)
  })

  it('closes an open period after a confirm step', async () => {
    const user = userEvent.setup()
    const recorded = stub({
      capabilities: APPROVER_CAPABILITIES,
      reads: [() => json(periodPage([openPeriod()]))],
      commands: [() => json(openPeriod({ status: 'Closed', version: 3 }), 200)],
    })
    renderAt('/muhasebe/donemler')
    await user.click(await screen.findByRole('button', { name: 'Kapat: 2026-09-01 – 2026-09-30' }))
    await user.click(screen.getByRole('button', { name: 'Evet, kapat' }))
    await waitFor(() => expect(requestsFor(recorded, '/close', 'POST')).toHaveLength(1))
    expect(requestsFor(recorded, '/close', 'POST')[0].body).toEqual({ expectedVersion: 2 })
  })
})

describe('M-07 period proposal', () => {
  it('mints one period id and key per snapshot and re-sends both unchanged after an unknown', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({
      capabilities: PREPARER_CAPABILITIES,
      respond: (url, init) => {
        if (!url.endsWith('/accounting/periods') || init?.method !== 'POST') return undefined
        return requestsFor(recorded, '/accounting/periods', 'POST').length === 1
          ? problem(503, { code: 'service_unavailable' })
          : json(period({ isOwnProposal: true }), 201)
      },
    })
    renderAt('/muhasebe/donemler/yeni')
    await user.type(await screen.findByLabelText('Başlangıç'), '2026-09-01')
    await user.type(screen.getByLabelText('Bitiş'), '2026-09-30')
    await user.click(screen.getByRole('button', { name: 'Öner' }))

    expect(await screen.findByText('Sonuç bilinmiyor')).toBeTruthy()
    expect((screen.getByLabelText('Başlangıç') as HTMLInputElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden gönder' }))

    expect(await screen.findByText('Dönem önerildi; açılması için başka bir yetkili onaylamalı.')).toBeTruthy()
    const [first, second] = requestsFor(recorded, '/accounting/periods', 'POST')
    expect(first.body).toMatchObject({ startsOn: '2026-09-01', endsOn: '2026-09-30' })
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('refuses reversed dates locally and sends nothing', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({ capabilities: PREPARER_CAPABILITIES })
    renderAt('/muhasebe/donemler/yeni')
    await user.type(await screen.findByLabelText('Başlangıç'), '2026-09-30')
    await user.type(screen.getByLabelText('Bitiş'), '2026-09-01')
    await user.click(screen.getByRole('button', { name: 'Öner' }))
    expect(await screen.findByText('Bitiş tarihi başlangıçtan önce olamaz.')).toBeTruthy()
    expect(requestsFor(recorded, '/accounting/periods', 'POST')).toHaveLength(0)
  })

  it('shows an overlap by its code', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: PREPARER_CAPABILITIES,
      respond: (url, init) =>
        url.endsWith('/accounting/periods') && init?.method === 'POST' ? problem(409, { code: 'accounting_period_overlap' }) : undefined,
    })
    renderAt('/muhasebe/donemler/yeni')
    await user.type(await screen.findByLabelText('Başlangıç'), '2026-09-01')
    await user.type(screen.getByLabelText('Bitiş'), '2026-09-30')
    await user.click(screen.getByRole('button', { name: 'Öner' }))
    expect(await screen.findByText('Bu tarihler şirketin kayıtlı bir dönemiyle çakışıyor.')).toBeTruthy()
  })
})
