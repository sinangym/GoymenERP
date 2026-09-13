/**
 * FS0/FS1 draft form — DOM tests with REAL pending promises (the recording port resolves
 * only when the test says so). Component evidence; SENTETIK/NOT_ENV; no API/SQL/browser.
 */

import { describe, expect, it, vi } from 'vitest'
import { QueryClientProvider } from '@tanstack/react-query'
import { ShellStateContext } from '../shell/shellContext'
import { FinancePortProvider } from './financePort'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { ManualReceiptDraftForm } from './ManualReceiptDraftForm'
import {
  ABANDON,
  CONFLICT_TITLE,
  DRAFT_SAVED,
  FIELD_ERROR,
  FIELD_LABEL,
  NO_USABLE_BANK_ACCOUNT,
  PROFILE_MISSING,
  REFUSAL_AFTER_UNKNOWN,
  RESEND_SAME,
  SAVE_DRAFT,
  UNKNOWN_TITLE,
  issueText,
} from './financeText'
import { EXAMPLE_ACCOUNT, EXAMPLE_BANK, EXAMPLE_PARTY, EXAMPLE_PROFILE, NORMAL_EXAMPLE } from './contractExamples'
import {
  COMPANY_A,
  COMPANY_B,
  NO_REPLY,
  bankAccount,
  bankPage,
  body,
  commitContext,
  failedOutcome,
  found,
  recordingPort,
  renderFinance,
  shellFor,
  sourceDetail,
  succeeded,
} from './testHarness'
import type { DraftSaveRequest } from './contract'

const PARTY = { partyId: EXAMPLE_PARTY, companyAccountId: EXAMPLE_ACCOUNT }

function banksFor(items?: (company: string) => Record<string, unknown>[]) {
  return recordingPort((method, scope) =>
    method === 'listBankAccounts' ? found(bankPage(scope.companyId, items?.(scope.companyId))) : NO_REPLY,
  )
}

async function fillValid(amount = '1000') {
  const select = await screen.findByLabelText(FIELD_LABEL.treasuryAccountId)
  fireEvent.change(select, { target: { value: EXAMPLE_BANK } })
  fireEvent.change(screen.getByLabelText(FIELD_LABEL.amount), { target: { value: amount } })
  fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
  fireEvent.change(screen.getByLabelText(FIELD_LABEL.description), { target: { value: 'SENTETIK/NOT_ENV' } })
}

function saveButton() {
  return screen.getByRole('button', { name: SAVE_DRAFT }) as HTMLButtonElement
}

describe('ManualReceiptDraftForm — normal', () => {
  it('sends exactly the example create request once, freezes while pending, and says "no money effect"', async () => {
    const { port, commands } = banksFor()
    const onSaved = vi.fn()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} onSaved={onSaved} />, { port })
    await fillValid()

    fireEvent.click(saveButton())
    expect(commands).toHaveLength(1)
    expect(commands[0].method).toBe('draftSave')
    expect(commands[0].scope).toEqual({ companyId: COMPANY_A })
    expect(commands[0].request).toEqual(NORMAL_EXAMPLE.draftSave)

    // Genuinely in flight: nothing resolved yet.
    expect(saveButton().disabled).toBe(true)
    expect((screen.getByLabelText(FIELD_LABEL.amount) as HTMLInputElement).disabled).toBe(true)
    expect(screen.getByRole('button', { name: SAVE_DRAFT }).closest('form')?.getAttribute('aria-busy')).toBe('true')

    await commands[0].resolve(body(NORMAL_EXAMPLE.draftSaveResult))
    expect((await screen.findByText(DRAFT_SAVED)).getAttribute('role')).toBe('status')
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(onSaved.mock.calls[0][0].source.stage).toBe('Draft')
    // A saved create is closed: a second click would be a second draft.
    expect(saveButton().disabled).toBe(true)
  })

  it('a second submit while the first is pending issues no command', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid()
    const form = saveButton().closest('form') as HTMLFormElement
    fireEvent.submit(form)
    fireEvent.submit(form)
    expect(commands).toHaveLength(1)
  })

  it('an update sends draft id + version and reuses the draft profile', async () => {
    const { port, commands } = banksFor()
    const decoded = (await import('./contract')).decodeSourceDetail(sourceDetail(COMPANY_A), COMPANY_A)
    if (!decoded.ok) throw new Error('fixture')
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={null} draft={decoded.value} />, { port })
    await screen.findByLabelText(FIELD_LABEL.amount)
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.amount), { target: { value: '1000,5' } })
    fireEvent.click(saveButton())
    const request = commands[0].request as DraftSaveRequest
    expect([request.draftId, request.expectedVersion]).toEqual([NORMAL_EXAMPLE.record.draftId, '9007199254740993'])
    expect(request.preparation.profile).toEqual(EXAMPLE_PROFILE)
    expect(request.preparation.money.amount).toBe('1000.5')
  })
})

describe('ManualReceiptDraftForm — closed and client-invalid paths send nothing', () => {
  it('without a server profile a create is closed and explains why', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={null} />, { port })
    await fillValid()
    screen.getByText(PROFILE_MISSING)
    expect(saveButton().disabled).toBe(true)
    fireEvent.submit(saveButton().closest('form') as HTMLFormElement)
    expect(commands).toHaveLength(0)
  })

  it('a dotted amount is refused on the field, focused, and nothing is sent', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid('1.000')
    fireEvent.click(saveButton())
    const amount = screen.getByLabelText(FIELD_LABEL.amount)
    expect(amount.getAttribute('aria-invalid')).toBe('true')
    expect(document.activeElement).toBe(amount)
    screen.getByText(FIELD_ERROR.format)
    expect(commands).toHaveLength(0)
  })

  it('inactive or unusable bank accounts are not offered', async () => {
    const { port } = banksFor((company) => [
      bankAccount(company, { isActive: false }),
      bankAccount(company, { id: 'cccccccc-0000-4000-8000-000000000003', isUsableForReceipt: false }),
    ])
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await screen.findByText(NO_USABLE_BANK_ACCOUNT)
    expect(screen.getAllByRole('option')).toHaveLength(1) // the placeholder only
  })
})

describe('ManualReceiptDraftForm — unknown outcome keeps key, company, actor and payload', () => {
  it('no reply → frozen; resend uses the SAME key and body; abandon + identical submit reuses it; an edit mints a new one', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(NO_REPLY)

    await screen.findByText(UNKNOWN_TITLE)
    expect((screen.getByLabelText(FIELD_LABEL.amount) as HTMLInputElement).disabled).toBe(true)
    expect(saveButton().disabled).toBe(true)
    expect(commands).toHaveLength(1) // nothing resent on its own

    fireEvent.click(screen.getByRole('button', { name: RESEND_SAME }))
    expect(commands).toHaveLength(2)
    expect(commands[1].key).toBe(commands[0].key)
    expect(commands[1].request).toEqual(commands[0].request)
    expect(commands[1].scope).toEqual(commands[0].scope)

    await commands[1].reject() // transport failure on the resend: still unknown
    fireEvent.click(await screen.findByRole('button', { name: ABANDON }))
    expect((screen.getByLabelText(FIELD_LABEL.amount) as HTMLInputElement).disabled).toBe(false)

    fireEvent.click(saveButton())
    expect(commands[2].key).toBe(commands[0].key)
    await commands[2].resolve(NO_REPLY)
    fireEvent.click(await screen.findByRole('button', { name: ABANDON }))

    fireEvent.change(screen.getByLabelText(FIELD_LABEL.amount), { target: { value: '1001' } })
    fireEvent.click(saveButton())
    expect(commands[3].key).not.toBe(commands[0].key)
  })

  it('an undecodable success body is not a success', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(body({ ...NORMAL_EXAMPLE.draftSaveResult, balance: '1000' }))
    await screen.findByText(UNKNOWN_TITLE)
    expect(screen.queryByText(DRAFT_SAVED)).toBeNull()
  })

  it('a non-replayed refusal of the resend does not settle the first send', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(failedOutcome('Unavailable'))
    fireEvent.click(await screen.findByRole('button', { name: RESEND_SAME }))
    await commands[1].resolve(failedOutcome('Denied'))
    await screen.findByText(REFUSAL_AFTER_UNKNOWN)
    screen.getByText(UNKNOWN_TITLE)
    expect(saveButton().disabled).toBe(true)
  })

  it('IdempotencyConflict freezes without minting a new key or claiming nothing was written', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(failedOutcome('IdempotencyConflict'))
    await screen.findByText(CONFLICT_TITLE)
    expect(saveButton().disabled).toBe(true)
    expect(screen.queryByRole('button', { name: RESEND_SAME })).toBeNull()
    expect(commands).toHaveLength(1)
  })

  it('a validation refusal shows the owner issue text and unfreezes the form', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid('1,1234')
    fireEvent.click(saveButton())
    await commands[0].resolve(failedOutcome('ValidationFailed', { issues: [{ field: 'money.amount', code: 'settings_decimal_scale_exceeded' }] }))
    await screen.findByText(issueText('settings_decimal_scale_exceeded'))
    expect((screen.getByLabelText(FIELD_LABEL.amount) as HTMLInputElement).disabled).toBe(false)
  })
})

describe('ManualReceiptDraftForm — lifecycle', () => {
  it('unmounting while pending loses nothing: the remounted form is frozen on the same key', async () => {
    const { port, commands } = banksFor()
    const view = renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    view.rerender(<></>)
    view.rerender(
      <QueryClientProvider client={view.queryClient}>
        <ShellStateContext.Provider value={shellFor(COMPANY_A)}>
          <FinancePortProvider port={port}>
            <ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />
          </FinancePortProvider>
        </ShellStateContext.Provider>
      </QueryClientProvider>,
    )
    fireEvent.click(await screen.findByRole('button', { name: RESEND_SAME }))
    expect(commands).toHaveLength(2)
    expect(commands[1].key).toBe(commands[0].key)
  })
})

describe('ManualReceiptDraftForm — company context', () => {
  it('a late success from company A never shows in company B, and B sends nothing', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port, shell: shellFor(COMPANY_A, 1) })
    await fillValid()
    fireEvent.click(saveButton())

    await commitContext(shellFor(COMPANY_B, 2))
    await screen.findByLabelText(FIELD_LABEL.treasuryAccountId)
    await commands[0].resolve(succeeded(sourceDetail(COMPANY_A)))

    expect(screen.queryByText(DRAFT_SAVED)).toBeNull()
    expect(screen.queryByText(UNKNOWN_TITLE)).toBeNull()
    expect(saveButton().disabled).toBe(false)
    expect(commands).toHaveLength(1)
  })

  it('an unknown attempt stays with company A: B is not frozen, returning to A restores the same key', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} profile={EXAMPLE_PROFILE} />, { port, shell: shellFor(COMPANY_A, 1) })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(NO_REPLY)
    await screen.findByText(UNKNOWN_TITLE)

    await commitContext(shellFor(COMPANY_B, 2))
    await waitFor(() => expect(screen.queryByText(UNKNOWN_TITLE)).toBeNull())

    await commitContext(shellFor(COMPANY_A, 3))
    fireEvent.click(await screen.findByRole('button', { name: RESEND_SAME }))
    expect(commands[1].key).toBe(commands[0].key)
    expect(commands[1].scope).toEqual({ companyId: COMPANY_A })
  })
})
