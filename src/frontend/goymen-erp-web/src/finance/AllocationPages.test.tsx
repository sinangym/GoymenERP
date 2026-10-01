// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { AllocationPage } from './AllocationPages'
import { FINANCE_CAPABILITY, FINANCE_ROUTE } from './capabilities'
import { COMPANY_A, renderFinance, shellFor } from './testHarness'

const ID = '22222222-2222-4222-8222-222222222222'
const SOURCE = '33333333-3333-4333-8333-333333333333'
const ITEM = '44444444-4444-4444-8444-444444444444'
const CURRENCY = '55555555-5555-4555-8555-555555555555'

const allocation = {
  id: ID, companyId: COMPANY_A, sourceId: SOURCE, openItemId: ITEM, currencyId: CURRENCY,
  amount: '125.50', status: 'Active', version: '1', reason: 'SENTETIK dağıtım', createdBy: 'maker',
  createdAtUtc: '2026-09-30T10:00:00Z', releasedBy: null, releasedAtUtc: null, releaseReason: null,
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('AllocationPage', () => {
  it('lets an allocation-read-only actor list and reopen without receipt capabilities', async () => {
    const fetchMock = vi.fn(async (request: RequestInfo | URL) => {
      const url = String(request)
      const body = url.endsWith(`/${ID}`)
        ? { allocation, openItemVersion: '7' }
        : { companyId: COMPANY_A, items: [allocation], windowFull: false }
      return new Response(JSON.stringify(body), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(
      <MemoryRouter initialEntries={[FINANCE_ROUTE.allocationsList]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsList]) },
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Aç' }))
    expect((await screen.findByRole('region', { name: 'Dağıtım detayı' })).textContent).toContain(ID)
    expect(screen.queryByRole('button', { name: 'Dağıtımı bırak' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Kapat' }))
    expect(screen.queryByRole('region', { name: 'Dağıtım detayı' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Aç' }))
    expect((await screen.findByRole('region', { name: 'Dağıtım detayı' })).textContent).toContain(ID)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
  })

  it('keeps a forbidden read fail-closed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })))
    renderFinance(
      <MemoryRouter initialEntries={[FINANCE_ROUTE.allocationsList]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsList]) },
    )
    await screen.findByText('Bu içeriğe erişim yetkiniz bulunmuyor.')
    expect(screen.queryByText(SOURCE)).toBeNull()
  })
})
