/**
 * C-03 account status action — the selected company account's reasoned deactivate /
 * reactivate (Business §14.2 sub-item, PR #232 comment 5647422648).
 *
 * Component tests over the REAL shell + router + capability gate + boundary, with a
 * stubbed `fetch`. They measure what is SENT and what is SHOWN: the exact wire body,
 * the one key per attempt, the fresh read after a 204, and every path on which no
 * second PUT may go out. None of this is a server fact — the SQL/HTTP facts for
 * status, audit and idempotency live in Backend's own acceptance tests, and the
 * browser journey stays HAZIRLANDI / NOT_RUN until it is really run.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  A_ID,
  ACC_A,
  ALL_CARI_CAPABILITIES,
  CHAIN,
  PARTY,
  findStateBlock,
  installFetch,
  json,
  mutationView,
  noContent,
  problem,
  renderAt,
  type RecordedCall,
} from './commandPageHarness'
import { resetContractReadiness } from './clientContract'
import { CARI_CONTROL_CAPABILITY } from './accountStatusContract'
import { CARI_ACCOUNT_STATUS_TEXT as TEXT } from './cariCommandText'

const DETAIL_PATH = `/cari/${PARTY}`
const REASON_DEACTIVATE = 'Bu şirket hesabındaki çalışmaya ara verildi.'
const REASON_ACTIVATE = 'Çalışma yeniden başladı.'

/** Business's example fixture: one Party, account A roles 0 / active / v7. */
interface AccountFixture {
  version: string
  isActive: boolean
}

function accountView(account: AccountFixture) {
  return mutationView({
    companyAccounts: [{ id: ACC_A, companyId: A_ID, roles: 0, isActive: account.isActive, version: account.version }],
  })
}

const WITHOUT_STATUS = ALL_CARI_CAPABILITIES.filter((id) => id !== CARI_CONTROL_CAPABILITY.accountStatusUpdate)

function accountState(): string | null {
  return document.querySelector('[data-account-state]')?.getAttribute('data-account-state') ?? null
}

function actionButton(name: string): HTMLButtonElement {
  return screen.getByRole('button', { name }) as HTMLButtonElement
}

function dialog(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[role="dialog"][data-intent]')
}

async function openDialog(name: string): Promise<HTMLElement> {
  await userEvent.click(actionButton(name))
  return await waitFor(() => {
    const node = dialog()
    if (node === null) throw new Error('status dialog not open')
    return node
  }, CHAIN)
}

async function fillReason(panel: HTMLElement, reason: string) {
  const field = within(panel).getByLabelText(TEXT.reason)
  await userEvent.clear(field)
  await userEvent.type(field, reason)
}

async function waitForAccountState(expected: 'active' | 'inactive') {
  await waitFor(() => expect(accountState()).toBe(expected), CHAIN)
}

beforeEach(() => {
  resetContractReadiness()
  window.localStorage.clear()
  vi.stubGlobal('navigator', { onLine: true })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('offered only under the status CONTROL capability (never derived from the detail grant)', () => {
  it('without the control: the card renders, no action, no PUT', async () => {
    const fetches = installFetch({
      capabilities: WITHOUT_STATUS,
      detail: () => json(accountView({ version: '7', isActive: true })),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    expect(screen.queryByRole('button', { name: TEXT.action.deactivate })).toBeNull()
    expect(screen.queryByRole('button', { name: TEXT.action.activate })).toBeNull()
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })

  it('with the control: the action names the intent derived from the SHOWN account state', async () => {
    installFetch({ detail: () => json(accountView({ version: '7', isActive: true })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    expect(actionButton(TEXT.action.deactivate).disabled).toBe(false)
    expect(screen.queryByRole('button', { name: TEXT.action.activate })).toBeNull()
  })

  it('a passive account offers activation, not deactivation', async () => {
    installFetch({ detail: () => json(accountView({ version: '8', isActive: false })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('inactive')
    expect(actionButton(TEXT.action.activate).disabled).toBe(false)
  })
})

describe('the user chain: deactivate with reason → fresh read → reactivate with a separate reason', () => {
  it('sends exactly the contract body, then reads — and shows the READ, not the request', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    const fetches = installFetch({
      detail: () => json(accountView(account)),
      activeState: (call: RecordedCall) => {
        // The server applies the change; the client learns of it only through the GET.
        const body = call.body as { isActive: boolean }
        account.isActive = body.isActive
        account.version = String(Number(account.version) + 1)
        return noContent()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const readsBefore = fetches.detailReads().length

    const panel = await openDialog(TEXT.action.deactivate)
    expect(within(panel).getByRole('heading', { name: TEXT.title.deactivate })).toBeTruthy()
    // The target is named from the record, the company from the shell directory.
    expect(within(panel).getByText(/CAR-000001/)).toBeTruthy()
    expect(within(panel).getByText(/Göymen Demir Çelik/)).toBeTruthy()
    expect(within(panel).getByText(TEXT.reasonHint)).toBeTruthy()

    await fillReason(panel, `  ${REASON_DEACTIVATE}  `)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))

    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)
    const put = fetches.activeStateUpdates()[0]
    expect(put.url).toMatch(new RegExp(`/counterparties/${PARTY}/company-account/active-state$`))
    expect(put.companyId).toBe(A_ID)
    expect(put.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(put.headers['X-ERP-Counterparty-Contract']).toBe('optional-trade-roles-v1')
    // EXACTLY the three properties; the reason trimmed; the fence the ACCOUNT's version,
    // as the decimal string — never a Party version, never a number.
    expect(put.body).toEqual({ isActive: false, reason: REASON_DEACTIVATE, expectedVersion: '7' })
    expect(Object.keys(put.body as object).sort()).toEqual(['expectedVersion', 'isActive', 'reason'])

    // 204 → a fresh authorized GET, and the screen is that read's answer: v8, passive.
    await waitForAccountState('inactive')
    expect(fetches.detailReads().length).toBeGreaterThan(readsBefore)
    const saved = await findStateBlock('account-status-saved')
    expect(within(saved).getByText(TEXT.savedTitle.deactivate)).toBeTruthy()
    expect(within(saved).getByText(TEXT.savedDetail)).toBeTruthy()
    expect(within(saved).getByText(TEXT.savedListNote)).toBeTruthy()
    expect(dialog()).toBeNull()
    expect(screen.getByTestId('cari-account-status-announcement').textContent).toBe(
      TEXT.announceSaved.deactivate,
    )
    // Focus is back on the action, which now offers the OTHER intent.
    expect(document.activeElement).toBe(actionButton(TEXT.action.activate))

    // Reactivation: a separate reason, a NEW key, the fence moved to what the read said.
    const panel2 = await openDialog(TEXT.action.activate)
    await fillReason(panel2, REASON_ACTIVATE)
    await userEvent.click(within(panel2).getByRole('button', { name: TEXT.submit.activate }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    const put2 = fetches.activeStateUpdates()[1]
    expect(put2.body).toEqual({ isActive: true, reason: REASON_ACTIVATE, expectedVersion: '8' })
    expect(put2.idempotencyKey).not.toBe(put.idempotencyKey)
    await waitForAccountState('active')
    expect(within(await findStateBlock('account-status-saved')).getByText(TEXT.savedTitle.activate)).toBeTruthy()
    // Two logical changes, two PUTs, nothing else written.
    expect(fetches.activeStateUpdates()).toHaveLength(2)
    expect(fetches.creates()).toHaveLength(0)
    expect(fetches.links()).toHaveLength(0)
    expect(fetches.contactUpdates()).toHaveLength(0)
  })

  it('carries an Int64 fence at full range verbatim — no Number, no rounding', async () => {
    const MAX = '9223372036854775807'
    const fetches = installFetch({ detail: () => json(accountView({ version: MAX, isActive: true })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)
    expect((fetches.activeStateUpdates()[0].body as { expectedVersion: unknown }).expectedVersion).toBe(MAX)
  })

  it('a replayed 204 is reported as the earlier attempt’s result, not a second change', async () => {
    const account: AccountFixture = { version: '8', isActive: false }
    installFetch({
      detail: () => json(accountView(account)),
      activeState: () => noContent({ 'Idempotency-Replayed': 'true' }),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('inactive')
    const panel = await openDialog(TEXT.action.activate)
    await fillReason(panel, REASON_ACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.activate }))
    const saved = await findStateBlock('account-status-saved')
    expect(within(saved).getByText(TEXT.savedReplayed)).toBeTruthy()
    // The read said nothing moved, so the screen says nothing moved.
    expect(accountState()).toBe('inactive')
  })
})

describe('reason — refused on the client before any key exists', () => {
  it('empty and whitespace-only: no PUT, no key, the summary takes focus; fixed → sent', async () => {
    const fetches = installFetch({ detail: () => json(accountView({ version: '7', isActive: true })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, '   ')
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    const summary = await within(panel).findByText(TEXT.errorSummary)
    expect(document.activeElement).toBe(summary)
    expect(within(panel).getByText(TEXT.reasonRequired)).toBeTruthy()
    expect(within(panel).getByLabelText(TEXT.reason).getAttribute('aria-invalid')).toBe('true')
    expect(fetches.activeStateUpdates()).toHaveLength(0)

    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)
  })

  it('1001 characters after trim is refused with the length shown', async () => {
    const fetches = installFetch({ detail: () => json(accountView({ version: '7', isActive: true })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    const field = within(panel).getByLabelText(TEXT.reason) as HTMLTextAreaElement
    // Typed by value, not keystroke by keystroke — 1001 key events are not the point.
    await userEvent.click(field)
    await userEvent.paste('x'.repeat(1001))
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    expect(await within(panel).findByText(TEXT.reasonTooLong(1001))).toBeTruthy()
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })
})

describe('closing the window', () => {
  it('with an unsent reason asks first; keeping stays, discarding closes and returns focus', async () => {
    const fetches = installFetch({ detail: () => json(accountView({ version: '7', isActive: true })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, 'yarım kalan gerekçe')
    await userEvent.keyboard('{Escape}')
    const confirm = within(panel).getByRole('alertdialog')
    expect(within(confirm).getByText(TEXT.closeConfirmTitle)).toBeTruthy()
    // The safe answer holds the focus.
    expect(document.activeElement).toBe(within(confirm).getByRole('button', { name: TEXT.closeConfirmKeep }))
    await userEvent.click(within(confirm).getByRole('button', { name: TEXT.closeConfirmKeep }))
    expect(within(panel).queryByRole('alertdialog')).toBeNull()
    expect((within(panel).getByLabelText(TEXT.reason) as HTMLTextAreaElement).value).toBe('yarım kalan gerekçe')

    await userEvent.keyboard('{Escape}')
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.closeConfirmDiscard }))
    await waitFor(() => expect(dialog()).toBeNull())
    expect(document.activeElement).toBe(actionButton(TEXT.action.deactivate))
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })

  it('with no reason typed closes at once', async () => {
    installFetch({ detail: () => json(accountView({ version: '7', isActive: true })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.cancel }))
    await waitFor(() => expect(dialog()).toBeNull())
  })
})

describe('unknown outcome — same key, explicit retry after a fresh read, never a new key', () => {
  it('a 500 freezes the attempt; the retry re-reads first, then resends the SAME key and body', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    let statusAnswers = 0
    const fetches = installFetch({
      detail: () => json(accountView(account)),
      activeState: () => {
        statusAnswers += 1
        if (statusAnswers === 1) return problem(500, 'internal_error')
        account.isActive = false
        account.version = '8'
        return noContent()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))

    const unknown = await waitFor(() => {
      const node = document.querySelector<HTMLElement>('[data-attempt="unknown"]')
      if (node === null) throw new Error('unknown notice not shown')
      return node
    }, CHAIN)
    const first = fetches.activeStateUpdates()[0]
    // Frozen: the form cannot start a second attempt — the field and the submit are
    // closed, the reason is kept exactly as sent.
    expect((within(panel).getByRole('button', { name: TEXT.submit.deactivate }) as HTMLButtonElement).disabled).toBe(
      true,
    )
    expect((within(panel).getByLabelText(TEXT.reason).closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
    expect((within(panel).getByLabelText(TEXT.reason) as HTMLTextAreaElement).value).toBe(REASON_DEACTIVATE)
    const readsBefore = fetches.detailReads().length

    await userEvent.click(within(unknown).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    // A fresh read went out BEFORE the resend, and the resend is byte-for-byte the same attempt.
    expect(fetches.detailReads().length).toBeGreaterThan(readsBefore)
    const second = fetches.activeStateUpdates()[1]
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)

    await waitForAccountState('inactive')
    expect(await findStateBlock('account-status-saved')).toBeTruthy()
  })

  it('closing the window while unknown is neither a cancel nor a rollback: the recovery stays in the section', async () => {
    const fetches = installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => problem(503, 'service_unavailable'),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    await waitFor(() => expect(document.querySelector('[data-attempt="unknown"]')).not.toBeNull(), CHAIN)

    await userEvent.keyboard('{Escape}')
    const confirm = within(panel).getByRole('alertdialog')
    expect(within(confirm).getByText(TEXT.closeUnresolvedTitle)).toBeTruthy()
    await userEvent.click(within(confirm).getByRole('button', { name: TEXT.closeUnresolvedClose }))
    await waitFor(() => expect(dialog()).toBeNull())

    // Still unresolved, still reachable, still the same single PUT — and no new window.
    const section = document.querySelector<HTMLElement>('[data-control="account-status"]')
    expect(section).not.toBeNull()
    expect(within(section as HTMLElement).getByText('İşlem sonucu belirsiz')).toBeTruthy()
    expect(actionButton(TEXT.action.deactivate).disabled).toBe(true)
    expect(screen.getByText(TEXT.actionBlockedUnresolved)).toBeTruthy()
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })

  it('after an abandon, the unchanged payload is NOT re-sent under a new key', async () => {
    const fetches = installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => problem(503, 'service_unavailable'),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    const unknown = await waitFor(() => {
      const node = document.querySelector<HTMLElement>('[data-attempt="unknown"]')
      if (node === null) throw new Error('unknown notice not shown')
      return node
    }, CHAIN)
    await userEvent.click(within(unknown).getByRole('button', { name: 'İşlemi bırak' }))
    // Same intent, same fence, same reason → the same unresolved attempt; refused, explained.
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    const abandoned = await within(panel).findByText('Bırakılan işlemin sonucu belirsiz')
    expect(abandoned).toBeTruthy()
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })
})

describe('definite refusals — closed attempts, nothing resent on its own', () => {
  it('version_conflict: no overwrite, no new key; "Kaydı yenile" re-reads and closes the window', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    const fetches = installFetch({
      detail: () => json(accountView(account)),
      activeState: () => {
        // Someone else moved the record (a contact update advances the same account version).
        account.version = '8'
        return problem(409, 'version_conflict')
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    const failed = await waitFor(() => {
      const node = document.querySelector<HTMLElement>('[data-attempt="failed"][data-failure="version-conflict"]')
      if (node === null) throw new Error('conflict notice not shown')
      return node
    }, CHAIN)
    expect(within(failed).getByText('Kayıt siz düzenlerken değişti')).toBeTruthy()
    expect(within(failed).queryByText(TEXT.replayedConflict)).toBeNull()
    const readsBefore = fetches.detailReads().length
    await userEvent.click(within(failed).getByRole('button', { name: TEXT.refreshRecord }))
    await waitFor(() => expect(fetches.detailReads().length).toBeGreaterThan(readsBefore), CHAIN)
    await waitFor(() => expect(dialog()).toBeNull())
    expect(fetches.activeStateUpdates()).toHaveLength(1)
    // The account is still active on screen — the read said so — and a new attempt is a
    // new window on the fresh version.
    expect(accountState()).toBe('active')
    expect(actionButton(TEXT.action.deactivate).disabled).toBe(false)
  })

  it('a REPLAYED version_conflict is marked as not current', async () => {
    installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () =>
        new Response(JSON.stringify({ code: 'version_conflict', title: 'Çakışma' }), {
          status: 409,
          headers: {
            'Content-Type': 'application/json',
            'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1',
            'Idempotency-Replayed': 'true',
          },
        }),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    expect(await screen.findByText(TEXT.replayedConflict, {}, CHAIN)).toBeTruthy()
  })

  it('idempotency_conflict: the key is not reused for another intent, nothing resent', async () => {
    const fetches = installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => problem(409, 'idempotency_conflict'),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    await waitFor(
      () => expect(document.querySelector('[data-attempt="failed"][data-failure="idempotency-conflict"]')).not.toBeNull(),
      CHAIN,
    )
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })

  it('404: the account is not there — no create, no link, the status text is this command’s', async () => {
    const fetches = installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => problem(404, 'not_found'),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    expect(await screen.findByText(TEXT.failure['not-found'].title, {}, CHAIN)).toBeTruthy()
    expect(screen.queryByText(/aday listesi/)).toBeNull()
    expect(fetches.creates()).toHaveLength(0)
    expect(fetches.links()).toHaveLength(0)
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })

  it('400 validation_failed keeps the reason for correction', async () => {
    installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => problem(400, 'validation_failed'),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    expect(await within(panel).findByText(TEXT.failure.validation.detail, {}, CHAIN)).toBeTruthy()
    expect((within(panel).getByLabelText(TEXT.reason) as HTMLTextAreaElement).value).toBe(REASON_DEACTIVATE)
  })

  it('403 on the PUT reaches the central boundary: the feature closes on the rejected authority, no second PUT', async () => {
    const fetches = installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => problem(403, 'forbidden'),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
    // The same effect a read's 403 has (SAB-004/005): the capability is re-fenced, the
    // gated route closes onto a neutral state block, the record leaves the DOM — the
    // window with it — and no local "forbidden" notice sits over a capability that is
    // no longer believed to be held.
    await waitFor(() => expect(accountState()).toBeNull(), CHAIN)
    expect(dialog()).toBeNull()
    expect(document.querySelector('[data-state]')).not.toBeNull()
    expect(document.querySelector('[data-attempt]')).toBeNull()
    expect(screen.queryByText(/CAR-000001/)).toBeNull()
    // A 403 is not a session end.
    expect(screen.queryByText(/Oturum sonlandı/i)).toBeNull()
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })
})

describe('after a 204, the read is the truth', () => {
  it('a failed post-204 read: "saved, view not refreshed", a READ retry, never a second PUT', async () => {
    let applied = false
    let detailAfterApply = 0
    const fetches = installFetch({
      detail: () => {
        if (!applied) return json(accountView({ version: '7', isActive: true }))
        detailAfterApply += 1
        return detailAfterApply === 1
          ? problem(503, 'service_unavailable')
          : json(accountView({ version: '8', isActive: false }))
      },
      activeState: () => {
        applied = true
        return noContent()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))

    const recovery = await findStateBlock('read-recovery')
    expect(recovery.textContent).toContain(TEXT.readRecovery)
    // The retry offered is the read's own.
    await userEvent.click(screen.getByRole('button', { name: /Yeniden dene|Tekrar dene/ }))
    await waitForAccountState('inactive')
    expect(await findStateBlock('account-status-saved')).toBeTruthy()
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })

  it('a read landing under the open window with a NEW version blocks the stale fence', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    const fetches = installFetch({ detail: () => json(accountView(account)) })
    const { queryClient } = renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON_DEACTIVATE)
    // Another tab's contact update advanced the account; this screen re-reads.
    account.version = '8'
    await queryClient.invalidateQueries()
    expect(await within(panel).findByText(TEXT.changedSinceOpen, {}, CHAIN)).toBeTruthy()
    expect((within(panel).getByRole('button', { name: TEXT.submit.deactivate }) as HTMLButtonElement).disabled).toBe(
      true,
    )
    await userEvent.keyboard('{Enter}')
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })
})

describe('company / session boundary', () => {
  it('a typed reason registers a dirty scope: the company switch asks first, and nothing is sent', async () => {
    const fetches = installFetch({ detail: () => json(accountView({ version: '7', isActive: true })) })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, 'yarım gerekçe')
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), 'bbbbbbbb-0000-0000-0000-000000000002')
    const ask = await screen.findByRole('button', { name: 'Kal' }, CHAIN)
    expect(ask).toBeTruthy()
    await userEvent.click(ask)
    // Stayed: the window and the reason are exactly as they were.
    expect((within(panel).getByLabelText(TEXT.reason) as HTMLTextAreaElement).value).toBe('yarım gerekçe')
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })
})
