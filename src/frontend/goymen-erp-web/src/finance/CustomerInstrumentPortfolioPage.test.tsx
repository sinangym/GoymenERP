// @vitest-environment jsdom

import { cleanup, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, useRoutes } from 'react-router'
import { FINANCE_CAPABILITY, FINANCE_ROUTE, portfolioInstrumentPath } from './capabilities'
import { financeRoutes } from './financeRoutes'
import { detailBody, instrumentBody, INSTRUMENT_ID } from './customerInstrumentFixtures'
import { PORTFOLIO_TITLE } from './customerInstrumentText'
import { COMPANY_A, renderFinance, shellFor } from './testHarness'

function Routes() {
  return useRoutes(financeRoutes)
}

function renderRoute(capabilities: readonly string[], entry: string = FINANCE_ROUTE.customerInstrumentsList) {
  return renderFinance(
    <MemoryRouter initialEntries={[entry]}><Routes /></MemoryRouter>,
    { port: null, shell: shellFor(COMPANY_A, 1, capabilities) },
  )
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('CustomerInstrumentPortfolioPage route', () => {
  it('lists and reopens one instrument with its own read capability and no receipt capability', async () => {
    const fetchMock = vi.fn(async (request: RequestInfo | URL) => {
      const url = String(request)
      if (url.endsWith('/api/v1/shell/capabilities')) {
        return new Response(JSON.stringify({
          contractVersion: 'SHELL-CAPABILITY-V1',
          generation: 'instrument-reader-generation',
          capabilities: [FINANCE_CAPABILITY.customerInstrumentsList],
        }), { status: 200 })
      }
      if (url.endsWith(`/finance/customer-instruments/${INSTRUMENT_ID}`)) {
        return new Response(JSON.stringify(detailBody(instrumentBody(), [])), { status: 200 })
      }
      if (url.includes('/finance/customer-instruments/risk?')) return new Response(null, { status: 403 })
      if (url.endsWith('/finance/customer-instruments')) {
        return new Response(JSON.stringify({ companyId: COMPANY_A, items: [instrumentBody()], nextCursor: null }),
          { status: 200 })
      }
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    renderRoute([FINANCE_CAPABILITY.customerInstrumentsList], portfolioInstrumentPath(INSTRUMENT_ID))

    await screen.findByRole('heading', { name: PORTFOLIO_TITLE })
    await screen.findByRole('region', { name: 'Müşteri çeki · SENTETIK-CEK-1' })
    expect(screen.queryByRole('button', { name: 'Tahsil edildi olarak kaydet' })).toBeNull()
    expect(fetchMock.mock.calls.some(([request]) => String(request).includes('/finance/manual-receipts'))).toBe(false)
  })

  it('does not substitute receipt-list access for the instrument read route', async () => {
    const fetchMock = vi.fn(async (request: RequestInfo | URL) => {
      const url = String(request)
      if (url.endsWith('/api/v1/shell/capabilities')) {
        return new Response(JSON.stringify({
          contractVersion: 'SHELL-CAPABILITY-V1',
          generation: 'receipt-reader-generation',
          capabilities: [FINANCE_CAPABILITY.receiptsList],
        }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    renderRoute([FINANCE_CAPABILITY.receiptsList])

    await waitFor(() => expect(screen.queryByRole('heading', { name: PORTFOLIO_TITLE })).toBeNull())
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(fetchMock.mock.calls.some(([request]) => String(request).includes('/finance/customer-instruments'))).toBe(false)
  })
})
