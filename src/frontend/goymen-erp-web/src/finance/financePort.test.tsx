/**
 * `useReceiptCommand` guard that a DOM click cannot reach: React re-renders between two
 * discrete events, so the component state already blocks a second click. Two submits in
 * ONE handler share a stale render — only the synchronous store reservation stops the
 * second request. Component evidence only.
 */

import { describe, expect, it } from 'vitest'
import { fireEvent, screen } from '@testing-library/react'
import { useReceiptCommand } from './financePort'
import { NORMAL_EXAMPLE } from './contractExamples'
import { NO_REPLY, recordingPort, renderFinance } from './testHarness'

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
