/**
 * S-03 create screen in the real shell, with REAL pending requests (deferred promises
 * resolved by the test). Component evidence, NOT_ENV: the answers are #257's own
 * example bodies, not a server.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import createResponseRaw from './examples/create-response.json?raw'
import replayResponseRaw from './examples/replay-response.json?raw'
import errorProductRaw from './examples/error-product.json?raw'
import errorReplayRaw from './examples/error-replay.json?raw'
import { resetPendingDrafts } from './pendingDrafts'
import { unavailableReferenceSource } from './referenceSource'
import { SALES_CAPABILITY } from './capabilities'
import { ORDER_DRAFT_TEXT as T } from './salesText'
import {
  ACCOUNT,
  COMPANY_A,
  COMPANY_B,
  CURRENCY,
  KG,
  PARTY,
  PRODUCT,
  deferred,
  installFetch,
  json,
  renderSalesApp,
} from './testHarness'

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
const saveButton = () => screen.getByRole('button', { name: T.save })

/** Fills the form a person would fill to send #257's example request (1000 kg, 400 + 600). */
async function fillExample(slices: readonly string[] = ['400', '600']) {
  await formVisible()
  await userEvent.selectOptions(screen.getByLabelText(T.field.customer), `${PARTY}|${ACCOUNT}`)
  fireEvent.change(screen.getByLabelText(T.field.businessDate), { target: { value: '2026-09-13' } })
  await userEvent.selectOptions(screen.getByLabelText(T.field.currency), CURRENCY)
  await userEvent.selectOptions(screen.getByLabelText(T.field.product), PRODUCT)
  await userEvent.type(screen.getByLabelText(T.field.quantity), '1000')
  await userEvent.selectOptions(screen.getByLabelText(T.field.orderUnit), KG)
  fireEvent.change(screen.getByLabelText(T.field.lineDeliveryDate), { target: { value: '2026-09-20' } })
  for (const [index, quantity] of slices.entries()) {
    await userEvent.click(screen.getByRole('button', { name: T.addSlice }))
    await userEvent.type(screen.getByLabelText(`${index + 1}. ${T.field.sliceQuantity}`), quantity)
    fireEvent.change(screen.getByLabelText(`${index + 1}. ${T.field.sliceDate}`), {
      target: { value: index === 0 ? '2026-09-20' : '2026-09-22' },
    })
  }
}

/** Rewrites the example answer so it describes the ids THIS form minted. */
function answerFor(raw: string, body: string | null): string {
  const sent = JSON.parse(body as string)
  const answer = JSON.parse(raw)
  answer.detail.lines[0].lineId = sent.lines[0].lineId
  answer.detail.lines[0].requestedDeliverySlices = sent.lines[0].requestedDeliverySlices
  return JSON.stringify(answer)
}

const switchedTo = (companyId: string) =>
  waitFor(() => {
    const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
    expect(picker.disabled).toBe(false)
    expect(picker.value).toBe(companyId)
  })

describe('mounting', () => {
  it('mounts nothing and calls nothing without the create capability', async () => {
    const fetch = installFetch({ capabilities: [SALES_CAPABILITY.draftsList] })
    renderSalesApp(PATH)
    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
    expect(fetch.salesCalls()).toHaveLength(0)
  })

  it('fails closed without a selection source: no form, no guessed choices, no send', async () => {
    const fetch = installFetch()
    renderSalesApp(PATH, unavailableReferenceSource)
    await formVisible()
    expect(screen.getByText(T.referencesUnavailable)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(fetch.salesCalls()).toHaveLength(0)
  })
})

describe('saving — one key per snapshot, real pending requests', () => {
  it('shows company and the un-priced, un-approved state; a double click sends ONE request; 201 completes', async () => {
    const pending = deferred<Response>()
    const fetch = installFetch({ onCreate: () => pending.promise })
    renderSalesApp(PATH)
    await fillExample()

    const summary = screen.getByRole('region', { name: T.summary.heading })
    expect(within(summary).getByText(/GDC — Göymen A/)).toBeTruthy()
    expect(within(summary).getByText(T.summary.notPriced)).toBeTruthy()
    expect(within(summary).getByText(T.summary.notApproved)).toBeTruthy()
    expect(screen.getByTestId('sales-line-editor-1-unplanned').textContent).toContain('0')

    await userEvent.click(saveButton())
    await userEvent.click(saveButton())
    expect(await screen.findByText(T.attempt.submitting)).toBeTruthy()
    expect(fetch.salesCalls()).toHaveLength(1)

    const [call] = fetch.salesCalls()
    expect(call.companyId).toBe(COMPANY_A)
    expect(call.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    const body = JSON.parse(call.body as string)
    expect(body).not.toHaveProperty('companyId')
    expect(body.lines[0]).toMatchObject({ expectedProductVersion: '7', quantity: '1000', unitPrice: null })
    expect(body.lines[0].requestedDeliverySlices.map((s: { quantity: string }) => s.quantity)).toEqual(['400', '600'])

    await act(async () => pending.resolve(json(answerFor(createResponseRaw, call.body), 201)))
    expect(await screen.findByRole('heading', { name: T.attempt.completed })).toBeTruthy()
    expect(screen.getByText(/SO-2026-0001 · Rev\.0/)).toBeTruthy()
    expect(screen.queryByText(T.attempt.replayed)).toBeNull()
  })

  it('two submits in the same tick, before any re-render, still send ONE request', async () => {
    const pending = deferred<Response>()
    const fetch = installFetch({ onCreate: () => pending.promise })
    renderSalesApp(PATH)
    await fillExample()
    const form = saveButton().closest('form') as HTMLFormElement
    // Both events are handled before React re-renders, so the disabled button cannot help:
    // only the synchronous in-flight marker stands between one click and two orders.
    act(() => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    })
    await screen.findByText(T.attempt.submitting)
    expect(fetch.salesCalls()).toHaveLength(1)
    const [call] = fetch.salesCalls()
    await act(async () => pending.resolve(json(answerFor(createResponseRaw, call.body), 201)))
    await screen.findByRole('heading', { name: T.attempt.completed })
  })

  it('an unknown outcome retries the SAME key, company and body; the 200 replay completes once', async () => {
    const fetch = installFetch({
      onCreate: (attempt, call) =>
        attempt === 1 ? Promise.reject(new TypeError('Failed to fetch')) : json(answerFor(replayResponseRaw, call.body), 200),
    })
    renderSalesApp(PATH)
    await fillExample()
    await userEvent.click(saveButton())

    const notice = await screen.findByText(T.attempt.unknown)
    expect(notice).toBeTruthy()
    // No reload advice anywhere on the screen while the outcome is unknown.
    expect(document.body.textContent).not.toMatch(/yeniden yükle|sayfayı yenile/i)

    await userEvent.click(screen.getByRole('button', { name: T.attempt.retry }))
    expect(await screen.findByRole('heading', { name: T.attempt.completed })).toBeTruthy()
    expect(screen.getByText(T.attempt.replayed)).toBeTruthy()

    const [first, second] = fetch.salesCalls()
    expect(fetch.salesCalls()).toHaveLength(2)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.companyId).toBe(first.companyId)
    expect(second.body).toBe(first.body)
  })

  it('while the outcome is unknown, a CHANGED form is not sent under a new key', async () => {
    const fetch = installFetch({ onCreate: () => json('{}', 503) })
    renderSalesApp(PATH)
    await fillExample()
    await userEvent.click(saveButton())
    await screen.findByText(T.attempt.unknown)

    const quantity = screen.getByLabelText(T.field.quantity)
    await userEvent.clear(quantity)
    await userEvent.type(quantity, '1100')
    await userEvent.click(saveButton())

    expect(await screen.findByText(T.attempt.unknownChangedBlocked)).toBeTruthy()
    expect(fetch.salesCalls()).toHaveLength(1)

    // Leaving explicitly is the person's decision; only then does a changed form get a new key.
    await userEvent.click(screen.getByRole('button', { name: T.attempt.abandon }))
    await userEvent.click(saveButton())
    await waitFor(() => expect(fetch.salesCalls()).toHaveLength(2))
    const [first, second] = fetch.salesCalls()
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey)
    // The line identity minted when the row was added is the same in both bodies.
    expect(JSON.parse(second.body as string).lines[0].lineId).toBe(JSON.parse(first.body as string).lines[0].lineId)
  })

  it('a 409 answering the retry does not say the first send was not saved', async () => {
    installFetch({
      onCreate: (attempt) => (attempt === 1 ? Promise.reject(new TypeError('lost')) : json(errorReplayRaw, 409)),
    })
    renderSalesApp(PATH)
    await fillExample()
    await userEvent.click(saveButton())
    await screen.findByText(T.attempt.unknown)
    await userEvent.click(screen.getByRole('button', { name: T.attempt.retry }))

    expect(await screen.findByText(T.attempt.refusedAfterUnknown)).toBeTruthy()
    expect(screen.queryByText(/taslak kaydedilmedi/i)).toBeNull()
    expect(screen.getByRole('button', { name: T.attempt.retry })).toBeTruthy()
  })
})

describe('refusals and validation', () => {
  it('a 422 places the owner’s product error on the line it names, and keeps what was typed', async () => {
    installFetch({ onCreate: () => json(errorProductRaw, 422) })
    renderSalesApp(PATH)
    await fillExample()
    await userEvent.click(saveButton())

    const product = await waitFor(() => {
      const field = screen.getByLabelText(T.field.product)
      expect(field.getAttribute('aria-invalid')).toBe('true')
      return field
    })
    const described = document.getElementById(product.getAttribute('aria-describedby') as string)
    expect(described?.textContent).toBe('Bu ürün satışa kullanılabilir değil.')
    expect(screen.getByText(T.errorSummary)).toBeTruthy()
    expect((screen.getByLabelText(T.field.quantity) as HTMLInputElement).value).toBe('1000')
    expect(screen.getByText(/Taslak kaydedilmedi\. İşaretli alanları düzeltin\./)).toBeTruthy()
  })

  it('after a lost POST, an invalid field does not claim the draft was not sent', async () => {
    const fetch = installFetch({ onCreate: () => Promise.reject(new TypeError('lost')) })
    renderSalesApp(PATH)
    await fillExample()
    await userEvent.click(saveButton())
    await screen.findByText(T.attempt.unknown)

    await userEvent.clear(screen.getByLabelText(T.field.quantity))
    await userEvent.click(saveButton())

    expect(await screen.findByText(T.errorSummaryUnresolved)).toBeTruthy()
    expect(screen.queryByText(T.errorSummary)).toBeNull()
    expect(document.body.textContent).not.toMatch(/gönderilmedi|kaydedilmedi[.;]/)
    expect(fetch.salesCalls()).toHaveLength(1)
  })

  it('a field 422 answering the same-key retry of a lost POST does not claim the draft was not sent', async () => {
    installFetch({
      onCreate: (attempt) => (attempt === 1 ? Promise.reject(new TypeError('lost')) : json(errorProductRaw, 422)),
    })
    renderSalesApp(PATH)
    await fillExample()
    await userEvent.click(saveButton())
    await screen.findByText(T.attempt.unknown)
    await userEvent.click(screen.getByRole('button', { name: T.attempt.retry }))

    expect(await screen.findByText(T.errorSummaryUnresolved)).toBeTruthy()
    await waitFor(() => expect(screen.getByLabelText(T.field.product).getAttribute('aria-invalid')).toBe('true'))
    expect(screen.getByText(T.attempt.refusedAfterUnknown)).toBeTruthy()
    expect(document.body.textContent).not.toMatch(/gönderilmedi|kaydedilmedi[.;]/)
  })

  it('400 + 700 on a 1000 line is refused before anything is sent, and the summary takes focus', async () => {
    const fetch = installFetch({ onCreate: () => json('{}', 500) })
    renderSalesApp(PATH)
    await fillExample(['400', '700'])
    await userEvent.click(saveButton())

    expect(await screen.findByText(T.validation.slicesExceedOrder, { selector: 'a' })).toBeTruthy()
    await waitFor(() => expect(document.activeElement?.textContent).toContain(T.errorSummary))
    expect(fetch.salesCalls()).toHaveLength(0)
  })

  it('400 + 400 shows 200 as unplanned, not delivered', async () => {
    installFetch()
    renderSalesApp(PATH)
    await fillExample(['400', '400'])
    const hint = screen.getByTestId('sales-line-editor-1-unplanned').textContent ?? ''
    expect(hint).toContain(T.unplanned('200'))
    expect(hint).toContain(T.unplannedNote)
  })
})

describe('company context', () => {
  it('a late answer for company A paints nothing in company B', async () => {
    const pending = deferred<Response>()
    const fetch = installFetch({ onCreate: () => pending.promise })
    renderSalesApp(PATH)
    await fillExample()
    await userEvent.click(saveButton())
    await screen.findByText(T.attempt.submitting)

    // The unresolved send makes the switch ASK; the person chooses to leave.
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    const dialog = await waitFor(() => screen.getByRole('dialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await switchedTo(COMPANY_B)
    await waitFor(() => expect(screen.getByText(T.referencesUnavailable)).toBeTruthy())

    const [call] = fetch.salesCalls()
    await act(async () => pending.resolve(json(answerFor(createResponseRaw, call.body), 201)))

    // B shows no success, no toast, and no write was unlocked by A's answer.
    expect(screen.queryByText(T.attempt.completed)).toBeNull()
    expect(screen.queryByText(/SO-2026-0001/)).toBeNull()
    expect(screen.getByText(T.referencesUnavailable)).toBeTruthy()
    expect(fetch.salesCalls()).toHaveLength(1)

    // Back in A the answer is not lost: the draft exists, and A says so — once, without a resend.
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_A)
    await switchedTo(COMPANY_A)
    expect(await screen.findByRole('heading', { name: T.attempt.completed })).toBeTruthy()
    expect(fetch.salesCalls()).toHaveLength(1)
  })
})
