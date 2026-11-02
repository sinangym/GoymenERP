/**
 * SAT-D175/D176 (EK25), SAT-D195 (EK29): the "fason hizmet" flag on the create screen — the picker asks the server for service products only, the
 * flag cannot change while a line holds a product, the price rule is explained on the price fields, the server's refusal of a
 * goods line is shown in Turkish on the line, and a normal order's request is unchanged. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import createResponseRaw from './examples/create-response.json?raw'
import { resetPendingDrafts } from './pendingDrafts'
import type { UseReferenceOptions } from './referenceSource'
import { SALES_PRICE_WRITE_CAPABILITY } from './capabilities'
import { ORDER_DRAFT_TEXT as T } from './salesText'
import { ACCOUNT, ALL_SALES, COMPANY_A, CURRENCY, KG, OPTIONS_A, PARTY, PRODUCT, installFetch, json, renderSalesApp } from './testHarness'

const PATH = '/satis/siparisler/yeni'

afterEach(() => {
  cleanup()
  resetPendingDrafts()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const formVisible = () => waitFor(() => screen.getByRole('heading', { name: T.heading }))
const toggle = () => screen.getByLabelText(T.subcontract.toggle) as HTMLInputElement

/** A source that records the item type the picker asked for and offers company A's options. */
function recordingSource(asked: (string | undefined)[]): UseReferenceOptions {
  return (companyId, _date, _accounts, _products, itemType) => {
    asked.push(itemType)
    return companyId === COMPANY_A ? { status: 'ready', options: OPTIONS_A } : { status: 'unavailable' }
  }
}

async function fillHeaderAndLine() {
  await userEvent.selectOptions(screen.getByLabelText(T.field.customer), `${PARTY}|${ACCOUNT}`)
  fireEvent.change(screen.getByLabelText(T.field.businessDate), { target: { value: '2026-09-13' } })
  await userEvent.selectOptions(screen.getByLabelText(T.field.currency), CURRENCY)
  await userEvent.selectOptions(screen.getByLabelText(T.field.product), PRODUCT)
  await userEvent.type(screen.getByLabelText(T.field.quantity), '250')
  await userEvent.selectOptions(screen.getByLabelText(T.field.orderUnit), KG)
}

/** The example answer, rewritten for THIS form's line id. */
function answerFor(body: string | null): string {
  const sent = JSON.parse(body as string)
  const answer = JSON.parse(createResponseRaw)
  answer.detail.lines[0].lineId = sent.lines[0].lineId
  answer.detail.lines[0].requestedDeliverySlices = null
  answer.detail.draft.subcontractService = true
  return JSON.stringify(answer)
}

describe('SAT-D175/D176 — the fason flag at create', () => {
  it('narrows the product picker to service products and sends the flag; the flag locks once a line holds a product', async () => {
    const asked: (string | undefined)[] = []
    const fetch = installFetch({ onCreate: (_attempt, call) => json(answerFor(call.body), 201) })
    renderSalesApp(PATH, recordingSource(asked))
    await formVisible()

    expect(toggle().checked).toBe(false)
    expect(asked.at(-1)).toBeUndefined()
    await userEvent.click(toggle())
    expect(toggle().checked).toBe(true)
    await waitFor(() => expect(asked.at(-1)).toBe('Service'))
    expect(screen.getByTestId('sales-create-subcontract-picker').textContent).toContain(T.subcontract.pickerNote)

    await fillHeaderAndLine()
    // A goods line never sits on a fason order: with a chosen product the flag no longer moves.
    expect(toggle().disabled).toBe(true)
    expect(screen.getByTestId('sales-create-subcontract-locked').textContent).toContain(T.subcontract.lockedByLines)

    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(fetch.salesCalls()).toHaveLength(1))
    expect(JSON.parse(fetch.salesCalls()[0].body as string)).toMatchObject({ subcontractService: true })
  })

  it('a normal order sends no flag and asks for every product', async () => {
    const asked: (string | undefined)[] = []
    const fetch = installFetch({ onCreate: () => json('{}', 503) })
    renderSalesApp(PATH, recordingSource(asked))
    await formVisible()
    await fillHeaderAndLine()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.attempt.unknown)

    expect(asked.every((itemType) => itemType === undefined)).toBe(true)
    expect(JSON.parse(fetch.salesCalls()[0].body as string)).not.toHaveProperty('subcontractService')
    expect(screen.queryByTestId('sales-line-editor-1-subcontract-price')).toBeNull()
  })

  it('places the server’s goods-line refusal on the line in Turkish and keeps what was typed', async () => {
    installFetch({
      onCreate: () => json(JSON.stringify({
        status: 422, title: 'validation_failed', code: 'validation_failed', correlationId: 'SENTETIK-fason',
        errors: [{ field: 'lines[0].productId', code: 'sales_subcontract_line_not_service' }],
      }), 422),
    })
    renderSalesApp(PATH)
    await formVisible()
    await userEvent.click(toggle())
    await fillHeaderAndLine()
    await userEvent.click(screen.getByRole('button', { name: T.save }))

    const product = await waitFor(() => {
      const field = screen.getByLabelText(T.field.product)
      expect(field.getAttribute('aria-invalid')).toBe('true')
      return field
    })
    expect(document.getElementById(product.getAttribute('aria-describedby') as string)?.textContent)
      .toBe('Fason hizmet siparişinde yalnız kalem tipi "Hizmet" olan ürün seçilebilir; mal satışı ayrı, normal siparişle yapılır.')
    expect((screen.getByLabelText(T.field.quantity) as HTMLInputElement).value).toBe('250')
    expect(toggle().checked).toBe(true)
  })

  it('SAT-D195: explains the per-kg manual fason price rule on the price fields of a price writer', async () => {
    installFetch({ capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY] })
    renderSalesApp(PATH)
    await formVisible()
    await userEvent.click(toggle())
    await fillHeaderAndLine()
    expect(screen.getByTestId('sales-line-editor-1-subcontract-price').textContent).toContain(T.pricing.subcontractFree)
  })
})
