/**
 * `useReceiptCommand` guard that a DOM click cannot reach: React re-renders between two
 * discrete events, so the component state already blocks a second click. Two submits in
 * ONE handler share a stale render — only the synchronous store reservation stops the
 * second request. Component evidence only.
 */

import { describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { useReceiptCommand, useReversalCommand } from './financePort'
import { NORMAL_EXAMPLE } from './contractExamples'
import {
  COMPANY_A,
  COMPANY_B,
  NO_REPLY,
  commitContext,
  failedOutcome,
  recordingPort,
  renderFinance,
  shellFor,
} from './testHarness'

const REVERSAL_REQUEST = {
  operation: 'propose' as const,
  body: {
    sourceId: NORMAL_EXAMPLE.record.draftId,
    expectedVersion: '2',
    occurredOn: '2026-09-22',
    reason: 'SENTETIK yanlis tahsilat',
  },
}

function DoubleSubmit() {
  const command = useReceiptCommand('record', NORMAL_EXAMPLE.record.draftId)
  return (
    <button
      type="button"
      onClick={() => {
        command.submit({ operation: 'record', body: NORMAL_EXAMPLE.record })
        command.submit({ operation: 'record', body: NORMAL_EXAMPLE.record })
      }}
    >
      twice
    </button>
  )
}

function WrongTarget() {
  const command = useReceiptCommand('record', 'another-target')
  return (
    <button type="button" onClick={() => command.submit({ operation: 'record', body: NORMAL_EXAMPLE.record })}>
      wrong
    </button>
  )
}

function ReversalProbe({ twice = false }: { readonly twice?: boolean }) {
  const command = useReversalCommand('propose', REVERSAL_REQUEST.body.sourceId)
  return (
    <>
      <output aria-label="reversal phase">{command.state.phase}</output>
      <button
        type="button"
        onClick={() => {
          command.submit(REVERSAL_REQUEST)
          if (twice) command.submit(REVERSAL_REQUEST)
        }}
      >
        propose reversal
      </button>
      <button type="button" onClick={() => command.resend()}>
        resend reversal
      </button>
    </>
  )
}

describe('useReceiptCommand', () => {
  it('two submits from one stale render issue ONE request', () => {
    const { port, commands } = recordingPort(() => NO_REPLY)
    renderFinance(<DoubleSubmit />, { port })
    fireEvent.click(screen.getByRole('button', { name: 'twice' }))
    expect(commands).toHaveLength(1)
  })

  it('refuses a request for a record other than the one the command is bound to', () => {
    const { port, commands } = recordingPort(() => NO_REPLY)
    renderFinance(<WrongTarget />, { port })
    fireEvent.click(screen.getByRole('button', { name: 'wrong' }))
    expect(commands).toHaveLength(0)
  })
})

describe('useReversalCommand', () => {
  it('reserves the proposal key synchronously so one stale render issues one request', () => {
    const { port, commands } = recordingPort(() => NO_REPLY)
    renderFinance(<ReversalProbe twice />, { port })

    fireEvent.click(screen.getByRole('button', { name: 'propose reversal' }))

    expect(commands).toHaveLength(1)
    expect(commands[0]).toMatchObject({ method: 'proposeReversal', scope: { companyId: COMPANY_A } })
  })

  it('turns an unmounted pending proposal into an unknown attempt and resends the same key', async () => {
    const { port, commands } = recordingPort(() => NO_REPLY)
    const view = renderFinance(<ReversalProbe />, { port })
    fireEvent.click(screen.getByRole('button', { name: 'propose reversal' }))
    const originalKey = commands[0].key

    view.remount(<></>)
    view.remount(<ReversalProbe />)
    await waitFor(() => expect(screen.getByLabelText('reversal phase').textContent).toBe('unknown'))
    fireEvent.click(screen.getByRole('button', { name: 'resend reversal' }))

    expect(commands).toHaveLength(2)
    expect(commands[1].key).toBe(originalKey)
    expect(commands[1].request).toEqual(commands[0].request)
  })

  it('keeps a late company A answer out of B and out of a newer A context revision', async () => {
    const { port, commands } = recordingPort(() => NO_REPLY)
    renderFinance(<ReversalProbe />, { port })
    fireEvent.click(screen.getByRole('button', { name: 'propose reversal' }))

    await commitContext(shellFor(COMPANY_B, 2))
    await waitFor(() => expect(screen.getByLabelText('reversal phase').textContent).toBe('idle'))
    await commands[0].resolve(NO_REPLY)
    expect(screen.getByLabelText('reversal phase').textContent).toBe('idle')

    await commitContext(shellFor(COMPANY_A, 3))
    await waitFor(() => expect(screen.getByLabelText('reversal phase').textContent).toBe('idle'))
    expect(commands).toHaveLength(1)
  })

  it('does not settle an unknown attempt from a non-replayed refusal', async () => {
    const { port, commands } = recordingPort(() => NO_REPLY)
    renderFinance(<ReversalProbe />, { port })
    fireEvent.click(screen.getByRole('button', { name: 'propose reversal' }))
    await commands[0].resolve(NO_REPLY)
    await waitFor(() => expect(screen.getByLabelText('reversal phase').textContent).toBe('unknown'))

    fireEvent.click(screen.getByRole('button', { name: 'resend reversal' }))
    await commands[1].resolve(failedOutcome('Denied'))
    await waitFor(() => expect(screen.getByLabelText('reversal phase').textContent).toBe('unknown'))
    expect(commands[1].key).toBe(commands[0].key)

    fireEvent.click(screen.getByRole('button', { name: 'resend reversal' }))
    await commands[2].resolve(failedOutcome('Denied', { isReplay: true }))
    await waitFor(() => expect(screen.getByLabelText('reversal phase').textContent).toBe('refused'))
    expect(commands[2].key).toBe(commands[0].key)
  })
})
