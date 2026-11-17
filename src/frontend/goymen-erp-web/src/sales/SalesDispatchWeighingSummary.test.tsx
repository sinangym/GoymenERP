import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dispatchesResponse from './examples/dispatches-response.json'
import { SALES_CAPABILITY } from './capabilities'
import { DISPATCH_WEIGHING_TEXT as T } from './dispatchWeighingText'
import { COMPANY_A, installPoolFetch, json } from './poolFixtures'
import { renderPool } from './poolTestHarness'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const id = dispatchesResponse.items[0].id
const value = {
  dispatchNoteId: id, companyId: COMPANY_A, state: 'Recorded',
  recordId: 'a7a7a7a7-0000-4000-8000-000000000001', theoreticalKg: '100.001', scaleKg: '99.999',
  differenceKg: '-0.002', totalExcessKg: '0', bandLevel: 'None', decision: 'NoExcess',
  recordState: 'Recorded', saleMayProceed: true, invoiceWait: null, version: 1,
}
function install(answer: () => Response | Promise<Response>) {
  return installPoolFetch(call => {
    if (call.url.includes('/sales/dispatches?')) return json(dispatchesResponse)
    if (call.url.endsWith(`/sales/dispatches/${id}/weighing-difference`)) return answer()
    throw new Error(`Unexpected dispatch read: ${call.method}`)
  }, [SALES_CAPABILITY.dispatchesList])
}
async function expand() {
  const buttons = await screen.findAllByRole('button', { name: T.show })
  await userEvent.setup().click(buttons[0])
}

describe('dispatch weighing in the authorized shell', () => {
  it('loads only when expanded and shows exact owner values without a financial command', async () => {
    const fetch = install(() => json(value))
    renderPool('/satis/sevkiyatlar')
    await screen.findAllByRole('button', { name: T.show })
    expect(fetch.calls).toHaveLength(1)
    await expand()
    expect(await screen.findByText(T.proceed)).toBeTruthy()
    const region = screen.getAllByRole('region', { name: T.heading })[0]
    for (const amount of ['100.001', '99.999', '-0.002', '0']) expect(within(region).getByText(amount)).toBeTruthy()
    expect(fetch.calls).toHaveLength(2)
    expect(fetch.calls[1].companyId).toBe(COMPANY_A)
    expect(fetch.calls[1].method).toBe('GET')
    expect(within(region).queryByRole('button', { name: /onayla|fatura kes/i })).toBeNull()
  })

  it('shows absent data and pending approval as distinct invoice waiting reasons', async () => {
    install(() => json({ ...value, recordState: 'PendingApproval', decision: 'ApprovalRequired',
      saleMayProceed: false, invoiceWait: 'PendingApproval' }))
    renderPool('/satis/sevkiyatlar')
    await expand()
    expect(await screen.findByText(T.waits.PendingApproval)).toBeTruthy()
    expect(screen.queryByText(T.proceed)).toBeNull()
    cleanup()
    install(() => json({ dispatchNoteId: id, companyId: COMPANY_A, state: 'NotRecorded', recordId: null,
      theoreticalKg: null, scaleKg: null, differenceKg: null, totalExcessKg: null, bandLevel: null,
      decision: null, recordState: null, saleMayProceed: false, invoiceWait: 'NotRecorded', version: null }))
    renderPool('/satis/sevkiyatlar')
    await expand()
    expect(await screen.findByText(T.missing)).toBeTruthy()
    expect(screen.queryByText(T.theoretical)).toBeNull()
  })

  it.each([
    [404, {}, /Bulunamadı/], [409, {}, /Çakışma/], [503, {}, /Servis geçici olarak kullanılamıyor/],
    [200, { ...value, companyId: value.recordId }, /Beklenmeyen yanıt/],
  ] as const)('keeps response %s separate from missing weighing data', async (status, body, message) => {
    install(() => json(body, status))
    renderPool('/satis/sevkiyatlar')
    await expand()
    expect(await screen.findByText(message)).toBeTruthy()
    expect(screen.queryByText(T.missing)).toBeNull()
    expect(screen.queryByText(T.proceed)).toBeNull()
  })
})
