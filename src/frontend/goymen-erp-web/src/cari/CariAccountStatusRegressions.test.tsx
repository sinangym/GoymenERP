/**
 * The seven K2 findings on #250 `a94b0c7` (Dirac, non-author review), one describe
 * each, each carrying the reviewer's MINIMUM regression scenario and nothing broader.
 * Same harness as CariAccountStatus.test.tsx: the real shell + router + gate +
 * boundary, a stubbed fetch. Client facts only — no server, no browser, no ENV.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { onlineManager } from '@tanstack/react-query'
import {
  A_ID,
  ACC_A,
  CHAIN,
  PARTY,
  findStateBlock,
  installFetch,
  json,
  jsonWithoutAck,
  mutationView,
  noContent,
  problem,
  renderAt,
} from './commandPageHarness'
import { resetContractReadiness } from './clientContract'
import { CARI_ACCOUNT_STATUS_TEXT as TEXT } from './cariCommandText'

const DETAIL_PATH = `/cari/${PARTY}`
const REASON = 'Bu şirket hesabındaki çalışmaya ara verildi.'

interface AccountFixture {
  version: string
  isActive: boolean
}

function accountView(account: AccountFixture) {
  return mutationView({
    companyAccounts: [{ id: ACC_A, companyId: A_ID, roles: 0, isActive: account.isActive, version: account.version }],
  })
}

/** A replayed, acknowledged definite answer: the server's STORED terminal result. */
function replayedProblem(status: number, code: string): Response {
  return new Response(JSON.stringify({ type: 'about:blank', title: 'Hata', status, code }), {
    status,
    headers: {
      'Content-Type': 'application/json',
      'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1',
      'Idempotency-Replayed': 'true',
    },
  })
}

function accountState(): string | null {
  return document.querySelector('[data-account-state]')?.getAttribute('data-account-state') ?? null
}

function actionButton(name: string): HTMLButtonElement {
  return screen.getByRole('button', { name }) as HTMLButtonElement
}

function dialog(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[role="dialog"][data-intent]')
}

function attemptNode(phase: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-attempt="${phase}"]`)
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

async function submitDeactivate(panel: HTMLElement) {
  await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
}

async function waitForAttempt(phase: string): Promise<HTMLElement> {
  return await waitFor(() => {
    const node = attemptNode(phase)
    if (node === null) throw new Error(`attempt "${phase}" not shown`)
    return node
  }, CHAIN)
}

async function waitForAccountState(expected: 'active' | 'inactive') {
  await waitFor(() => expect(accountState()).toBe(expected), CHAIN)
}

beforeEach(() => {
  resetContractReadiness()
  window.localStorage.clear()
  vi.stubGlobal('navigator', { onLine: true })
  onlineManager.setOnline(true)
})

afterEach(() => {
  onlineManager.setOnline(true)
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('P2-1 — the unresolved attempt outlives a failed retry read', () => {
  it('unknown(K) → retry GET 503 → read-retry GET 200: same K, same body, no PUT on the failed read', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    let failNextDetail = false
    let statusCalls = 0
    const fetches = installFetch({
      detail: () => {
        if (failNextDetail) {
          failNextDetail = false
          return problem(503, 'service_unavailable')
        }
        return json(accountView(account))
      },
      activeState: () => {
        statusCalls += 1
        if (statusCalls === 1) return problem(500, 'internal_error')
        account.isActive = false
        account.version = '8'
        return noContent()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const unknown = await waitForAttempt('unknown')
    const first = fetches.activeStateUpdates()[0]

    // The retry's own read fails: the card leaves the ready branch...
    failNextDetail = true
    await userEvent.click(within(unknown).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await findStateBlock('service-unavailable')
    // ...and the attempt is still here, still unresolved, still the same key.
    expect(attemptNode('unknown')).not.toBeNull()
    expect(fetches.activeStateUpdates()).toHaveLength(1)

    // The READ is retried (the state block's own retry), the record comes back...
    await userEvent.click(screen.getByRole('button', { name: /Yeniden dene|Tekrar dene/ }))
    await waitForAccountState('active')
    expect(attemptNode('unknown')).not.toBeNull()

    // ...and the explicit same-key retry sends exactly the first attempt again.
    await userEvent.click(within(attemptNode('unknown') as HTMLElement).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    const second = fetches.activeStateUpdates()[1]
    expect(second.url).toBe(first.url)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)
    await waitForAccountState('inactive')
    expect(await findStateBlock('account-status-saved')).toBeTruthy()
  })
})

describe('P2-2 — a paused post-204 read is not the fresh read', () => {
  it('GET v7 → PUT 204 → GET paused (offline): no saved claim, action closed; the real GET v8 is adopted, no extra PUT', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    const fetches = installFetch({
      detail: () => json(accountView(account)),
      activeState: () => {
        account.isActive = false
        account.version = '8'
        return noContent()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    // The connection drops between the command and the read it owes.
    onlineManager.setOnline(false)
    await submitDeactivate(panel)
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)

    const pending = await findStateBlock('read-pending')
    expect(pending.textContent).toContain(TEXT.readPending)
    expect(document.querySelector('[data-state="account-status-saved"]')).toBeNull()
    // The OLD read is still on screen and is not called fresh; the action is closed.
    expect(accountState()).toBe('active')
    expect(actionButton(TEXT.action.deactivate).disabled).toBe(true)
    expect(dialog()).toBeNull()

    onlineManager.setOnline(true)
    await waitForAccountState('inactive')
    expect(await findStateBlock('account-status-saved')).toBeTruthy()
    expect(document.querySelector('[data-state="read-pending"]')).toBeNull()
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })
})

describe('P2-3 — a replayed same-key verdict is definite; anything else stays unknown', () => {
  async function reachUnknownThen(second: () => Response) {
    let statusCalls = 0
    const fetches = installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => {
        statusCalls += 1
        return statusCalls === 1 ? problem(500, 'internal_error') : second()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const unknown = await waitForAttempt('unknown')
    await userEvent.click(within(unknown).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    return { fetches, panel }
  }

  it('same-K replayed version_conflict 409 → terminal conflict with the replay explanation, no new PUT', async () => {
    const { fetches, panel } = await reachUnknownThen(() => replayedProblem(409, 'version_conflict'))
    const failed = await waitForAttempt('failed')
    expect(failed.getAttribute('data-failure')).toBe('version-conflict')
    expect(within(panel).getByText(TEXT.replayedConflict)).toBeTruthy()
    expect(within(panel).getByRole('button', { name: TEXT.refreshRecord })).toBeTruthy()
    expect(fetches.activeStateUpdates()[1].idempotencyKey).toBe(fetches.activeStateUpdates()[0].idempotencyKey)
    expect(fetches.activeStateUpdates()).toHaveLength(2)
  })

  it('a non-replayed compatibility 409 on the re-send leaves the attempt unknown', async () => {
    const { fetches } = await reachUnknownThen(() => problem(409, 'client_contract_required'))
    await waitFor(() => expect(attemptNode('unknown')).not.toBeNull(), CHAIN)
    expect(attemptNode('failed')).toBeNull()
    expect(fetches.activeStateUpdates()).toHaveLength(2)
  })

  it('a second 500 on the re-send leaves the attempt unknown', async () => {
    const { fetches } = await reachUnknownThen(() => problem(500, 'internal_error'))
    await waitFor(() => expect(attemptNode('unknown')).not.toBeNull(), CHAIN)
    expect(fetches.activeStateUpdates()).toHaveLength(2)
  })
})

describe('P2-4 — the result is the fresh read’s, not the old intent’s', () => {
  it('old deactivate replayed 204 + fresh GET active/v9: past result stated, current state announced, no list note, no second PUT', async () => {
    // Another actor reactivated the account after the first deactivation; the old key
    // now replays its 204 while the record is active at v9.
    const fetches = installFetch({
      detail: () => json(accountView({ version: '9', isActive: true })),
      activeState: () => noContent({ 'Idempotency-Replayed': 'true' }),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const saved = await findStateBlock('account-status-saved')
    expect(saved.getAttribute('data-agrees')).toBe('no')
    expect(within(saved).getByText(TEXT.savedSupersededTitle)).toBeTruthy()
    expect(within(saved).getByText(TEXT.currentStateNote(true))).toBeTruthy()
    expect(within(saved).queryByText(TEXT.savedTitle.deactivate)).toBeNull()
    expect(within(saved).queryByText(TEXT.savedListNote)).toBeNull()
    expect(screen.getByTestId('cari-account-status-announcement').textContent).toBe(TEXT.currentStateNote(true))
    expect(accountState()).toBe('active')
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })
})

describe('P2-5 — the reason survives the conflict refresh', () => {
  it('reason → 409 v7 → fresh GET v8: reason kept for review; Save is a NEW key on the current version', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    let statusCalls = 0
    const fetches = installFetch({
      detail: () => json(accountView(account)),
      activeState: () => {
        statusCalls += 1
        if (statusCalls === 1) {
          account.version = '8'
          return problem(409, 'version_conflict')
        }
        account.isActive = false
        account.version = '9'
        return noContent()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const failed = await waitForAttempt('failed')
    await userEvent.click(within(failed).getByRole('button', { name: TEXT.refreshRecord }))
    expect(await within(panel).findByText(TEXT.refreshedNote, {}, CHAIN)).toBeTruthy()
    expect((within(panel).getByLabelText(TEXT.reason) as HTMLTextAreaElement).value).toBe(REASON)
    expect(fetches.activeStateUpdates()).toHaveLength(1)

    // Reviewed and re-sent: a new attempt, a new key, the fence the read just gave.
    await submitDeactivate(panel)
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    const [first, second] = fetches.activeStateUpdates()
    expect(second.idempotencyKey).not.toBe(first.idempotencyKey)
    expect(second.body).toEqual({ isActive: false, reason: REASON, expectedVersion: '8' })
    await waitForAccountState('inactive')
  })
})

describe('P2-6 — no Save before the own acknowledged read', () => {
  it('valid GET 200 without ACK: action closed, PUT 0, read offered; ACKed GET opens it', async () => {
    const view = accountView({ version: '7', isActive: true })
    let detailCalls = 0
    const fetches = installFetch({
      detail: () => {
        detailCalls += 1
        return detailCalls === 1 ? jsonWithoutAck(view) : json(view)
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    expect(actionButton(TEXT.action.deactivate).disabled).toBe(true)
    const blocked = await findStateBlock('action-blocked-contract-not-ready')
    expect(blocked.textContent).toContain(TEXT.contractNotReady)
    expect(fetches.activeStateUpdates()).toHaveLength(0)

    await userEvent.click(within(blocked).getByRole('button', { name: TEXT.readAgain }))
    await waitFor(() => expect(actionButton(TEXT.action.deactivate).disabled).toBe(false), CHAIN)
    expect(document.querySelector('[data-state="action-blocked-contract-not-ready"]')).toBeNull()
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)
  })
})

describe('P2-7 — focus and the Tab ring while the form is locked', () => {
  it('unknown → Escape → keep: focus stays on an enabled control in the panel, Escape works, Tab skips disabled fields', async () => {
    installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => problem(503, 'service_unavailable'),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    await waitForAttempt('unknown')
    const reasonField = within(panel).getByLabelText(TEXT.reason)

    await userEvent.keyboard('{Escape}')
    const confirm = within(panel).getByRole('alertdialog')
    await userEvent.click(within(confirm).getByRole('button', { name: TEXT.closeUnresolvedKeep }))
    await waitFor(() => expect(within(panel).queryByRole('alertdialog')).toBeNull())

    const inPanelAndEnabled = () => {
      const active = document.activeElement as HTMLElement | null
      expect(active).not.toBeNull()
      expect(panel.contains(active)).toBe(true)
      expect(active).not.toBe(reasonField)
      expect(active?.matches(':disabled')).toBe(false)
      expect(active?.closest('fieldset:disabled')).toBeNull()
    }
    inPanelAndEnabled()

    // Escape still reaches the panel.
    await userEvent.keyboard('{Escape}')
    expect(within(panel).getByRole('alertdialog')).toBeTruthy()
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.closeUnresolvedKeep }))
    await waitFor(() => expect(within(panel).queryByRole('alertdialog')).toBeNull())

    // Tab and Shift+Tab only ever land on enabled controls, and never leave the panel.
    for (let i = 0; i < 6; i += 1) {
      await userEvent.tab()
      inPanelAndEnabled()
    }
    for (let i = 0; i < 6; i += 1) {
      await userEvent.tab({ shift: true })
      inPanelAndEnabled()
    }
  })
})
