/**
 * FS0/FS1 draft form — DOM tests with REAL pending promises (the recording port resolves
 * only when the test says so). Component evidence; SENTETIK/NOT_ENV; no API/SQL/browser.
 */

import { describe, expect, it, vi } from 'vitest'
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { FINANCE_DRAFT_DIRTY_SCOPE, ManualReceiptDraftForm } from './ManualReceiptDraftForm'
import {
  ABANDON,
  CONFLICT_TITLE,
  DRAFT_CONTEXT_RETRY,
  DRAFT_CONTEXT_TEXT,
  DRAFT_SAVED,
  FIELD_ERROR,
  FIELD_LABEL,
  NO_USABLE_BANK_ACCOUNT,
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
  draftContextBody,
  commitContext,
  contextOk,
  contextStatus,
  currentShell,
  failedOutcome,
  found,
  recordingPort,
  renderFinance,
  shellEvent,
  shellFor,
  sourceDetail,
  succeeded,
} from './testHarness'
import type { DraftSaveRequest } from './contract'
import type { DraftContextRequest } from './draftContext'
import type { HttpReadReply } from './http'

const PARTY = { partyId: EXAMPLE_PARTY, companyAccountId: EXAMPLE_ACCOUNT }

/** Synthetic scale for tests that are not about scale (the #261 example itself carries 0). */
const TEST_SCALE = 4

function banksFor(items?: (company: string) => Record<string, unknown>[]) {
  return recordingPort((method, scope, request) => {
    if (method === 'listBankAccounts') return found(bankPage(scope.companyId, items?.(scope.companyId)))
    if (method === 'draftContext') return contextOk(contextWithScale(scope.companyId, request as DraftContextRequest, TEST_SCALE))
    return NO_REPLY
  })
}

function contextWithScale(companyId: string, request: DraftContextRequest, scale: number): Record<string, unknown> {
  const body = draftContextBody(companyId, request)
  const currency = body.currency as { profile: Record<string, unknown> }
  return { ...body, currency: { ...currency, profile: { ...currency.profile, scale } } }
}

const PARTY_TEXT = 'SENTETIK-code — SENTETIK legal name'

/** Per-company Cari legal names, so a stale label cannot be mistaken for a fresh one. */
const LEGAL_NAME: Readonly<Record<string, string>> = {
  [COMPANY_A]: 'SENTETIK A ünvanı',
  [COMPANY_B]: 'SENTETIK B ünvanı',
}

function labelFor(companyId: string): string {
  return `SENTETIK-code — ${LEGAL_NAME[companyId]}`
}

function contextWithLegalName(companyId: string, request: DraftContextRequest): Record<string, unknown> {
  const body = contextWithScale(companyId, request, TEST_SCALE)
  return { ...body, party: { ...(body.party as Record<string, unknown>), legalName: LEGAL_NAME[companyId] } }
}


async function fillValid(amount = '1000') {
  const select = await screen.findByLabelText(FIELD_LABEL.treasuryAccountId)
  fireEvent.change(select, { target: { value: EXAMPLE_BANK } })
  fireEvent.change(amountInput(), { target: { value: amount } })
  fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
  fireEvent.change(screen.getByLabelText(FIELD_LABEL.description), { target: { value: 'SENTETIK/NOT_ENV' } })
  // Saving is closed until the draft read context for THIS selection has arrived.
  await screen.findByText(PARTY_TEXT)
}

function amountInput(): HTMLInputElement {
  return screen.getByRole('textbox', { name: /^Tutar/ }) as HTMLInputElement
}

function saveButton() {
  return screen.getByRole('button', { name: SAVE_DRAFT }) as HTMLButtonElement
}

describe('ManualReceiptDraftForm — normal', () => {
  it('sends exactly the example create request once, freezes while pending, and says "no money effect"', async () => {
    const { port, commands } = banksFor()
    const onSaved = vi.fn()
    renderFinance(<ManualReceiptDraftForm party={PARTY} onSaved={onSaved} />, { port })
    await fillValid()

    fireEvent.click(saveButton())
    expect(commands).toHaveLength(1)
    expect(commands[0].method).toBe('draftSave')
    expect(commands[0].scope).toEqual({ companyId: COMPANY_A })
    expect(commands[0].request).toEqual(NORMAL_EXAMPLE.draftSave)

    // Genuinely in flight: nothing resolved yet.
    expect(saveButton().disabled).toBe(true)
    expect((amountInput() as HTMLInputElement).disabled).toBe(true)
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
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    const form = saveButton().closest('form') as HTMLFormElement
    fireEvent.submit(form)
    fireEvent.submit(form)
    expect(commands).toHaveLength(1)
  })

  it('an update sends draft id + version and the fresh context profile', async () => {
    const { port, commands } = banksFor()
    const decoded = (await import('./contract')).decodeSourceDetail(sourceDetail(COMPANY_A), COMPANY_A)
    if (!decoded.ok) throw new Error('fixture')
    renderFinance(<ManualReceiptDraftForm party={PARTY} draft={decoded.value} />, { port })
    await screen.findByText(PARTY_TEXT)
    fireEvent.change(amountInput(), { target: { value: '1000,5' } })
    fireEvent.click(saveButton())
    const request = commands[0].request as DraftSaveRequest
    expect([request.draftId, request.expectedVersion]).toEqual([NORMAL_EXAMPLE.record.draftId, '9007199254740993'])
    expect(request.preparation.profile).toEqual(EXAMPLE_PROFILE)
    expect(request.preparation.money.amount).toBe('1000.5')
  })
})

describe('ManualReceiptDraftForm — draft read context (#261)', () => {
  it('asks for the context only once bank and date are chosen, with exactly those values and the shell company', async () => {
    const { port, reads } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    const select = await screen.findByLabelText(FIELD_LABEL.treasuryAccountId)
    fireEvent.change(select, { target: { value: EXAMPLE_BANK } })
    screen.getByText(DRAFT_CONTEXT_TEXT['needs-selection'])
    expect(reads.filter((read) => read.method === 'draftContext')).toHaveLength(0)

    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
    await screen.findByText(PARTY_TEXT)
    const contextReads = reads.filter((read) => read.method === 'draftContext')
    expect(contextReads).toHaveLength(1)
    expect(contextReads[0].scope).toEqual({ companyId: COMPANY_A })
    expect(contextReads[0].request).toEqual({ treasuryAccountId: EXAMPLE_BANK, partyId: EXAMPLE_PARTY, companyAccountId: EXAMPLE_ACCOUNT, occurredOn: '2026-09-13' })
    screen.getByRole('textbox', { name: 'Tutar (SENTETIK)' })
  })

  it('saves with the context profile and currency, not the bank list or a prop', async () => {
    const otherCurrency = '99999999-0000-4000-8000-000000000009'
    const otherProfile = { profileId: '88888888-0000-4000-8000-000000000008', version: '42' }
    const { port, commands } = recordingPort((method, scope, request) => {
      if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
      if (method !== 'draftContext') return NO_REPLY
      const body = contextWithScale(scope.companyId, request as DraftContextRequest, TEST_SCALE)
      return contextOk({
        ...body,
        bankAccount: { ...(body.bankAccount as object), currencyId: otherCurrency },
        currency: { ...(body.currency as object), currencyId: otherCurrency, profile: { ...((body.currency as { profile: object }).profile), reference: otherProfile } },
      })
    })
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    const request = commands[0].request as DraftSaveRequest
    expect(request.preparation.money.currencyId).toBe(otherCurrency)
    expect(request.preparation.profile).toEqual(otherProfile)
  })

  it('a changed date closes saving until its own context arrives; the older profile is never sent', async () => {
    let releaseSecond: (reply: HttpReadReply) => void = () => {}
    const second = new Promise<HttpReadReply>((resolve) => {
      releaseSecond = resolve
    })
    const { port, commands } = recordingPort((method, scope, request) => {
      if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
      if (method !== 'draftContext') return NO_REPLY
      const typed = request as DraftContextRequest
      if (typed.occurredOn === '2026-09-14') return second
      return contextOk(contextWithScale(scope.companyId, typed, TEST_SCALE))
    })
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    expect(saveButton().disabled).toBe(false)

    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-14' } })
    await screen.findByText(DRAFT_CONTEXT_TEXT.loading)
    expect(saveButton().disabled).toBe(true)
    fireEvent.submit(saveButton().closest('form') as HTMLFormElement)
    expect(commands).toHaveLength(0)

    const newer = draftContextBody(COMPANY_A, { treasuryAccountId: EXAMPLE_BANK, partyId: EXAMPLE_PARTY, companyAccountId: EXAMPLE_ACCOUNT, occurredOn: '2026-09-14' })
    const newProfile = { profileId: '88888888-0000-4000-8000-000000000008', version: '43' }
    await act(async () => {
      releaseSecond(contextOk({ ...newer, currency: { ...(newer.currency as object), profile: { ...((newer.currency as { profile: object }).profile), scale: TEST_SCALE, reference: newProfile } } }))
    })
    await waitFor(() => expect(saveButton().disabled).toBe(false))
    fireEvent.click(saveButton())
    const request = commands[0].request as DraftSaveRequest
    expect(request.preparation.occurredOn).toBe('2026-09-14')
    expect(request.preparation.profile).toEqual(newProfile)
  })

  it.each([
    [404, 'not-found'],
    [403, 'forbidden'],
    [400, 'invalid-request'],
  ] as const)('HTTP %s is its own closed state', async (status, kind) => {
    const { port, commands } = recordingPort((method, scope) =>
      method === 'listBankAccounts' ? found(bankPage(scope.companyId)) : method === 'draftContext' ? contextStatus(status) : NO_REPLY,
    )
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    fireEvent.change(await screen.findByLabelText(FIELD_LABEL.treasuryAccountId), { target: { value: EXAMPLE_BANK } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
    await screen.findByText(DRAFT_CONTEXT_TEXT[kind])
    expect(saveButton().disabled).toBe(true)
    expect(commands).toHaveLength(0)
  })

  it('a context body for another date is malformed, closed, and retryable', async () => {
    const { port, reads } = recordingPort((method, scope, request) => {
      if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
      if (method !== 'draftContext') return NO_REPLY
      return contextOk(draftContextBody(scope.companyId, { ...(request as DraftContextRequest), occurredOn: '2026-01-01' }))
    })
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    fireEvent.change(await screen.findByLabelText(FIELD_LABEL.treasuryAccountId), { target: { value: EXAMPLE_BANK } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
    await screen.findByText(DRAFT_CONTEXT_TEXT.malformed)
    expect(saveButton().disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: DRAFT_CONTEXT_RETRY }))
    await waitFor(() => expect(reads.filter((read) => read.method === 'draftContext')).toHaveLength(2))
  })

  it('pre-checks the approved scale without rounding and says the limit', async () => {
    const { port, commands } = recordingPort((method, scope, request) => {
      if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
      if (method !== 'draftContext') return NO_REPLY
      return contextOk(contextWithScale(scope.companyId, request as DraftContextRequest, 2))
    })
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid('1000,125')
    screen.getByText(/En fazla 2 ondalık basamak\./)
    fireEvent.click(saveButton())
    screen.getByText(FIELD_ERROR['scale-exceeded'])
    expect(amountInput().value).toBe('1000,125')
    expect(commands).toHaveLength(0)
  })
})

describe('ManualReceiptDraftForm — closed and client-invalid paths send nothing', () => {
  it('without a ready draft context a create is closed and explains why', async () => {
    const { port, commands } = recordingPort((method, scope) =>
      method === 'listBankAccounts' ? found(bankPage(scope.companyId)) : method === 'draftContext' ? contextStatus(503) : NO_REPLY,
    )
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    const select = await screen.findByLabelText(FIELD_LABEL.treasuryAccountId)
    fireEvent.change(select, { target: { value: EXAMPLE_BANK } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
    fireEvent.change(amountInput(), { target: { value: '1000' } })
    await screen.findByText(DRAFT_CONTEXT_TEXT.unavailable)
    expect(saveButton().disabled).toBe(true)
    fireEvent.submit(saveButton().closest('form') as HTMLFormElement)
    expect(commands).toHaveLength(0)
  })

  it('a dotted amount is refused on the field, focused, and nothing is sent', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid('1.000')
    fireEvent.click(saveButton())
    const amount = amountInput()
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
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await screen.findByText(NO_USABLE_BANK_ACCOUNT)
    expect(within(screen.getByLabelText(FIELD_LABEL.treasuryAccountId)).getAllByRole('option')).toHaveLength(1) // the placeholder only
  })
})

describe('ManualReceiptDraftForm — unknown outcome keeps key, company, actor and payload', () => {
  it('no reply → frozen; resend uses the SAME key and body; abandon + identical submit reuses it; an edit mints a new one', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(NO_REPLY)

    await screen.findByText(UNKNOWN_TITLE)
    expect((amountInput() as HTMLInputElement).disabled).toBe(true)
    expect(saveButton().disabled).toBe(true)
    expect(commands).toHaveLength(1) // nothing resent on its own

    fireEvent.click(screen.getByRole('button', { name: RESEND_SAME }))
    expect(commands).toHaveLength(2)
    expect(commands[1].key).toBe(commands[0].key)
    expect(commands[1].request).toEqual(commands[0].request)
    expect(commands[1].scope).toEqual(commands[0].scope)

    await commands[1].reject() // transport failure on the resend: still unknown
    fireEvent.click(await screen.findByRole('button', { name: ABANDON }))
    expect((amountInput() as HTMLInputElement).disabled).toBe(false)

    fireEvent.click(saveButton())
    expect(commands[2].key).toBe(commands[0].key)
    await commands[2].resolve(NO_REPLY)
    fireEvent.click(await screen.findByRole('button', { name: ABANDON }))

    fireEvent.change(amountInput(), { target: { value: '1001' } })
    fireEvent.click(saveButton())
    expect(commands[3].key).not.toBe(commands[0].key)
  })

  it('an undecodable success body is not a success', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(body({ ...NORMAL_EXAMPLE.draftSaveResult, balance: '1000' }))
    await screen.findByText(UNKNOWN_TITLE)
    expect(screen.queryByText(DRAFT_SAVED)).toBeNull()
  })

  it('a non-replayed refusal of the resend does not settle the first send', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
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
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
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
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid('1,1234')
    fireEvent.click(saveButton())
    await commands[0].resolve(failedOutcome('ValidationFailed', { issues: [{ field: 'money.amount', code: 'settings_decimal_scale_exceeded' }] }))
    await screen.findByText(issueText('settings_decimal_scale_exceeded'))
    expect((amountInput() as HTMLInputElement).disabled).toBe(false)
  })
})

describe('ManualReceiptDraftForm — abandoned attempts keep their history', () => {
  it('re-submitting an abandoned payload inherits its uncertainty: a non-replayed refusal does not close it', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(NO_REPLY)
    fireEvent.click(await screen.findByRole('button', { name: ABANDON }))

    fireEvent.click(saveButton())
    expect(commands[1].key).toBe(commands[0].key)
    await commands[1].resolve(failedOutcome('Denied'))

    await screen.findByText(REFUSAL_AFTER_UNKNOWN)
    screen.getByText(UNKNOWN_TITLE)
    expect(saveButton().disabled).toBe(true)
  })

  it('a different payload after abandoning does not erase the abandoned key', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(NO_REPLY)
    fireEvent.click(await screen.findByRole('button', { name: ABANDON }))

    fireEvent.change(amountInput(), { target: { value: '2000' } })
    fireEvent.click(saveButton())
    expect(commands[1].key).not.toBe(commands[0].key)
    await commands[1].resolve(failedOutcome('ValidationFailed', { issues: [{ field: 'money.amount', code: 'settings_decimal_invalid' }] }))
    await screen.findByText(issueText('settings_decimal_invalid'))

    fireEvent.change(amountInput(), { target: { value: '1000' } })
    fireEvent.click(saveButton())
    expect(commands[2].key).toBe(commands[0].key)
  })
})

describe('ManualReceiptDraftForm — dirty work reaches the shell gate', () => {
  it('an edited form makes a company switch ask; a clean form does not register', async () => {
    const { port } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await screen.findByLabelText(FIELD_LABEL.amount)
    expect(currentShell().dirtyScopes).not.toContain(FINANCE_DRAFT_DIRTY_SCOPE)

    fireEvent.change(amountInput(), { target: { value: '1000' } })
    await waitFor(() => expect(currentShell().dirtyScopes).toContain(FINANCE_DRAFT_DIRTY_SCOPE))
    await shellEvent({ type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: COMPANY_B } })
    expect(currentShell().phase).toBe('confirming-switch')
    expect((amountInput() as HTMLInputElement).value).toBe('1000')
  })

  it('an unresolved key is dirty even when the fields match what was sent', async () => {
    const { port, commands } = banksFor()
    const view = renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    await commands[0].resolve(NO_REPLY)
    await screen.findByText(UNKNOWN_TITLE)
    expect(currentShell().dirtyScopes).toContain(FINANCE_DRAFT_DIRTY_SCOPE)
    view.remount(<></>)
    await waitFor(() => expect(currentShell().dirtyScopes).not.toContain(FINANCE_DRAFT_DIRTY_SCOPE))
  })
})

describe('ManualReceiptDraftForm — lifecycle', () => {
  it('unmounting while pending loses nothing: the remounted form is frozen on the same key', async () => {
    const { port, commands } = banksFor()
    const view = renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    fireEvent.click(saveButton())
    view.remount(<ManualReceiptDraftForm party={PARTY} />)
    fireEvent.click(await screen.findByRole('button', { name: RESEND_SAME }))
    expect(commands).toHaveLength(2)
    expect(commands[1].key).toBe(commands[0].key)
  })
})

describe('ManualReceiptDraftForm — company context', () => {
  it('a late success from company A never shows in company B, and B sends nothing', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port, shell: shellFor(COMPANY_A, 1) })
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
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port, shell: shellFor(COMPANY_A, 1) })
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

  it('the context is fenced by the shell namespace: B waits for its own answer instead of reading A’s', async () => {
    /**
     * The selection (bank, party, account, date) is identical in both companies, so the
     * shell namespace at the head of the query key is the ONLY thing separating the two
     * reads. Take it out and A's currency, profile and Cari label are what company B
     * renders — from cache, without a request anyone could refuse.
     *
     * B's answer is held on purpose: "B shows nothing yet" is the state that tells the
     * two apart, because once both answers have arrived the screen looks the same either
     * way.
     */
    const heldB: { release: (() => void) | null } = { release: null }
    const { port, reads } = recordingPort((method, scope, request) => {
      if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
      if (method !== 'draftContext') return NO_REPLY
      const answer = contextOk(contextWithLegalName(scope.companyId, request as DraftContextRequest))
      if (scope.companyId === COMPANY_A) return answer
      return new Promise<HttpReadReply>((resolve) => {
        heldB.release = () => resolve(answer)
      })
    })
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port, shell: shellFor(COMPANY_A, 1) })
    fireEvent.change(await screen.findByLabelText(FIELD_LABEL.treasuryAccountId), { target: { value: EXAMPLE_BANK } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })
    await screen.findByText(labelFor(COMPANY_A))

    await commitContext(shellFor(COMPANY_B, 2))
    await screen.findByLabelText(FIELD_LABEL.treasuryAccountId)

    expect(screen.queryByText(labelFor(COMPANY_A))).toBeNull()
    expect(screen.getByText(DRAFT_CONTEXT_TEXT.loading)).toBeTruthy()
    expect(saveButton().disabled).toBe(true)
    const contextReads = reads.filter((read) => read.method === 'draftContext')
    expect(contextReads).toHaveLength(2)
    expect(contextReads[1].scope).toEqual({ companyId: COMPANY_B })
    expect(contextReads[1].request).toEqual(contextReads[0].request)

    await act(async () => heldB.release?.())
    await screen.findByText(labelFor(COMPANY_B))
    expect(screen.queryByText(labelFor(COMPANY_A))).toBeNull()
  })
})

describe('ManualReceiptDraftForm — auth boundary', () => {
  /**
   * A refused context read is not this screen's private business: 401 ends the session
   * for the whole app and 403 means the capability this read ran under is no longer
   * trustworthy. The screen's own closed text proves neither, so these cases assert the
   * SHELL state the boundary produces — the effect a person actually experiences.
   */
  it.each([
    [401, 'session-reset'],
    [403, 'capability-refence'],
  ] as const)('HTTP %s from the context read reaches the shell auth boundary', async (status, effect) => {
    const { port } = recordingPort((method, scope) =>
      method === 'listBankAccounts'
        ? found(bankPage(scope.companyId))
        : method === 'draftContext'
          ? contextStatus(status)
          : NO_REPLY,
    )
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port, shell: shellFor(COMPANY_A, 1) })
    fireEvent.change(await screen.findByLabelText(FIELD_LABEL.treasuryAccountId), { target: { value: EXAMPLE_BANK } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.occurredOn), { target: { value: '2026-09-13' } })

    await waitFor(() => expect(currentShell().lastSecurityEffect?.kind).toBe(effect))
    if (status === 401) {
      expect(currentShell().sessionInvalid).toBe(true)
      expect(currentShell().activeContext).toBeNull()
      expect(currentShell().sessionEpoch).toBe(2)
    } else {
      expect(currentShell().activeContext?.capabilityGeneration).toBeNull()
      expect(currentShell().activeContext?.capabilities).toEqual([])
      expect(currentShell().requestFenceGeneration).toBe(1)
    }
  })
})

describe('ManualReceiptDraftForm — DEC-FIN-014 customer POS collection', () => {
  function grossInput(): HTMLInputElement {
    return screen.getByRole('textbox', { name: /^Brüt POS tutarı/ }) as HTMLInputElement
  }

  async function choosePos(commission: string, bearer: '' | 'Company' | 'Customer') {
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.collectionChannel), { target: { value: 'pos' } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.commissionAmount), { target: { value: commission } })
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.commissionBearer), { target: { value: bearer } })
  }

  it('sends the gross, the typed commission and the bearer, previews the exact net and never sends a net', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid('1000,00')
    await choosePos('25,50', 'Company')

    expect(grossInput().value).toBe('1000,00')
    const preview = screen.getByText(/974,50/)
    expect(preview.getAttribute('data-bank-net-preview')).toBe('ready')

    fireEvent.click(saveButton())
    expect(commands).toHaveLength(1)
    const request = commands[0].request as DraftSaveRequest
    expect(request.preparation.money.amount).toBe('1000.00')
    expect(request.preparation.pos).toEqual({ commissionAmount: '25.50', commissionBearer: 'Company' })
    expect(JSON.stringify(request)).not.toMatch(/bankNet|974/)
  })

  it('a zero commission is an answer: the net is the gross', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid('500')
    await choosePos('0', 'Company')
    expect(screen.getByText(/^500/).getAttribute('data-bank-net-preview')).toBe('ready')
    fireEvent.click(saveButton())
    expect((commands[0].request as DraftSaveRequest).preparation.pos).toEqual({ commissionAmount: '0', commissionBearer: 'Company' })
  })

  it('a commission not below the gross and a missing bearer are refused on the screen and nothing is sent', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid('1000')
    await choosePos('1000', '')
    expect(screen.getByText(/Brüt tutar ve komisyon girilince/).getAttribute('data-bank-net-preview')).toBe('pending')

    fireEvent.click(saveButton())
    screen.getByText(FIELD_ERROR['commission-not-below-gross'])
    screen.getByText(FIELD_ERROR['bearer-required'])
    expect(screen.getByLabelText(FIELD_LABEL.commissionAmount).getAttribute('aria-invalid')).toBe('true')
    expect(commands).toHaveLength(0)
  })

  it('a customer bearer says the charge is not applied here', async () => {
    const { port } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid('1000')
    await choosePos('10', 'Customer')
    screen.getByText(/bu kayıt müşterinin borcunu artırmaz/)
  })

  it('switching back to a bank transfer sends the exact #258 request with no pos member', async () => {
    const { port, commands } = banksFor()
    renderFinance(<ManualReceiptDraftForm party={PARTY} />, { port })
    await fillValid()
    await choosePos('10', 'Company')
    fireEvent.change(screen.getByLabelText(FIELD_LABEL.collectionChannel), { target: { value: 'bank-transfer' } })
    fireEvent.click(saveButton())
    expect(commands[0].request).toEqual(NORMAL_EXAMPLE.draftSave)
    expect(JSON.stringify(commands[0].request)).not.toContain('"pos"')
  })
})
