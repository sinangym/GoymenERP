/**
 * L-02 in the real shell: the rows the summary carries and nothing more, the server-side status
 * filter, the continuation, the detail link only with the detail grant, the create link only with
 * the create grant, a closed route without the grant. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LOGISTICS_CAPABILITY } from './capabilities'
import { INSTRUCTION_LIST_TEXT } from './logisticsText'
import {
  CANCELLED_INSTRUCTION,
  FORBIDDEN_MONEY_WORDS,
  INSTRUCTION,
  READ_ONLY_LOGISTICS,
  installFetch,
  instructionSummary,
  json,
  renderLogisticsApp,
} from './testHarness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('L-02 Yükleme Talimatları', () => {
  it('lists the company’s instructions with the status badge, links the detail, and shows no money word', async () => {
    const harness = installFetch({ onList: () => json({ items: [instructionSummary()], nextCursor: null }) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByTestId(`logistics-row-${INSTRUCTION}`)
    expect(screen.getByTestId(`logistics-row-status-${INSTRUCTION}`).textContent).toBe('Taslak')
    expect(screen.getByRole('link', { name: INSTRUCTION }).getAttribute('href')).toBe(`/lojistik/yukleme-talimatlari/${INSTRUCTION}`)
    expect(screen.getByRole('link', { name: INSTRUCTION_LIST_TEXT.newInstruction })).toBeTruthy()
    expect(screen.getByText(INSTRUCTION_LIST_TEXT.end)).toBeTruthy()
    const listCall = harness.logisticsCalls()[0]
    expect(listCall.url).toBe('/api/v1/logistics/loading-instructions?pageSize=50')
    for (const word of FORBIDDEN_MONEY_WORDS) expect(document.body.textContent).not.toContain(word)
  })

  it('the status filter is the server’s and restarts the list', async () => {
    const urls: string[] = []
    installFetch({
      onList: (url) => {
        urls.push(url)
        return json({ items: url.includes('status=Issued') ? [instructionSummary({ status: 'Issued' })] : [instructionSummary()], nextCursor: null })
      },
    })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByTestId(`logistics-row-${INSTRUCTION}`)
    await userEvent.selectOptions(screen.getByLabelText(INSTRUCTION_LIST_TEXT.filter), 'Issued')
    await waitFor(() => expect(urls.at(-1)).toContain('status=Issued'))
    await waitFor(() => expect(screen.getByTestId(`logistics-row-status-${INSTRUCTION}`).textContent).toBe('Yayımlandı'))
  })

  it('F-020: a cancelled instruction is a row like any other, shown as "İptal edildi", and the filter offers it', async () => {
    const urls: string[] = []
    const other = 'cccccccc-cccc-cccc-cccc-ccccccccccc2'
    installFetch({
      onList: (url) => {
        urls.push(url)
        return json({ items: [instructionSummary(), instructionSummary({ id: other, ...CANCELLED_INSTRUCTION })], nextCursor: null })
      },
    })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByTestId(`logistics-row-${other}`)
    expect(screen.getByTestId(`logistics-row-status-${other}`).textContent).toBe('İptal edildi')
    expect(screen.getByTestId(`logistics-row-status-${INSTRUCTION}`).textContent).toBe('Taslak')
    await userEvent.selectOptions(screen.getByLabelText(INSTRUCTION_LIST_TEXT.filter), 'Cancelled')
    await waitFor(() => expect(urls.at(-1)).toContain('status=Cancelled'))
  })

  it('LOJ-D17: a cancelled row whose loaded goods have no unload weighing carries the "İndirme kaydı yok" mark; no other row does', async () => {
    const missing = 'cccccccc-cccc-cccc-cccc-ccccccccccc2'
    const recorded = 'cccccccc-cccc-cccc-cccc-ccccccccccc3'
    const notLoaded = 'cccccccc-cccc-cccc-cccc-ccccccccccc4'
    installFetch({
      onList: () => json({
        items: [
          instructionSummary(),
          instructionSummary({ id: missing, ...CANCELLED_INSTRUCTION, unloadRecord: 'Missing' }),
          instructionSummary({ id: recorded, ...CANCELLED_INSTRUCTION, unloadRecord: 'Recorded' }),
          instructionSummary({ id: notLoaded, ...CANCELLED_INSTRUCTION }),
        ],
        nextCursor: null,
      }),
    })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByTestId(`logistics-row-${missing}`)
    expect(screen.getByTestId(`logistics-row-unload-missing-${missing}`).textContent).toBe(INSTRUCTION_LIST_TEXT.unloadMissing)
    // The cancel stays valid: the row is still "İptal edildi".
    expect(screen.getByTestId(`logistics-row-status-${missing}`).textContent).toBe('İptal edildi')
    for (const id of [INSTRUCTION, recorded, notLoaded]) expect(screen.queryByTestId(`logistics-row-unload-missing-${id}`)).toBeNull()
  })

  it('LOJ-D17: a live row carrying an unload mark is not shown as a list: the answer is refused whole', async () => {
    installFetch({ onList: () => json({ items: [instructionSummary({ status: 'Issued', unloadRecord: 'Missing' })], nextCursor: null }) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByText(/Beklenmeyen yanıt/)
    expect(screen.queryByTestId(`logistics-row-${INSTRUCTION}`)).toBeNull()
  })

  it('F-020: a Cancelled row without its cancel facts is not shown as a list: the answer is refused whole', async () => {
    installFetch({ onList: () => json({ items: [instructionSummary(), instructionSummary({ id: 'cccccccc-cccc-cccc-cccc-ccccccccccc2', status: 'Cancelled' })], nextCursor: null }) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByText(/Beklenmeyen yanıt/)
    expect(screen.queryByTestId(`logistics-row-${INSTRUCTION}`)).toBeNull()
  })

  it('an unknown status in one row is not shown as a list: the answer is refused whole', async () => {
    installFetch({ onList: () => json({ items: [instructionSummary(), instructionSummary({ id: 'cccccccc-cccc-cccc-cccc-ccccccccccc2', status: 'Voided' })], nextCursor: null }) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByText(/Beklenmeyen yanıt/)
    expect(screen.queryByTestId(`logistics-row-${INSTRUCTION}`)).toBeNull()
  })

  it('a failed next page keeps the rows shown and offers the same cursor again', async () => {
    let pages = 0
    installFetch({
      onList: (url) => {
        pages += 1
        if (!url.includes('cursor=')) return json({ items: [instructionSummary()], nextCursor: 'c2' })
        return pages === 2 ? json({ status: 503, title: 'Service Unavailable' }, 503) : json({ items: [instructionSummary({ id: 'cccccccc-cccc-cccc-cccc-ccccccccccc2' })], nextCursor: null })
      },
    })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByTestId(`logistics-row-${INSTRUCTION}`)
    await userEvent.click(screen.getByRole('button', { name: INSTRUCTION_LIST_TEXT.loadMore }))
    await screen.findByText(INSTRUCTION_LIST_TEXT.nextPageFailed)
    expect(screen.getByTestId(`logistics-row-${INSTRUCTION}`)).toBeTruthy()
  })

  it('without the create grant there is no create link; without the list grant the route is closed', async () => {
    installFetch({ capabilities: READ_ONLY_LOGISTICS, onList: () => json({ items: [instructionSummary()], nextCursor: null }) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await screen.findByTestId(`logistics-row-${INSTRUCTION}`)
    expect(screen.queryByRole('link', { name: INSTRUCTION_LIST_TEXT.newInstruction })).toBeNull()
    cleanup()
    vi.unstubAllGlobals()
    const harness = installFetch({ capabilities: [LOGISTICS_CAPABILITY.instructionsDetail] })
    renderLogisticsApp('/lojistik/yukleme-talimatlari')
    await waitFor(() => expect(harness.calls.some((call) => call.url.includes('/shell/capabilities'))).toBe(true))
    await waitFor(() => expect(screen.queryByRole('heading', { name: INSTRUCTION_LIST_TEXT.heading })).toBeNull())
    expect(harness.logisticsCalls()).toHaveLength(0)
  })
})
