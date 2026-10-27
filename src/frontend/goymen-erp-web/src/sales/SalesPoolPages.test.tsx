/**
 * S-01 Talep/Teklif Havuzu screens (UI-20) in the real shell, against wire bodies shaped like #824/#828's schemas.
 * Component evidence, NOT_ENV: the screen tests use TEST capability ids; the production map (the ids Sales shell 1.8
 * publishes) is pinned in the first block.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SALES_POOL_CAPABILITY } from './poolCapabilities'
import { SALES_POOL_CAPABILITY_ID } from './capabilities'
import { POOL_TEXT } from './poolText'
import { resetPendingPoolAttempts } from './pendingPoolAttempts'
import {
  ALL_POOL,
  COMPANY_A,
  CURRENCY,
  KG,
  ORDER_ID,
  OTHER_PARTY,
  PRODUCT,
  QUOTE_ID,
  QUOTE_LINE,
  REQUEST_2,
  REQUEST_ID,
  installPoolFetch,
  json,
  orderMutation,
  quoteDetail,
  quoteLine,
  quoteMutation,
  requestDetail,
  requestMutation,
  requestSummary,
  type PoolCall,
} from './poolFixtures'
import { renderPool } from './poolTestHarness'

afterEach(() => {
  cleanup()
  resetPendingPoolAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const notFound = (call: PoolCall) => {
  throw new Error(`unexpected ${call.method} ${call.url}`)
}

describe('the production map: the ids the backend publishes (Sales shell 1.8)', () => {
  it('maps every pool key onto exactly its published id', () => {
    expect(SALES_POOL_CAPABILITY).toEqual({
      requestsList: ['sales.requests.list'],
      requestsDetail: ['sales.requests.detail'],
      requestsCreate: ['sales.requests.create'],
      requestsDecide: ['sales.requests.approve'],
      quotesList: ['sales.quotes.list'],
      quotesDetail: ['sales.quotes.detail'],
      quotesCreate: ['sales.quotes.create'],
      quotesWrite: ['sales.quotes.write'],
      quotesConvert: ['sales.quotes.convert'],
    })
  })

  it('closes every pool route onto the neutral block and calls no Sales read when the shell grants none of them', async () => {
    const fetch = installPoolFetch(notFound, ALL_POOL)
    for (const path of ['/satis/talepler', '/satis/talepler/yeni', `/satis/teklifler/${QUOTE_ID}`]) {
      renderPool(path, SALES_POOL_CAPABILITY)
      expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
      cleanup()
    }
    expect(fetch.calls).toHaveLength(0)
  })

  it('opens the request list for the published list id and nothing it does not grant', async () => {
    installPoolFetch((call) => {
      if (call.method === 'GET' && call.url.includes('/sales/requests?')) return json({ items: [requestSummary()], nextCursor: null })
      return notFound(call)
    }, [SALES_POOL_CAPABILITY_ID.requestsList])
    renderPool('/satis/talepler', SALES_POOL_CAPABILITY)
    expect(await screen.findByRole('table', { name: POOL_TEXT.requestList.caption })).toBeTruthy()
    expect(screen.queryByRole('link', { name: POOL_TEXT.requestList.create })).toBeNull()
    cleanup()

    renderPool('/satis/teklifler', SALES_POOL_CAPABILITY)
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
  })

  it('an ungranted test id closes the route as well', async () => {
    installPoolFetch(notFound, [])
    renderPool('/satis/teklifler')
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
  })
})

describe('Talepler', () => {
  it('lists the requests under the company, filters on the server, and starts a quote from rows of ONE customer', async () => {
    const user = userEvent.setup()
    const other = requestSummary({ id: REQUEST_2, documentNo: 'SR-2026-000002', partyId: OTHER_PARTY })
    const fetch = installPoolFetch((call) => {
      if (call.method === 'GET' && call.url.includes('/sales/requests?')) {
        return json({ items: [requestSummary({ state: 'Approved', version: '2' }), other], nextCursor: null })
      }
      return notFound(call)
    })
    const { router } = renderPool('/satis/talepler')
    const table = await screen.findByRole('table', { name: POOL_TEXT.requestList.caption })
    expect(within(table).getByRole('link', { name: 'SR-2026-000001' }).getAttribute('href')).toBe(`/satis/talepler/${REQUEST_ID}`)
    expect(fetch.calls[0].companyId).toBe(COMPANY_A)

    const start = screen.getByRole('button', { name: POOL_TEXT.requestList.quoteFromSelected }) as HTMLButtonElement
    expect(start.disabled).toBe(true)
    await user.click(screen.getByRole('checkbox', { name: POOL_TEXT.requestList.select('SR-2026-000001') }))
    await user.click(screen.getByRole('checkbox', { name: POOL_TEXT.requestList.select('SR-2026-000002') }))
    expect(screen.getByTestId('pool-request-selection').textContent).toBe(POOL_TEXT.requestList.mixedParties)
    expect(start.disabled).toBe(true)
    await user.click(screen.getByRole('checkbox', { name: POOL_TEXT.requestList.select('SR-2026-000002') }))
    await user.click(start)
    expect(router.state.location.pathname).toBe('/satis/teklifler/yeni')
    expect(fetch.calls.every((call) => !call.url.includes('state='))).toBe(true)
    expect(router.state.location.search).toBe(`?talep=${REQUEST_ID}`)

  })

  it('the state filter is the server’s exact state, and changing it clears the selection', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch(() => json({ items: [requestSummary()], nextCursor: null }))
    renderPool('/satis/talepler')
    await user.click(await screen.findByRole('checkbox', { name: POOL_TEXT.requestList.select('SR-2026-000001') }))
    await user.selectOptions(screen.getByLabelText(POOL_TEXT.requestList.filter), 'Submitted')
    await waitFor(() => expect(fetch.calls.some((call) => call.url.includes('state=Submitted'))).toBe(true))
    expect(((await screen.findByRole('checkbox', { name: POOL_TEXT.requestList.select('SR-2026-000001') })) as HTMLInputElement).checked).toBe(false)
  })

  it('a rejected request cannot be selected for a quote', async () => {
    installPoolFetch(() => json({ items: [requestSummary({ state: 'Rejected', version: '2' })], nextCursor: null }))
    renderPool('/satis/talepler')
    const box = (await screen.findByRole('checkbox', { name: POOL_TEXT.requestList.select('SR-2026-000001') })) as HTMLInputElement
    expect(box.disabled).toBe(true)
  })

  it('a malformed list is not shown as rows', async () => {
    installPoolFetch(() => json({ items: [{ ...requestSummary(), version: 1 }], nextCursor: null }))
    renderPool('/satis/talepler')
    expect(await screen.findByText(/satış talepleri/, { selector: 'p' })).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('Yeni talep', () => {
  it('keeps the key on an unknown outcome, retries the SAME snapshot, then opens the recorded request', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetch = installPoolFetch((call) => {
      if (call.method === 'POST' && call.url.endsWith('/sales/requests')) {
        attempts += 1
        if (attempts === 1) return json({ status: 503 }, 503)
        const sent = call.body as { lines: { lineId: string }[] }
        return json(requestMutation(requestDetail({}, {
          lines: [{ ...(requestDetail().lines as Record<string, unknown>[])[0], lineId: sent.lines[0].lineId, requestedDeliveryDate: null }],
        })), 201)
      }
      if (call.method === 'GET' && call.url.includes(`/sales/requests/${REQUEST_ID}`)) return json(requestDetail())
      return notFound(call)
    })
    const { router } = renderPool('/satis/talepler/yeni')
    await user.selectOptions(await screen.findByLabelText(POOL_TEXT.form.customer), 'ABC Demir — A hesabı')
    fireEvent.change(screen.getByLabelText(POOL_TEXT.form.businessDate), { target: { value: '2026-10-20' } })
    await user.selectOptions(screen.getByLabelText(POOL_TEXT.form.product), PRODUCT)
    await user.selectOptions(screen.getByLabelText(POOL_TEXT.form.unit), KG)
    await user.type(screen.getByLabelText(POOL_TEXT.form.quantity), '1500')
    await user.click(screen.getByRole('button', { name: POOL_TEXT.requestCreate.submit }))

    expect((await screen.findByTestId('pool-request-create-notice')).textContent).toBe(POOL_TEXT.attempt.unknown(POOL_TEXT.requestCreate.noun))
    expect((screen.getByLabelText(POOL_TEXT.form.quantity) as HTMLInputElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: POOL_TEXT.common.retry }))
    await waitFor(() => expect(router.state.location.pathname).toBe(`/satis/talepler/${REQUEST_ID}`))
    const posts = fetch.posts()
    expect(posts).toHaveLength(2)
    expect(posts[1].idempotencyKey).toBe(posts[0].idempotencyKey)
    expect(posts[1].body).toEqual(posts[0].body)
    expect(posts[0].body).toMatchObject({ businessDate: '2026-10-20', note: null, lines: [{ productId: PRODUCT, expectedProductVersion: '7', orderUnitId: KG, quantity: '1500' }] })
  })

  it('P2: an unknown outcome survives leaving the screen — the next visit resumes the SAME key and values', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetch = installPoolFetch((call) => {
      if (call.method === 'POST') {
        attempts += 1
        if (attempts === 1) return json({ status: 503 }, 503)
        const sent = call.body as { lines: { lineId: string }[] }
        return json(requestMutation(requestDetail({}, {
          lines: [{ ...(requestDetail().lines as Record<string, unknown>[])[0], lineId: sent.lines[0].lineId, requestedDeliveryDate: null }],
        })), 201)
      }
      if (call.url.includes('/sales/requests?')) return json({ items: [], nextCursor: null })
      return json(requestDetail())
    })
    const { router } = renderPool('/satis/talepler/yeni')
    await user.selectOptions(await screen.findByLabelText(POOL_TEXT.form.customer), 'ABC Demir — A hesabı')
    fireEvent.change(screen.getByLabelText(POOL_TEXT.form.businessDate), { target: { value: '2026-10-20' } })
    await user.selectOptions(screen.getByLabelText(POOL_TEXT.form.product), PRODUCT)
    await user.selectOptions(screen.getByLabelText(POOL_TEXT.form.unit), KG)
    await user.type(screen.getByLabelText(POOL_TEXT.form.quantity), '1500')
    await user.click(screen.getByRole('button', { name: POOL_TEXT.requestCreate.submit }))
    expect((await screen.findByTestId('pool-request-create-notice')).textContent).toBe(POOL_TEXT.attempt.unknown(POOL_TEXT.requestCreate.noun))

    // The screen goes (the person leaves through the list), then comes back.
    await router.navigate('/satis/talepler')
    expect(await screen.findByText(/Kayıt yok/)).toBeTruthy()
    await router.navigate('/satis/talepler/yeni')
    expect((await screen.findByTestId('pool-request-create-notice')).textContent).toBe(POOL_TEXT.attempt.unknown(POOL_TEXT.requestCreate.noun))
    expect((screen.getByLabelText(POOL_TEXT.form.quantity) as HTMLInputElement).value).toBe('1500')
    expect((screen.getByLabelText(POOL_TEXT.form.quantity) as HTMLInputElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: POOL_TEXT.common.retry }))
    await waitFor(() => expect(router.state.location.pathname).toBe(`/satis/talepler/${REQUEST_ID}`))
    await screen.findByTestId('pool-request-state')
    const [first, second] = fetch.posts()
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)

    // Settled once shown: a later visit starts a fresh form.
    await router.navigate('/satis/talepler/yeni')
    expect(((await screen.findByLabelText(POOL_TEXT.form.quantity)) as HTMLInputElement).value).toBe('')
    expect(screen.queryByTestId('pool-request-create-notice')).toBeNull()
  })

  it('shows the client errors in a summary and sends nothing', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch(notFound)
    renderPool('/satis/talepler/yeni')
    await user.click(await screen.findByRole('button', { name: POOL_TEXT.requestCreate.submit }))
    const summary = await screen.findByTestId('pool-error-summary')
    expect(within(summary).getByText(POOL_TEXT.validation.customerRequired)).toBeTruthy()
    expect(fetch.posts()).toHaveLength(0)
  })
})

describe('Talep detayı', () => {
  it('rejection needs its reason; approval sends the version with a key and re-reads the request', async () => {
    const user = userEvent.setup()
    let approved = false
    const fetch = installPoolFetch((call) => {
      if (call.method === 'POST' && call.url.endsWith('/approval')) {
        approved = true
        return json(requestMutation(requestDetail({ state: 'Approved', version: '2' })), 200)
      }
      if (call.method === 'GET') return json(approved ? requestDetail({ state: 'Approved', version: '2' }) : requestDetail())
      return notFound(call)
    })
    renderPool(`/satis/talepler/${REQUEST_ID}`)
    await user.click(await screen.findByRole('button', { name: POOL_TEXT.requestDetail.reject }))
    expect(screen.getByText(POOL_TEXT.requestDetail.reasonRequired)).toBeTruthy()
    expect(fetch.posts()).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: POOL_TEXT.requestDetail.approve }))
    await waitFor(() => expect(screen.getByTestId('pool-request-state').textContent).toBe('Onaylı'))
    const [post] = fetch.posts()
    expect(post.body).toEqual({ expectedVersion: '1' })
    expect(post.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    // Approved: no approval any more, the rejection stays offered (No 161's two source states).
    expect(screen.queryByRole('button', { name: POOL_TEXT.requestDetail.approve })).toBeNull()
    expect(screen.getByRole('button', { name: POOL_TEXT.requestDetail.reject })).toBeTruthy()
  })

  it('a 409 re-reads the request and keeps the typed reason; the next send is a NEW attempt on the new version', async () => {
    const user = userEvent.setup()
    let reads = 0
    let posts = 0
    const fetch = installPoolFetch((call) => {
      if (call.method === 'POST') {
        posts += 1
        return posts === 1
          ? json({ status: 409, title: 'x', code: 'version_conflict' }, 409)
          : json(requestMutation(requestDetail({ state: 'Rejected', version: '3' })), 200)
      }
      reads += 1
      return json(reads === 1 ? requestDetail() : requestDetail({ version: '2' }))
    })
    renderPool(`/satis/talepler/${REQUEST_ID}`)
    await user.type(await screen.findByLabelText(POOL_TEXT.requestDetail.reasonLabel), 'Müşteri vazgeçti')
    await user.click(screen.getByRole('button', { name: POOL_TEXT.requestDetail.reject }))
    const notice = await screen.findByTestId('pool-request-notice-rejection')
    expect(notice.textContent).toContain(POOL_TEXT.attempt.conflict)
    await waitFor(() => expect(reads).toBe(2))
    expect((screen.getByLabelText(POOL_TEXT.requestDetail.reasonLabel) as HTMLTextAreaElement).value).toBe('Müşteri vazgeçti')
    await user.click(screen.getByRole('button', { name: POOL_TEXT.requestDetail.reject }))
    await waitFor(() => expect(fetch.posts()).toHaveLength(2))
    const [first, second] = fetch.posts()
    expect(second.body).toEqual({ expectedVersion: '2', reason: 'Müşteri vazgeçti' })
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey)
  })

  it('shows the No 161 refusal in its own words', async () => {
    const user = userEvent.setup()
    installPoolFetch((call) =>
      call.method === 'POST'
        ? json({ status: 409, title: 'x', code: 'sales_request_has_approved_order' }, 409)
        : json(requestDetail({ state: 'Approved', version: '2' })))
    renderPool(`/satis/talepler/${REQUEST_ID}`)
    await user.type(await screen.findByLabelText(POOL_TEXT.requestDetail.reasonLabel), 'İptal')
    await user.click(screen.getByRole('button', { name: POOL_TEXT.requestDetail.reject }))
    const notice = (await screen.findByTestId('pool-request-notice-rejection')).textContent
    expect(notice).toContain('talep reddedilemez')
    expect(notice).toContain('iptal edilmemiş bir sipariş')
  })
})

describe('Yeni teklif', () => {
  it('covers the chosen request: its lines are copied, the customer is fixed, prices are added and requestIds sent', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch((call) => {
      if (call.method === 'GET' && call.url.includes(`/sales/requests/${REQUEST_ID}`)) return json(requestDetail({ state: 'Approved', version: '2' }))
      if (call.method === 'POST' && call.url.endsWith('/sales/quotes')) {
        return json({ status: 422, title: 'x', code: 'validation_failed', errors: [{ field: 'lines[0]', code: 'sales_discount_out_of_range' }] }, 422)
      }
      return notFound(call)
    })
    renderPool(`/satis/teklifler/yeni?talep=${REQUEST_ID}`)
    expect(await screen.findByTestId('pool-quote-covered-requests')).toBeTruthy()
    expect(screen.getByTestId('pool-line-1-product').textContent).toBe('PRF-40X40 — 40x40 profil')
    expect((screen.getByLabelText(POOL_TEXT.form.quantity) as HTMLInputElement).value).toBe('1500')
    fireEvent.change(screen.getByLabelText(POOL_TEXT.form.businessDate), { target: { value: '2026-10-22' } })
    await user.selectOptions(screen.getByLabelText(POOL_TEXT.form.currency), CURRENCY)
    await user.type(screen.getByLabelText(POOL_TEXT.form.unitPrice), '42,5')
    await user.selectOptions(screen.getByLabelText(POOL_TEXT.form.priceUnit), KG)
    await user.type(screen.getByLabelText(POOL_TEXT.form.discount), '150')
    await user.click(screen.getByRole('button', { name: POOL_TEXT.quoteCreate.submit }))

    await waitFor(() => expect(fetch.posts()).toHaveLength(1))
    expect(fetch.posts()[0].body).toMatchObject({
      currencyId: CURRENCY,
      businessDate: '2026-10-22',
      requestIds: [REQUEST_ID],
      lines: [{ productId: PRODUCT, expectedProductVersion: '7', orderUnitId: KG, quantity: '1500', unitPrice: '42.5', priceUnitId: KG, discountPercent: '150', requestedDeliveryDate: '2026-11-01' }],
    })
    // The server's whole-line rule lands on the row; the values stay.
    expect(await screen.findByText(POOL_TEXT.form.serverFieldError, { selector: 'p' })).toBeTruthy()
    expect((screen.getByLabelText(POOL_TEXT.form.discount) as HTMLInputElement).value).toBe('150')
  })

  it('does not offer a quote over a rejected request, or over requests of two customers', async () => {
    installPoolFetch((call) =>
      call.url.includes(REQUEST_ID) ? json(requestDetail({ state: 'Rejected', version: '2' })) : json(requestDetail({ id: REQUEST_2, partyId: OTHER_PARTY })))
    renderPool(`/satis/teklifler/yeni?talep=${REQUEST_ID}`)
    expect((await screen.findByTestId('pool-quote-requests-blocked')).textContent).toBe(POOL_TEXT.quoteCreate.requestRejected('SR-2026-000001'))
    cleanup()
    installPoolFetch((call) =>
      call.url.includes(REQUEST_ID) ? json(requestDetail()) : json(requestDetail({ id: REQUEST_2, documentNo: 'SR-2026-000002', partyId: OTHER_PARTY })))
    renderPool(`/satis/teklifler/yeni?talep=${REQUEST_ID}&talep=${REQUEST_2}`)
    expect((await screen.findByTestId('pool-quote-requests-blocked')).textContent).toBe(POOL_TEXT.quoteCreate.requestsMixed)
  })
})

describe('Teklif detayı', () => {
  it('"Siparişe dönüştür": an unknown outcome retries the SAME key, and the recorded order opens', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const fetch = installPoolFetch((call) => {
      if (call.method === 'POST' && call.url.endsWith(`/sales/quotes/${QUOTE_ID}/conversion`)) {
        attempts += 1
        return attempts === 1 ? json({ status: 503 }, 503) : json(orderMutation('2026-10-27'), 201, { Location: `/api/v1/sales/drafts/${ORDER_ID}` })
      }
      if (call.method === 'GET' && call.url.includes(`/sales/quotes/${QUOTE_ID}`)) return json(quoteDetail())
      // The order screen's own reads after the navigation are not this test's.
      if (call.url.includes('/sales/drafts/')) return json({ status: 404 }, 404)
      return notFound(call)
    })
    const { router } = renderPool(`/satis/teklifler/${QUOTE_ID}`)
    await user.click(await screen.findByRole('button', { name: POOL_TEXT.quoteDetail.convert, expanded: false }))
    const form = screen.getByRole('form', { name: POOL_TEXT.quoteDetail.convertHeading })
    await user.click(within(form).getByRole('button', { name: POOL_TEXT.quoteDetail.convert }))
    expect(within(form).getByText(POOL_TEXT.quoteDetail.convertDateRequired)).toBeTruthy()
    expect(fetch.posts()).toHaveLength(0)

    fireEvent.change(screen.getByLabelText(POOL_TEXT.quoteDetail.convertDate), { target: { value: '2026-10-27' } })
    await user.click(within(form).getByRole('button', { name: POOL_TEXT.quoteDetail.convert }))
    expect((await screen.findByTestId('pool-quote-convert-notice')).textContent).toBe(POOL_TEXT.attempt.unknown('sipariş'))
    await user.click(screen.getByRole('button', { name: POOL_TEXT.common.retry }))
    await waitFor(() => expect(router.state.location.pathname).toBe(`/satis/siparisler/${ORDER_ID}`))
    const [first, second] = fetch.posts()
    expect(first.body).toEqual({ expectedVersion: '1', businessDate: '2026-10-27' })
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
  })

  it('a covered request that is not approved is refused in its own words and the quote is re-read', async () => {
    const user = userEvent.setup()
    let reads = 0
    installPoolFetch((call) => {
      if (call.method === 'POST') return json({ status: 409, title: 'x', code: 'sales_quote_request_not_approved', errors: [{ field: 'requestIds', code: 'sales_quote_request_not_approved' }] }, 409)
      reads += 1
      return json(quoteDetail())
    })
    renderPool(`/satis/teklifler/${QUOTE_ID}`)
    await user.click(await screen.findByRole('button', { name: POOL_TEXT.quoteDetail.convert, expanded: false }))
    fireEvent.change(screen.getByLabelText(POOL_TEXT.quoteDetail.convertDate), { target: { value: '2026-10-27' } })
    const form = screen.getByRole('form', { name: POOL_TEXT.quoteDetail.convertHeading })
    await user.click(within(form).getByRole('button', { name: POOL_TEXT.quoteDetail.convert }))
    expect((await screen.findByTestId('pool-quote-convert-notice')).textContent).toContain('onaysız talepten sipariş açılmaz')
    await waitFor(() => expect(reads).toBe(2))
  })

  it('SAT-D24 interim: an expired quote is refused in its own words and the quote is re-read', async () => {
    const user = userEvent.setup()
    let reads = 0
    installPoolFetch((call) => {
      if (call.method === 'POST') return json({ status: 409, title: 'x', code: 'sales_quote_expired', errors: [{ field: 'validUntil', code: 'sales_quote_expired' }] }, 409)
      reads += 1
      return json(quoteDetail())
    })
    renderPool(`/satis/teklifler/${QUOTE_ID}`)
    await user.click(await screen.findByRole('button', { name: POOL_TEXT.quoteDetail.convert, expanded: false }))
    fireEvent.change(screen.getByLabelText(POOL_TEXT.quoteDetail.convertDate), { target: { value: '2026-10-27' } })
    const form = screen.getByRole('form', { name: POOL_TEXT.quoteDetail.convertHeading })
    await user.click(within(form).getByRole('button', { name: POOL_TEXT.quoteDetail.convert }))
    expect((await screen.findByTestId('pool-quote-convert-notice')).textContent).toContain('geçerlilik süresi dolmuş')
    await waitFor(() => expect(reads).toBe(2))
  })

  it('a loss needs its free-text reason and sends it with the version', async () => {
    const user = userEvent.setup()
    let lost = false
    const fetch = installPoolFetch((call) => {
      if (call.method === 'POST') {
        lost = true
        return json(quoteMutation(quoteDetail({ state: 'Lost', version: '2' })), 200)
      }
      return json(lost ? quoteDetail({ state: 'Lost', version: '2' }) : quoteDetail())
    })
    renderPool(`/satis/teklifler/${QUOTE_ID}`)
    await user.click(await screen.findByRole('button', { name: POOL_TEXT.quoteDetail.lose, expanded: false }))
    const form = screen.getByRole('form', { name: POOL_TEXT.quoteDetail.loseHeading })
    await user.click(within(form).getByRole('button', { name: POOL_TEXT.quoteDetail.lose }))
    expect(within(form).getByText(POOL_TEXT.quoteDetail.lossReasonRequired)).toBeTruthy()
    await user.type(within(form).getByLabelText(POOL_TEXT.quoteDetail.lossReasonLabel), 'Rakip fiyatı düşük')
    await user.click(within(form).getByRole('button', { name: POOL_TEXT.quoteDetail.lose }))
    await waitFor(() => expect(screen.getByTestId('pool-quote-state').textContent).toBe('Kaybedildi'))
    expect(fetch.posts()[0].body).toEqual({ expectedVersion: '1', reason: 'Rakip fiyatı düşük' })
    expect(screen.getByTestId('pool-quote-loss').textContent).toContain('Fiyat yüksek')
    expect(screen.queryByTestId('pool-quote-commands')).toBeNull()
  })

  it('a revision sends every line under its own id with the opened version', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch((call) => {
      if (call.method === 'POST') return json(quoteMutation(quoteDetail({ version: '2', revision: 'Rev.1' }, { lines: [quoteLine({ quantity: '1600' })] })), 200)
      return json(quoteDetail())
    })
    renderPool(`/satis/teklifler/${QUOTE_ID}`)
    await user.click(await screen.findByRole('button', { name: POOL_TEXT.quoteDetail.revise, expanded: false }))
    const quantity = screen.getByLabelText(POOL_TEXT.form.quantity)
    await user.clear(quantity)
    await user.type(quantity, '1600')
    await user.click(screen.getByRole('button', { name: POOL_TEXT.quoteDetail.reviseSubmit }))
    await waitFor(() => expect(fetch.posts()).toHaveLength(1))
    expect(fetch.posts()[0].body).toEqual({
      expectedVersion: '1',
      lines: [{ lineId: QUOTE_LINE, productId: PRODUCT, expectedProductVersion: '7', orderUnitId: KG, quantity: '1600', unitPrice: '42.5', priceUnitId: KG, discountPercent: null, requestedDeliveryDate: null, note: null }],
      validUntil: '2026-11-22',
      requestedDeliveryDate: null,
      note: null,
    })
    expect((await screen.findByTestId('pool-quote-revision-notice')).textContent).toContain('Rev.1')
  })

  it('P1: a viewer who cannot see the prices is not offered a revision (it would erase them); loss and conversion stay', async () => {
    const fetch = installPoolFetch(() =>
      json(quoteDetail({}, { lines: [quoteLine({ unitPrice: null, priceUnitId: null, priceDisclosure: 'Withheld' })] })))
    renderPool(`/satis/teklifler/${QUOTE_ID}`)
    expect((await screen.findByTestId('pool-quote-revise-withheld')).textContent).toBe(POOL_TEXT.quoteDetail.reviseWithheld)
    expect(screen.queryByRole('button', { name: POOL_TEXT.quoteDetail.revise })).toBeNull()
    expect(screen.getByRole('button', { name: POOL_TEXT.quoteDetail.lose })).toBeTruthy()
    expect(screen.getByRole('button', { name: POOL_TEXT.quoteDetail.convert })).toBeTruthy()
    expect(fetch.posts()).toHaveLength(0)
  })

  it('a withheld price reads "Gizli", and a converted quote links its one order', async () => {
    installPoolFetch(() =>
      json(quoteDetail({ state: 'Converted', convertedDraftId: ORDER_ID }, {
        lines: [quoteLine({ unitPrice: null, priceUnitId: null, priceDisclosure: 'Withheld' })],
      })))
    renderPool(`/satis/teklifler/${QUOTE_ID}`)
    expect((await screen.findByTestId('pool-quote-line-1-price')).textContent).toBe(POOL_TEXT.quoteDetail.priceWithheld)
    expect(screen.getByRole('link', { name: POOL_TEXT.quoteDetail.openOrder }).getAttribute('href')).toBe(`/satis/siparisler/${ORDER_ID}`)
    expect(screen.queryByTestId('pool-quote-commands')).toBeNull()
  })

  it('a quote of another company is not shown', async () => {
    installPoolFetch(() => json(quoteDetail({ companyId: '88888888-8888-8888-8888-888888888888' })))
    renderPool(`/satis/teklifler/${QUOTE_ID}`)
    expect(await screen.findByText(/satış teklifi/, { selector: 'p' })).toBeTruthy()
    expect(screen.queryByTestId('pool-quote-state')).toBeNull()
  })
})
