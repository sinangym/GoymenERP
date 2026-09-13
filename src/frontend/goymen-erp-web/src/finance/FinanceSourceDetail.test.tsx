/**
 * Source detail — Record and Reverse kept apart; real pending promises.
 * Component evidence; SENTETIK/NOT_ENV; no API/SQL/browser.
 */

import { describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { FinanceSourceDetail } from './FinanceSourceDetail'
import {
  AMOUNT_REDACTED,
  AMOUNT_UNAVAILABLE,
  RECORD_ACTION,
  RECORD_CONFIRM,
  RECORD_DONE,
  REFUSAL_TITLE,
  REREAD,
  REREAD_AFTER_SUCCESS,
  REVERSE_ACTION,
  REVERSE_CONFIRM,
  REVERSE_DATE,
  REVERSE_REASON,
  REVERSE_REASON_REQUIRED,
  UNALLOCATED_LABEL,
} from './financeText'
import { EXAMPLE_SOURCE, NEGATIVE_EXAMPLE, NORMAL_EXAMPLE } from './contractExamples'
import {
  COMPANY_A,
  NO_REPLY,
  bankPage,
  body,
  found,
  recordingPort,
  renderFinance,
  sourceDetail,
  succeeded,
  type ReadResponder,
} from './testHarness'
import type { PortReply } from './port'

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
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })

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
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
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
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(succeeded(RECORDED))
    await screen.findByText(RECORD_DONE)
    await waitFor(() => expect(reads.filter((read) => read.method === 'sourceDetail')).toHaveLength(2))
    screen.getByText('Taslak')
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()
    expect(commands).toHaveLength(1)
  })

  it('a stale version is a refusal to re-read, not an overwrite', async () => {
    const { port, commands } = recordingPort(detailSequence(found(sourceDetail(COMPANY_A))))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    fireEvent.click(await screen.findByRole('button', { name: RECORD_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: RECORD_CONFIRM }))
    await commands[0].resolve(body(NEGATIVE_EXAMPLE.conflictResult))
    await screen.findByText(REFUSAL_TITLE['version-conflict'])
    expect(commands).toHaveLength(1)
  })
})

describe('FinanceSourceDetail — Reverse', () => {
  it('requires a reason and date, sends only identity/version/date/reason', async () => {
    const { port, commands } = recordingPort(detailSequence(found(RECORDED)))
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()

    fireEvent.click(await screen.findByRole('button', { name: REVERSE_ACTION }))
    fireEvent.click(screen.getByRole('button', { name: REVERSE_CONFIRM }))
    screen.getByText(REVERSE_REASON_REQUIRED)
    expect(commands).toHaveLength(0)

    fireEvent.change(screen.getByLabelText(REVERSE_DATE), { target: { value: '2026-09-13' } })
    fireEvent.change(screen.getByLabelText(REVERSE_REASON), { target: { value: '  SENTETIK correction ' } })
    fireEvent.click(screen.getByRole('button', { name: REVERSE_CONFIRM }))
    expect(commands).toHaveLength(1)
    expect(commands[0].method).toBe('reverse')
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
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    await waitFor(() => expect(container.querySelector(`[data-state="${state}"]`)).not.toBeNull())
    expect(screen.queryByRole('button', { name: RECORD_ACTION })).toBeNull()
  })

  it('without a port adapter the screen fails closed', () => {
    const { container } = renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port: null })
    expect(container.querySelector('[data-state="service-unavailable"]')).not.toBeNull()
  })

  it('a redacted recorded amount shows no digits in the amount cells', async () => {
    const { port } = recordingPort(
      detailSequence(found(sourceDetail(COMPANY_A, { source: { stage: 'Recorded', amount: NEGATIVE_EXAMPLE.redactedAmount }, unallocated: { knowledge: 'Known', amount: NEGATIVE_EXAMPLE.redactedAmount } }))),
    )
    renderFinance(<FinanceSourceDetail sourceId={EXAMPLE_SOURCE} />, { port })
    expect(await screen.findAllByText(AMOUNT_REDACTED)).toHaveLength(2)
  })
})
