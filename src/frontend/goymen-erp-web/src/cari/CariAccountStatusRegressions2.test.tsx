/**
 * Round two of the K2 review on #250 (Dirac on 4708820): the four remaining findings,
 * one describe each, the reviewer's minimum scenario at component level. Finding 1's
 * transport boundary lives in commandApiReplay.test.ts; here it is the component
 * consequence (an unverified replay keeps the key).
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  A_ID,
  ACC_A,
  CHAIN,
  PARTY,
  findStateBlock,
  installFetch,
  json,
  mutationView,
  noContent,
  problem,
  renderAt,
} from './commandPageHarness'
import { resetContractReadiness } from './clientContract'
import { CARI_ACCOUNT_STATUS_TEXT as TEXT } from './cariCommandText'

const DETAIL_PATH = `/cari/${PARTY}`
const REASON = 'Bu şirket hesabındaki çalışmaya ara verildi.'
const RETRY = 'Aynı işlemi yeniden dene'

interface AccountFixture {
  version: string
  isActive: boolean
}

function accountView(account: AccountFixture) {
  return mutationView({
    companyAccounts: [{ id: ACC_A, companyId: A_ID, roles: 0, isActive: account.isActive, version: account.version }],
  })
}

/** A replay marker on an answer the compatibility filter did NOT acknowledge. */
function unacknowledgedReplay(status: number, code: string): Response {
  return new Response(JSON.stringify({ type: 'about:blank', title: 'Hata', status, code }), {
    status,
    headers: { 'Content-Type': 'application/json', 'Idempotency-Replayed': 'true' },
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
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('R2-1 — an unverified replay on the re-send keeps the key', () => {
  it('unknown(K) → own GET+ACK → same-K re-send answered by an UNACKNOWLEDGED replay 409: still unknown, K kept', async () => {
    let statusCalls = 0
    const fetches = installFetch({
      detail: () => json(accountView({ version: '7', isActive: true })),
      activeState: () => {
        statusCalls += 1
        return statusCalls === 1 ? problem(500, 'internal_error') : unacknowledgedReplay(409, 'version_conflict')
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const unknown = await waitForAttempt('unknown')
    const first = fetches.activeStateUpdates()[0]
    await userEvent.click(within(unknown).getByRole('button', { name: RETRY }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    // Not terminal: no conflict notice, no replay explanation, the same attempt still open.
    await waitFor(() => expect(attemptNode('unknown')).not.toBeNull(), CHAIN)
    expect(attemptNode('failed')).toBeNull()
    expect(screen.queryByText(TEXT.replayedConflict)).toBeNull()
    // And a further explicit retry is still THAT attempt.
    await userEvent.click(within(attemptNode('unknown') as HTMLElement).getByRole('button', { name: RETRY }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(3), CHAIN)
    expect(fetches.activeStateUpdates()[2].idempotencyKey).toBe(first.idempotencyKey)
    expect(fetches.activeStateUpdates()[2].body).toEqual(first.body)
  })
})

describe('R2-2 — the recovery survives a compatibility 409 on the retry read', () => {
  it('unknown(K) → retry own GET 409 client_contract_required → in-place re-read → same-K retry', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    let refuseNextDetail = false
    let statusCalls = 0
    const fetches = installFetch({
      detail: () => {
        if (refuseNextDetail) {
          refuseNextDetail = false
          return problem(409, 'client_contract_required')
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

    refuseNextDetail = true
    await userEvent.click(within(unknown).getByRole('button', { name: RETRY }))
    const refusal = await findStateBlock('client-contract')
    expect(refusal).toBeTruthy()
    // The uncertainty and the key are still on screen, with a read offered IN PLACE.
    const recovery = await findStateBlock('contract-refusal-recovery')
    expect(recovery.textContent).toContain(TEXT.contractRefusalRecovery)
    expect(within(recovery).queryByText('İşlem sonucu belirsiz')).not.toBeNull()
    expect(fetches.activeStateUpdates()).toHaveLength(1)

    // Compatibility is back in this context: re-read here, then the same attempt.
    await userEvent.click(within(recovery).getByRole('button', { name: TEXT.readAgainInPlace }))
    await waitForAccountState('active')
    await userEvent.click(within(await waitForAttempt('unknown')).getByRole('button', { name: RETRY }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    const second = fetches.activeStateUpdates()[1]
    expect(second.url).toBe(first.url)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)
    await waitForAccountState('inactive')
  })
})

describe('R2-3 — the frozen snapshot is never re-seated under an unresolved attempt', () => {
  it('unknown deactivate(K, v7) while the read moved to inactive/v8: the window keeps showing v7/deactivate, no "Güncel sürüme geç", retry sends K unchanged', async () => {
    const account: AccountFixture = { version: '7', isActive: true }
    let statusCalls = 0
    const fetches = installFetch({
      detail: () => json(accountView(account)),
      activeState: () => {
        statusCalls += 1
        // Every send is lost; after the first, another actor has deactivated at v8.
        account.isActive = false
        account.version = '8'
        return problem(500, 'internal_error')
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const unknown = await waitForAttempt('unknown')
    const first = fetches.activeStateUpdates()[0]

    // The retry's own read now shows inactive/v8; the re-send is lost again.
    await userEvent.click(within(unknown).getByRole('button', { name: RETRY }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    await waitForAccountState('inactive')
    await waitFor(() => expect(attemptNode('unknown')).not.toBeNull(), CHAIN)

    // The window is honest about the record moving — and refuses to re-seat the view.
    const changed = await within(panel).findByText(TEXT.changedWhileFrozen, {}, CHAIN)
    expect(changed).toBeTruthy()
    expect(within(panel).queryByRole('button', { name: TEXT.adoptFresh })).toBeNull()
    expect(panel.getAttribute('data-intent')).toBe('deactivate')
    expect(panel.querySelector('[data-target-state]')?.textContent).toBe(TEXT.inactive)
    expect(within(panel).getByRole('heading', { name: TEXT.title.deactivate })).toBeTruthy()

    // What is shown is what is sent: the same frozen attempt, once more.
    await userEvent.click(within(attemptNode('unknown') as HTMLElement).getByRole('button', { name: RETRY }))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(3), CHAIN)
    const third = fetches.activeStateUpdates()[2]
    expect(third.idempotencyKey).toBe(first.idempotencyKey)
    expect(third.body).toEqual(first.body)
    expect((third.body as { expectedVersion: string }).expectedVersion).toBe('7')
  })
})

describe('R2-4 — a normal 204 whose fresh read differs is not called a replay', () => {
  it('replay=false 204 for deactivate + fresh GET active/v9: applied-then-changed wording, current state, no replay claim', async () => {
    let applied = false
    const fetches = installFetch({
      detail: () => json(accountView(applied ? { version: '9', isActive: true } : { version: '7', isActive: true })),
      activeState: () => {
        applied = true
        return noContent()
      },
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const saved = await findStateBlock('account-status-saved')
    expect(saved.getAttribute('data-agrees')).toBe('no')
    expect(within(saved).getByText(TEXT.savedThenChangedTitle)).toBeTruthy()
    expect(within(saved).getByText(TEXT.currentStateNote(true))).toBeTruthy()
    expect(within(saved).queryByText(TEXT.savedSupersededTitle)).toBeNull()
    expect(within(saved).queryByText(TEXT.savedSupersededDetail)).toBeNull()
    expect(within(saved).queryByText(TEXT.savedReplayed)).toBeNull()
    expect(within(saved).queryByText(TEXT.savedListNote)).toBeNull()
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })

  it('replay=true 204 with a differing fresh read keeps the replay wording (unchanged)', async () => {
    installFetch({
      detail: () => json(accountView({ version: '9', isActive: true })),
      activeState: () => noContent({ 'Idempotency-Replayed': 'true' }),
    })
    renderAt(DETAIL_PATH)
    await waitForAccountState('active')
    const panel = await openDialog(TEXT.action.deactivate)
    await fillReason(panel, REASON)
    await submitDeactivate(panel)
    const saved = await findStateBlock('account-status-saved')
    expect(within(saved).getByText(TEXT.savedSupersededTitle)).toBeTruthy()
    expect(within(saved).queryByText(TEXT.savedThenChangedTitle)).toBeNull()
  })
})
