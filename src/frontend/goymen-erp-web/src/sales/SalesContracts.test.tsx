/**
 * S-11 (SAT-D28, NS-13, SAT-D346): the sales contracts panel shows each contract's plan, rules and the server's approval facts —
 * "sınır tanımlı değil" when no limit was configured at recording time — offers the create form only with the create capability
 * and the discount decision only on a pending contract with the approve capability, sends exactly one key per attempt and the same
 * key again on an unknown outcome, names the server's refusal and refreshes on success or a conflict. The container is not routed
 * (the shell capability is not published yet), so the panel is checked directly with a stub transport. Every identity is synthetic.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { OrderCommandOutcome } from './orderAttempt'
import { decodeSalesContract, type SalesContract } from './salesContract'
import type { SalesContractMutation } from './salesContractApi'
import { SALES_CONTRACT_TEXT as T } from './salesContractText'
import { SalesContractsPanel, readContractForm, type PendingSalesContractAction } from './SalesContracts'
import type { ViewOutcome } from '../shell/uiState'

type Body = Record<string, any>

const PARTY = '11111111-1111-4111-8111-111111111111'
const ACCOUNT = '22222222-2222-4222-8222-222222222222'
const CURRENCY = '33333333-3333-4333-8333-333333333333'

const wire = (overrides: Body = {}): Body => ({
  id: '5c5c5c5c-5c5c-4c5c-8c5c-5c5c5c5c5c5c', contractNo: 1, partyId: PARTY, companyAccountId: ACCOUNT, currencyId: CURRENCY,
  moneyScale: 2, state: 'Active', version: '1', validFrom: '2026-11-01', validThrough: '2027-10-31',
  installments: [{ installmentNo: 1, dueDate: '2026-12-01', amount: '1500.00' }], plannedTotalAmount: '1500.00',
  discountRules: [], clauses: null, discountApprovalRequired: false, discountLimitDefined: false, discountLimitPercent: null,
  isInEffectToday: true, createdBy: 'synthetic-seller', createdAtUtc: '2026-11-15T08:00:00Z', decidedBy: null, decidedAtUtc: null,
  rejectionReason: null, ...overrides,
})

const pendingWire = (overrides: Body = {}): Body => wire({
  id: '6d6d6d6d-6d6d-4d6d-8d6d-6d6d6d6d6d6d', contractNo: 2, state: 'PendingDiscountApproval', isInEffectToday: false,
  discountRules: [{ ruleNo: 1, kind: 'Line', label: 'SENTETIK hacim', ratePercent: '5' }], discountApprovalRequired: true,
  ...overrides,
})

function contract(body: Body): SalesContract {
  const decoded = decodeSalesContract(body)
  if (decoded === null) throw new Error('fixture does not decode')
  return decoded
}

const ACTIVE = contract(wire())
const PENDING = contract(pendingWire())
const APPROVED = contract(pendingWire({ state: 'Active', version: '2', isInEffectToday: true, decidedBy: 'synthetic-manager',
  decidedAtUtc: '2026-11-15T09:00:00Z' }))

type Send = (action: PendingSalesContractAction) => Promise<OrderCommandOutcome<SalesContractMutation>>

const succeeded = (value: SalesContract, isReplay = false): OrderCommandOutcome<SalesContractMutation> =>
  ({ type: 'succeeded', data: { contract: value, isReplay } })
const refused = (status: number, code: string | null): OrderCommandOutcome<SalesContractMutation> => ({
  type: 'http', status, problem: { status, title: 'refused', code, correlationId: 'corr-contract-1', errors: [] },
})

interface PanelOptions {
  readonly outcome?: ViewOutcome<readonly SalesContract[]> | undefined
  readonly canCreate?: boolean
  readonly canDecide?: boolean
  readonly send?: ReturnType<typeof vi.fn<Send>>
  readonly onRecorded?: ReturnType<typeof vi.fn<() => void>>
}

function renderPanel(options: PanelOptions = {}) {
  // An explicit `outcome: undefined` is the loading state, not "use the default list".
  const outcome: ViewOutcome<readonly SalesContract[]> | undefined =
    'outcome' in options ? options.outcome : { type: 'ok-single', data: [ACTIVE, PENDING] }
  const send = options.send ?? vi.fn<Send>()
  const onRecorded = options.onRecorded ?? vi.fn<() => void>()
  render(<SalesContractsPanel outcome={outcome} canCreate={options.canCreate ?? false} canDecide={options.canDecide ?? false}
    send={send} onRecorded={onRecorded} />)
  return { send, onRecorded, section: screen.getByRole('region', { name: T.heading }) }
}

function fillCreateForm(form: HTMLElement) {
  fireEvent.change(within(form).getByLabelText(T.party), { target: { value: ` ${PARTY} ` } })
  fireEvent.change(within(form).getByLabelText(T.account), { target: { value: ACCOUNT } })
  fireEvent.change(within(form).getByLabelText(T.currency), { target: { value: CURRENCY } })
  fireEvent.change(within(form).getByLabelText(T.validFrom), { target: { value: '2026-11-01' } })
  fireEvent.change(within(form).getByLabelText(T.validThrough), { target: { value: '2027-10-31' } })
  fireEvent.change(within(form).getByLabelText(T.installmentDueDate(1)), { target: { value: '2026-12-01' } })
  fireEvent.change(within(form).getByLabelText(T.installmentAmount(1)), { target: { value: '1500,50' } })
  fireEvent.click(within(form).getByRole('button', { name: T.addRule }))
  fireEvent.change(within(form).getByLabelText(T.ruleLabel(1)), { target: { value: ' SENTETIK hacim ' } })
  fireEvent.change(within(form).getByLabelText(T.ruleRate(1)), { target: { value: '7,5' } })
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('S-11 sales contracts panel', () => {
  it('shows the plan, the rules and "sınır tanımlı değil" for a discount recorded without a configured limit', () => {
    const { section } = renderPanel()
    const pending = within(section).getByTestId('sales-contract-2')
    expect(within(pending).getByTestId('sales-contract-2-state').textContent).toBe(T.state.PendingDiscountApproval)
    expect(within(pending).getByTestId('sales-contract-2-limit').textContent).toContain(T.limitNotDefined)
    expect(within(pending).getByTestId('sales-contract-2-limit').textContent).toContain(T.approvalRequired)
    expect(within(pending).getByTestId('sales-contract-2-total').textContent).toBe(T.plannedTotal('1500.00'))
    expect(within(pending).getByTestId('sales-contract-2-validity').textContent).toContain(T.notInEffectToday)
    const active = within(section).getByTestId('sales-contract-1')
    expect(within(active).getByTestId('sales-contract-1-no-discount').textContent).toBe(T.noDiscount)
    expect(within(active).queryByTestId('sales-contract-1-limit')).toBeNull()
    expect(within(active).getByTestId('sales-contract-1-clauses').textContent).toBe(T.noClauses)
  })

  it('shows the configured limit as the server recorded it', () => {
    const limited = contract(wire({ discountRules: [{ ruleNo: 1, kind: 'Total', label: 'SENTETIK peşin', ratePercent: '2.5' }],
      discountLimitDefined: true, discountLimitPercent: '10' }))
    const { section } = renderPanel({ outcome: { type: 'ok-single', data: [limited] } })
    const limit = within(section).getByTestId('sales-contract-1-limit').textContent
    expect(limit).toContain(T.limitDefined('10'))
    expect(limit).toContain(T.approvalNotRequired)
  })

  it('offers neither the form nor a decision without the capabilities, and no decision on a decided contract', () => {
    const { section } = renderPanel({ outcome: { type: 'ok-single', data: [APPROVED] }, canDecide: true })
    expect(within(section).queryByTestId('sales-contract-create')).toBeNull()
    expect(within(section).queryByRole('button', { name: T.approve })).toBeNull()
    expect(within(section).getByTestId('sales-contract-2-decided').textContent).toContain('synthetic-manager')
    cleanup()
    const second = renderPanel()
    expect(within(second.section).queryByRole('button', { name: T.approve })).toBeNull()
  })

  it('creates one contract with one key and the canonical body, then refreshes', async () => {
    const send = vi.fn<Send>().mockResolvedValue(succeeded(PENDING))
    const { section, onRecorded } = renderPanel({ canCreate: true, send })
    const form = within(section).getByTestId('sales-contract-create')
    fillCreateForm(form)
    fireEvent.click(within(form).getByRole('button', { name: T.create }))

    expect((await within(section).findByText(T.createdPending)).getAttribute('role')).toBe('status')
    expect(send).toHaveBeenCalledTimes(1)
    const action = send.mock.calls[0][0]
    expect(action.kind).toBe('create')
    expect(action.key).toMatch(/\S/)
    expect(action.kind === 'create' ? action.payload : null).toEqual({
      partyId: PARTY, companyAccountId: ACCOUNT, currencyId: CURRENCY, validFrom: '2026-11-01', validThrough: '2027-10-31',
      installments: [{ dueDate: '2026-12-01', amount: '1500.5' }],
      discountRules: [{ kind: 'Line', label: 'SENTETIK hacim', ratePercent: '7.5' }],
      clauses: null,
    })
    expect(onRecorded).toHaveBeenCalledTimes(1)
  })

  it('names the first form problem and sends nothing', () => {
    const send = vi.fn<Send>()
    const { section } = renderPanel({ canCreate: true, send })
    fireEvent.click(within(within(section).getByTestId('sales-contract-create')).getByRole('button', { name: T.create }))
    expect(within(section).getByTestId('sales-contract-notice').textContent).toBe(T.invalidReference)
    expect(send).not.toHaveBeenCalled()
  })

  it('keeps the key on an unknown outcome and retries with the same key and body', async () => {
    const send = vi.fn<Send>().mockResolvedValueOnce({ type: 'offline' }).mockResolvedValueOnce(succeeded(ACTIVE, true))
    const { section, onRecorded } = renderPanel({ canCreate: true, send })
    const form = within(section).getByTestId('sales-contract-create')
    fillCreateForm(form)
    fireEvent.click(within(form).getByRole('button', { name: T.create }))
    expect(await within(section).findByText(T.unknown)).toBeTruthy()
    expect(within(form).getByRole('button', { name: T.create })).toHaveProperty('disabled', true)
    expect(onRecorded).not.toHaveBeenCalled()

    fireEvent.click(within(section).getByTestId('sales-contract-retry'))
    expect(await within(section).findByText(T.createdActive + T.replayed)).toBeTruthy()
    expect(send).toHaveBeenCalledTimes(2)
    expect(send.mock.calls[1][0]).toEqual(send.mock.calls[0][0])
    expect(onRecorded).toHaveBeenCalledTimes(1)
  })

  it('keeps the key on a gateway answer with its support code', async () => {
    const send = vi.fn<Send>().mockResolvedValue(refused(503, null))
    const { section } = renderPanel({ canDecide: true, send })
    fireEvent.click(within(section).getByRole('button', { name: T.approve }))
    const notice = await within(section).findByTestId('sales-contract-notice')
    await waitFor(() => expect(notice.textContent).toContain(T.retryPending))
    expect(notice.textContent).toContain(T.supportCode('corr-contract-1'))
    expect(within(section).getByTestId('sales-contract-retry')).toBeTruthy()
  })

  it('approves the pending discount at the version read', async () => {
    const send = vi.fn<Send>().mockResolvedValue(succeeded(APPROVED))
    const { section, onRecorded } = renderPanel({ canDecide: true, send })
    fireEvent.click(within(section).getByRole('button', { name: T.approve }))
    expect(await within(section).findByText(T.approved)).toBeTruthy()
    expect(send.mock.calls[0][0]).toEqual(expect.objectContaining({
      kind: 'discount-approval', contractId: PENDING.id, payload: { expectedVersion: '1' },
    }))
    expect(onRecorded).toHaveBeenCalledTimes(1)
  })

  it('requires a rejection reason and names the self-decision refusal, clearing the key and refreshing', async () => {
    const send = vi.fn<Send>().mockResolvedValue(refused(409, 'sales_contract_self_decision'))
    const { section, onRecorded } = renderPanel({ canDecide: true, send })
    fireEvent.click(within(section).getByRole('button', { name: T.reject }))
    expect(within(section).getByTestId('sales-contract-notice').textContent).toBe(T.invalidRejection)
    expect(send).not.toHaveBeenCalled()

    fireEvent.change(within(section).getByLabelText(T.rejectionReason), { target: { value: '  SENTETIK oran yüksek ' } })
    fireEvent.click(within(section).getByRole('button', { name: T.reject }))
    const notice = await within(section).findByText(T.problem.sales_contract_self_decision, { exact: false })
    expect(notice).toBeTruthy()
    expect(send.mock.calls[0][0]).toEqual(expect.objectContaining({
      kind: 'discount-rejection', payload: { expectedVersion: '1', reason: 'SENTETIK oran yüksek' },
    }))
    expect(within(section).queryByTestId('sales-contract-retry')).toBeNull()
    expect(onRecorded).toHaveBeenCalledTimes(1)
  })

  it('shows loading, empty, forbidden and unavailable reads without any contract', () => {
    renderPanel({ outcome: undefined })
    expect(screen.getByRole('region', { name: T.heading }).getAttribute('aria-busy')).toBe('true')
    cleanup()
    renderPanel({ outcome: { type: 'ok-single', data: [] } })
    expect(screen.getByTestId('sales-contract-empty').textContent).toBe(T.empty)
    cleanup()
    renderPanel({ outcome: { type: 'http', status: 403, body: null } })
    expect(screen.getByTestId('sales-contract-forbidden').textContent).toBe(T.forbidden)
    cleanup()
    renderPanel({ outcome: { type: 'http', status: 503, body: { status: 503, title: 'unavailable', correlationId: 'corr-read-1' } } })
    expect(screen.getByTestId('sales-contract-unreadable').textContent).toBe(T.serviceUnavailable)
    expect(screen.getByTestId('sales-contract-support-code').textContent).toBe(T.supportCode('corr-read-1'))
    expect(screen.queryByTestId('sales-contract-1')).toBeNull()
  })

  it('reads the form only as shape: ordered due dates, positive amounts and rates, bounded counts', () => {
    const base = {
      party: PARTY, account: ACCOUNT, currency: CURRENCY, validFrom: '2026-11-01', validThrough: '2027-10-31',
      installments: [{ dueDate: '2026-12-01', amount: '10' }], rules: [], clauses: '',
    }
    expect(readContractForm(base).ok).toBe(true)
    expect(readContractForm({ ...base, validThrough: '2026-10-01' })).toEqual({ ok: false, error: T.invalidValidity })
    expect(readContractForm({ ...base, installments: [] }).ok).toBe(false)
    expect(readContractForm({ ...base, installments: [{ dueDate: '2026-12-01', amount: '0' }] }))
      .toEqual({ ok: false, error: T.invalidInstallment(1) })
    expect(readContractForm({ ...base, installments: [{ dueDate: '2027-01-01', amount: '1' }, { dueDate: '2026-12-01', amount: '1' }] })
      .ok).toBe(false)
    expect(readContractForm({ ...base, rules: [{ kind: 'Line', label: 'SENTETIK', rate: '0' }] }).ok).toBe(false)
    expect(readContractForm({ ...base, clauses: 'x'.repeat(4001) }).ok).toBe(false)
  })
})
