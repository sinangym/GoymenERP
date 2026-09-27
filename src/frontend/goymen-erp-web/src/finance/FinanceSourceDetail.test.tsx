/**
 * Source detail — Record and Reverse kept apart; real pending promises.
 * Component evidence; SENTETIK/NOT_ENV; no API/SQL/browser.
 */

import { describe, expect, it } from 'vitest'
import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import { FINANCE_REVERSE_DIRTY_SCOPE, FinanceSourceDetail } from './FinanceSourceDetail'
import {
  AMOUNT_REDACTED,
  AMOUNT_UNAVAILABLE,
  DETAIL,
  NO_SOURCE_NUMBER,
  POS_BEARER_LABEL,
  POS_CUSTOMER_BEARER_PENDING,
  POS_DETAIL,
  POS_POSTING_PENDING,
  RECORD_ACTION,
  RECORD_CONFIRM,
  RECORD_DONE,
  REREAD,
  REREAD_AFTER_CONFLICT,
  REREAD_AFTER_SUCCESS,
  REVERSE_ACTION,
  REVERSE_CONFIRM,
  REVERSE_DATE,
  REVERSE_REASON,
  REVERSE_REASON_REQUIRED,
  UNALLOCATED_LABEL,
  UNKNOWN_TITLE,
  financeRef,
} from './financeText'
import { FINANCE_CAPABILITY, receiptDetailPath } from './capabilities'
import { EXAMPLE_CURRENCY, EXAMPLE_SOURCE, NEGATIVE_EXAMPLE, NORMAL_EXAMPLE } from './contractExamples'
import {
  COMPANY_A,
  COMPANY_B,
  NO_REPLY,
  currentShell,
  bankPage,
  body,
  found,
  recordingPort,
  renderFinance,
  shellEvent,
  shellFor,
  sourceDetail,
  succeeded,
  type ReadCall,
  type ReadResponder,
} from './testHarness'
import type { PortReply } from './port'

const OTHER_SOURCE = 'eeeeeeee-0000-4000-8000-000000000005'
const REVERSAL_SOURCE = 'eeeeeeee-0000-4000-8000-0000aaaa1111'
const REVERSED_SOURCE = 'eeeeeeee-0000-4000-8000-0000bbbb2222'

function detailReads(reads: readonly ReadCall[]): number {
  return reads.filter((read) => read.method === 'sourceDetail').length
}

/** Detail replies in order; the last one repeats. */
function detailSequence(...replies: PortReply[]): ReadResponder {
  let index = 0
  return (method, scope) => {
    if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
    if (method !== 'sourceDetail') return NO_REPLY
    const reply = replies[Math.min(index, replies.length - 1)]
    index += 1
    return reply
  }
}

const RECORDED = sourceDetail(COMPANY_A, {
  source: { stage: 'Recorded', version: '9007199254740994' },
  recordedAtUtc: '2026-09-13T08:00:00Z',
  unallocated: NEGATIVE_EXAMPLE.unavailableAllocation,
})

describe('FinanceSourceDetail — Record', () => {
  it('records only after confirmation, with its own request; a failed re-read never re-sends Record', async () => {
    const { port, commands, reads } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A)), NO_REPLY))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })

    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    expect(commands).toHaveLength(0)
    const confirm = screen.getByRole('button', { name: RECORD_CONFIRM })
    expect(document.activeElement).toBe(confirm)
    fireEvent.click(confirm)

    expect(commands).toHaveLength(1)
    expect(commands[0].method).toBe('record')
    expect(commands[0].request).toEqual(NORMAL_EXAMPLE.record)
    expect((screen.getByRole('button', { name: RECORD_ACTION }) as HTMLButtonElement).disabled).toBe(true)

    await commands[0].resolve(succeeded(RECORDED))
    await screen.findByText(REREAD_AFTER_SUCCESS)
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()

    const detailReadsBefore = reads.filter((read) => read.method === 'sourceDetail').length
    fireEvent.click(screen.getByRole('button', { name: REREAD }))
    await waitFor(() => expect(reads.filter((read) => read.method === 'sourceDetail').length).toBe(detailReadsBefore + 1))
    expect(commands).toHaveLength(1)
  })

  it('a successful re-read shows the recorded source and no second Record action', async () => {
    const { port, commands } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A)), found(RECORDED)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(succeeded(RECORDED))
    await screen.findByText(RECORD_DONE)
    await screen.findByText('Kesin')
    expect(screen.getByText(UNALLOCATED_LABEL).nextElementSibling?.textContent).toBe(AMOUNT_UNAVAILABLE)
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()
  })

  it('a re-read that still shows the Draft after a successful Record does not offer Record again', async () => {
    const draft = found(sourceDetail(COMPANY_A))
    const { port, commands, reads } = recordingPort(detailSequence(draft, draft))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(succeeded(RECORDED))
    await screen.findByText(RECORD_DONE)
    await waitFor(() => expect(reads.filter((read) => read.method === 'sourceDetail')).toHaveLength(2))
    screen.getByText('Taslak')
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()
    expect(commands).toHaveLength(1)
  })

  it('a VersionConflict re-reads once and keeps Record closed while the conflicting version is still shown', async () => {
    const { port, commands, reads } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A))))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(body(NEGATIVE_EXAMPLE.conflictResult))

    await screen.findByText(REREAD_AFTER_CONFLICT)
    await waitFor(() => expect(detailReads(reads)).toBe(2))
    expect((screen.getByRole('button', { name: RECORD_ACTION }) as HTMLButtonElement).disabled).toBe(true)
    expect(commands).toHaveLength(1)
  })

  it('after a VersionConflict only the freshly read version can be confirmed, as a new attempt', async () => {
    const fresh = found(sourceDetail(COMPANY_A, { source: { version: 'etag-v2' } }))
    const { port, commands } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A)), fresh))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(body(NEGATIVE_EXAMPLE.conflictResult))

    await waitFor(() => expect(screen.queryByText(REREAD_AFTER_CONFLICT)).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    expect(commands).toHaveLength(2)
    expect(commands[1].request).toEqual({ draftId: EXAMPLE_SOURCE, expectedVersion: 'etag-v2' })
    expect(commands[1].key).not.toBe(commands[0].key)
  })

  it.each([
    ['another source', sourceDetail(COMPANY_A, { source: { id: OTHER_SOURCE, stage: 'Recorded' } })],
    ['the same source still in Draft', sourceDetail(COMPANY_A)],
  ])('a Succeeded body about %s does not settle Record', async (_label, resource) => {
    const { port, commands } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A))))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(succeeded(resource))
    await screen.findByText(UNKNOWN_TITLE)
    expect(screen.queryByText(RECORD_DONE)).toBeNull()
  })

  it('Record re-reads once, while a reversal proposal leaves the original source untouched', async () => {
    const recorded = found(RECORDED)
    const { port, commands, reads } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A)), recorded))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })

    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(succeeded(RECORDED))
    fireEvent.click(await screen.findByRole('button', { name: REVERSE_ACTION }))
    fireEvent.change(screen.getByLabelText(REVERSE_DATE), { target: { value: '2026-09-13' } })
    fireEvent.change(screen.getByLabelText(REVERSE_REASON), { target: { value: 'SENTETIK correction' } })
    fireEvent.click(screen.getByRole('button', { name: REVERSE_CONFIRM }))
    await commands[1].resolve(succeeded({
      id: '11111111-1111-4111-8111-111111111111',
      originalSourceId: EXAMPLE_SOURCE,
      occurredOn: '2026-09-13',
      reason: 'SENTETIK correction',
      status: 'Pending',
      proposedBy: 'maker',
      proposedAtUtc: '2026-09-21T12:00:00+00:00',
      decidedBy: null,
      decidedAtUtc: null,
      decisionReason: null,
      reversalSourceId: null,
      version: '1',
    }))

    await waitFor(() => expect(detailReads(reads)).toBe(2))
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100))
    })
    expect(detailReads(reads)).toBe(2)
  })
})

describe('FinanceSourceDetail — bound to the requested source', () => {
  it('a detail body about another source is malformed, not shown', async () => {
    const { port } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A, { source: { id: OTHER_SOURCE } }))))
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    await waitFor(() => expect(container.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()
  })
})

describe('FinanceSourceDetail — dirty work reaches the shell gate', () => {
  it('a typed reversal makes a company switch ask instead of discarding it', async () => {
    const { port } = recordingPort(detailSequence(found(RECORDED)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    fireEvent.click(await screen.findByRole('button', { name: REVERSE_ACTION }))
    expect(currentShell().dirtyScopes).not.toContain(FINANCE_REVERSE_DIRTY_SCOPE)
    fireEvent.change(screen.getByLabelText(REVERSE_REASON), { target: { value: 'SENTETIK' } })
    await waitFor(() => expect(currentShell().dirtyScopes).toContain(FINANCE_REVERSE_DIRTY_SCOPE))

    await shellEvent({ type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: COMPANY_B } })
    expect(currentShell().phase).toBe('confirming-switch')
    expect((screen.getByLabelText(REVERSE_REASON) as HTMLTextAreaElement).value).toBe('SENTETIK')
  })
})

describe('FinanceSourceDetail — reversal proposal', () => {
  it('requires a reason and date, then sends the exact proposal body', async () => {
    const { port, commands } = recordingPort(detailSequence(found(RECORDED)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()

    fireEvent.click(await screen.findByRole('button', { name: REVERSE_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: REVERSE_CONFIRM }))
    screen.getByText(REVERSE_REASON_REQUIRED)
    expect(commands).toHaveLength(0)

    fireEvent.change(screen.getByLabelText(REVERSE_DATE), { target: { value: '2026-09-13' } })
    fireEvent.change(screen.getByLabelText(REVERSE_REASON), { target: { value: '  SENTETIK correction ' } })
    fireEvent.click(screen.getByRole('button', { name: REVERSE_CONFIRM }))
    expect(commands).toHaveLength(1)
    expect(commands[0].method).toBe('proposeReversal')
    expect(commands[0].request).toEqual(NORMAL_EXAMPLE.reverse)
    expect((screen.getByLabelText(REVERSE_REASON) as HTMLTextAreaElement).disabled).toBe(true)
  })
})

describe('FinanceSourceDetail — read states', () => {
  it.each([
    ['Denied', body({ outcome: 'Denied', resource: null }), 'forbidden'],
    ['NotFound', body({ outcome: 'NotFound', resource: null }), 'not-found'],
    ['Unavailable', body({ outcome: 'Unavailable', resource: null }), 'service-unavailable'],
    ['undecodable', found({ ...sourceDetail(COMPANY_A), extra: true }), 'malformed-response'],
    ['no reply', NO_REPLY, 'network-error'],
  ])('%s renders the shared %s block and no actions', async (_label, reply, state) => {
    const { port } = recordingPort(detailSequence(reply))
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    await waitFor(() => expect(container.querySelector(`[data-state="${state}"]`)).not.toBeNull())
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()
  })

  it('without a port adapter the screen fails closed', () => {
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port: null })
    expect(container.querySelector('[data-state="service-unavailable"]')).not.toBeNull()
  })

  it('a redacted recorded amount shows no digits in the amount cells', async () => {
    const { port } = recordingPort(
      detailSequence(found(sourceDetail(COMPANY_A, { source: { stage: 'Recorded', amount: NEGATIVE_EXAMPLE.redactedAmount }, unallocated: { knowledge: 'Known', amount: NEGATIVE_EXAMPLE.redactedAmount } }))),
    )
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port })
    expect(await screen.findAllByText(AMOUNT_REDACTED)).toHaveLength(2)
  })
})

// Business 5745076917: no technical id as final presentation — the same guarantee the list pins (#356), here on
// the detail's own facts. One reference shape everywhere: `financeRef`, the last 8 characters.
describe('FinanceSourceDetail — references and the currency code', () => {
  const WITH_REFS = sourceDetail(COMPANY_A, {
    // No number series is decided (Q2), so the server's "source number" is the record's own id.
    source: { sourceNumber: EXAMPLE_SOURCE },
    reversalSourceId: REVERSAL_SOURCE,
    reversesSourceId: REVERSED_SOURCE,
  })

  /** Answers the detail, the bank list and the Settings currency candidates. */
  const responder = (method: string, scope: { companyId: string }): PortReply => {
    if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
    if (method === 'bankAccountCurrencyCandidates') {
      return found({
        companyId: scope.companyId,
        items: [{ currencyId: EXAMPLE_CURRENCY, code: 'TRY', displayLabel: 'SENTETIK Lira' }],
      })
    }
    if (method !== 'sourceDetail') return NO_REPLY
    return found(WITH_REFS)
  }

  /** The value of a fact, read through its own term — the screen's shape, not an internal handle. */
  const factValue = (term: string): string | undefined => screen.getByText(term).nextElementSibling?.textContent ?? undefined

  it('shows the currency CODE to a caller who may read the candidates, and never a full id as text', async () => {
    const { port, reads } = recordingPort(responder)
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, {
      port,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsPropose]),
      router: true,
    })

    await waitFor(() => expect(factValue(DETAIL.currency)).toBe('TRY'))
    expect(reads.map((read) => read.method)).toContain('bankAccountCurrencyCandidates')
    expect(factValue(DETAIL.sourceNumber)).toBe(`${NO_SOURCE_NUMBER} · ${financeRef(EXAMPLE_SOURCE)}`)
    expect(factValue(DETAIL.reversedBy)).toBe(financeRef(REVERSAL_SOURCE))
    expect(factValue(DETAIL.reverses)).toBe(financeRef(REVERSED_SOURCE))
    expect(screen.getByRole('link', { name: financeRef(REVERSAL_SOURCE) }).getAttribute('href')).toBe(
      receiptDetailPath(REVERSAL_SOURCE),
    )
    expect(screen.getByRole('link', { name: financeRef(REVERSED_SOURCE) }).getAttribute('href')).toBe(
      receiptDetailPath(REVERSED_SOURCE),
    )
    for (const id of [EXAMPLE_SOURCE, EXAMPLE_CURRENCY, REVERSAL_SOURCE, REVERSED_SOURCE]) {
      expect(container.textContent).not.toContain(id)
    }
  })

  it('shows the labelled currency reference — never a guessed code — without that permission, and reads no candidates', async () => {
    const { port, reads } = recordingPort(responder)
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, { port, router: true })

    await waitFor(() => expect(factValue(DETAIL.currency)).toBe(`Para birimi ${financeRef(EXAMPLE_CURRENCY)}`))
    expect(reads.map((read) => read.method)).not.toContain('bankAccountCurrencyCandidates')
    expect(container.textContent).not.toContain('TRY')
    expect(container.textContent).not.toContain(EXAMPLE_CURRENCY)
  })

  it('a real source number is shown as given, and a source with no reversal relation shows neither link', async () => {
    const { port } = recordingPort((method, scope) =>
      method === 'sourceDetail' ? found(sourceDetail(COMPANY_A)) : responder(method, scope),
    )
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} offerRecordReverse />, {
      port,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsPropose]),
    })

    await waitFor(() => expect(factValue(DETAIL.currency)).toBe('TRY'))
    expect(factValue(DETAIL.sourceNumber)).toBe('SENTETIK-DRAFT-1')
    expect(factValue(DETAIL.reversedBy)).toBe(DETAIL.none)
    expect(factValue(DETAIL.reverses)).toBe(DETAIL.none)
    expect(container.textContent).not.toContain('ref. …')
  })
})

describe('FinanceSourceDetail — DEC-FIN-014 customer POS collection', () => {
  const visible = (amount: string) => ({ disclosure: 'Visible', money: { amount, currencyId: EXAMPLE_CURRENCY } })
  const POS_RECORDED = sourceDetail(COMPANY_A, {
    source: { stage: 'Recorded', version: '9007199254740994' },
    recordedAtUtc: '2026-09-13T08:00:00Z',
    unallocated: { knowledge: 'Known', amount: visible('1000') },
    pos: { commissionBearer: 'Company', commission: visible('25.5'), bankNet: visible('974.5') },
  })

  function factValue(term: string): string | null | undefined {
    return screen.getByText(term).nextElementSibling?.textContent
  }

  it('shows the gross, the commission and the bank net apart, and says the journal is not made yet', async () => {
    const { port } = recordingPort(detailSequence(found(POS_RECORDED)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    await screen.findByText(POS_DETAIL.gross)
    expect(factValue(POS_DETAIL.gross)).toBe('1.000')
    expect(factValue(POS_DETAIL.commission)).toBe('25,5')
    expect(factValue(POS_DETAIL.bankNet)).toBe('974,5')
    expect(factValue(POS_DETAIL.bearer)).toBe(POS_BEARER_LABEL.Company)
    // The customer's debt closes at the gross: the unallocated amount is the gross, never the net.
    expect(factValue(UNALLOCATED_LABEL)).toBe('1.000')
    screen.getByText(POS_POSTING_PENDING)
    expect(screen.queryByText(POS_CUSTOMER_BEARER_PENDING)).toBeNull()
  })

  it('a customer bearer is shown as recorded, not applied', async () => {
    const detail = { ...POS_RECORDED, pos: { ...(POS_RECORDED.pos as object), commissionBearer: 'Customer' } }
    const { port } = recordingPort(detailSequence(found(detail)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    await screen.findByText(POS_CUSTOMER_BEARER_PENDING)
    expect(factValue(POS_DETAIL.bearer)).toBe(POS_BEARER_LABEL.Customer)
  })

  it('without the amount grant none of the three amounts is shown', async () => {
    const redacted = { disclosure: 'Redacted', money: null }
    const detail = sourceDetail(COMPANY_A, {
      source: { amount: redacted },
      pos: { commissionBearer: 'Company', commission: redacted, bankNet: redacted },
    })
    const { port } = recordingPort(detailSequence(found(detail)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    await screen.findByText(POS_DETAIL.gross)
    expect([factValue(POS_DETAIL.gross), factValue(POS_DETAIL.commission), factValue(POS_DETAIL.bankNet)])
      .toEqual([AMOUNT_REDACTED, AMOUNT_REDACTED, AMOUNT_REDACTED])
  })

  it('a recorded POS collection read without the amount grant shows no gross, unallocated, commission or net', async () => {
    // K3 5330956606 P2-1: the server now redacts unallocated.amount too; the screen shows it like any redacted amount.
    const redacted = { disclosure: 'Redacted', money: null }
    const detail = sourceDetail(COMPANY_A, {
      source: { stage: 'Recorded', version: '9007199254740994', amount: redacted },
      recordedAtUtc: '2026-09-13T08:00:00Z',
      unallocated: { knowledge: 'Known', amount: redacted },
      pos: { commissionBearer: 'Company', commission: redacted, bankNet: redacted },
    })
    const { port } = recordingPort(detailSequence(found(detail)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    await screen.findByText(POS_DETAIL.gross)
    expect([factValue(UNALLOCATED_LABEL), factValue(POS_DETAIL.gross), factValue(POS_DETAIL.commission), factValue(POS_DETAIL.bankNet)])
      .toEqual([AMOUNT_REDACTED, AMOUNT_REDACTED, AMOUNT_REDACTED, AMOUNT_REDACTED])
    expect(document.body.textContent).not.toMatch(/1\.000|974|25,5/)
  })

  it('three amounts that do not add up are not shown as money', async () => {
    const detail = { ...POS_RECORDED, pos: { commissionBearer: 'Company', commission: visible('25.5'), bankNet: visible('975') } }
    const { port } = recordingPort(detailSequence(found(detail)))
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    await waitFor(() => expect(container.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText(POS_DETAIL.gross)).toBeNull()
    expect(document.body.textContent).not.toMatch(/975/)
  })

  it('a bank transfer shows no POS section', async () => {
    const { port } = recordingPort(detailSequence(found(RECORDED)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    await screen.findByText('Kesin')
    expect(screen.queryByText(POS_DETAIL.gross)).toBeNull()
    expect(screen.queryByText(POS_POSTING_PENDING)).toBeNull()
  })
})
