/**
 * ALS-D99 — the deactivation warning inside the C-03 account status window.
 *
 * Component tests over the real shell + router + capability gate, with a stubbed `fetch`. They measure what is SENT:
 * an open balance or open documents never block the deactivation, but the PUT goes out only after the warning was
 * read and, when it requires it, acknowledged — and the acknowledgement is on the wire only then. Whether the server
 * really refuses without it is Backend's SQL acceptance fact, not this file's.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  A_ID,
  B_ID,
  ACC_A,
  CHAIN,
  PARTY,
  deactivationWarningView,
  installFetch,
  json,
  mutationView,
  noContent,
  problem,
  renderAt,
  settle,
} from './commandPageHarness'
import { resetContractReadiness } from './clientContract'
import { CARI_ACCOUNT_STATUS_TEXT as TEXT } from './cariCommandText'

const DETAIL_PATH = `/cari/${PARTY}`
const REASON = 'Bu şirket hesabındaki çalışmaya ara verildi.'

function activeAccount() {
  return mutationView({
    companyAccounts: [{ id: ACC_A, companyId: A_ID, roles: 0, isActive: true, version: '7' }],
  })
}

function openWarning() {
  return deactivationWarningView(PARTY, A_ID, {
    accountVersion: '7',
    items: [
      { kind: 'finance-open-balance', outcome: 'open', openItemCount: 3 },
      { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
      { kind: 'purchasing-open-documents', outcome: 'clear', openItemCount: null },
    ],
    acknowledgementRequired: true,
  })
}

function dialog(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[role="dialog"][data-intent]')
}

async function openDeactivateDialog(): Promise<HTMLElement> {
  await waitFor(
    () => expect(document.querySelector('[data-account-state]')?.getAttribute('data-account-state')).toBe('active'),
    CHAIN,
  )
  await userEvent.click(screen.getByRole('button', { name: TEXT.action.deactivate }))
  const panel = await waitFor(() => {
    const node = dialog()
    if (node === null) throw new Error('status dialog not open')
    return node
  }, CHAIN)
  await userEvent.type(within(panel).getByLabelText(TEXT.reason), REASON)
  return panel
}

function submit(panel: HTMLElement): HTMLButtonElement {
  return within(panel).getByRole('button', { name: TEXT.submit.deactivate }) as HTMLButtonElement
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

describe('ALS-D99: the warning is read for the selected company and party when a deactivate window opens', () => {
  it('reads it with the company header; a clear warning leaves the body exactly as before', async () => {
    const fetches = installFetch({ detail: () => json(activeAccount()), activeState: () => noContent() })
    renderAt(DETAIL_PATH)
    const panel = await openDeactivateDialog()
    await waitFor(() => expect(within(panel).getByText(TEXT.warning.clear)).toBeTruthy(), CHAIN)
    const read = fetches.deactivationWarningReads()[0]
    expect(read.companyId).toBe(A_ID)
    expect(read.url).toMatch(new RegExp(`/counterparties/${PARTY}/company-account/deactivation-warning$`))
    expect(within(panel).queryByRole('checkbox')).toBeNull()

    await userEvent.click(submit(panel))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)
    expect(fetches.activeStateUpdates()[0].body).toEqual({ isActive: false, reason: REASON, expectedVersion: '7' })
  })
})

describe('ALS-D99: an open line asks for an acknowledgement, never blocks the change', () => {
  it('lists every line and sends nothing until the box is ticked; then the acknowledgement is on the wire', async () => {
    const fetches = installFetch({
      detail: () => json(activeAccount()),
      deactivationWarning: () => json(openWarning()),
      activeState: () => noContent(),
    })
    renderAt(DETAIL_PATH)
    const panel = await openDeactivateDialog()
    const list = await waitFor(() => {
      const node = panel.querySelector<HTMLElement>('ul[data-warning-items]')
      if (node === null) throw new Error('warning list not shown')
      return node
    }, CHAIN)
    expect(within(list).getByText(new RegExp(TEXT.warning.outcome.open(3)))).toBeTruthy()
    expect(within(list).getByText(new RegExp(TEXT.warning.outcome.unavailable))).toBeTruthy()
    expect(list.querySelectorAll('li')).toHaveLength(3)

    await userEvent.click(submit(panel))
    await settle()
    expect(fetches.activeStateUpdates()).toHaveLength(0)
    expect(within(panel).getByRole('alert').textContent).toBe(TEXT.warning.acknowledgeRequired)
    const acknowledgement = within(panel).getByRole('checkbox', { name: TEXT.warning.acknowledge })
    expect(acknowledgement.getAttribute('aria-invalid')).toBe('true')
    expect(acknowledgement.getAttribute('aria-describedby')).toBe('cari-account-status-warning-ack-error')
    expect(panel.querySelector('#cari-account-status-warning-ack-error')?.textContent)
      .toBe(TEXT.warning.acknowledgeRequired)

    await userEvent.click(acknowledgement)
    await userEvent.click(submit(panel))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)
    expect(fetches.activeStateUpdates()[0].body).toEqual({
      isActive: false,
      reason: REASON,
      expectedVersion: '7',
      exposureWarningAcknowledged: true,
    })
  })

  it('a refusal for a missing acknowledgement re-reads the warning and withdraws the tick', async () => {
    let answers = 0
    const fetches = installFetch({
      detail: () => json(activeAccount()),
      deactivationWarning: () => json(openWarning()),
      activeState: () => {
        answers += 1
        return answers === 1
          ? problem(422, 'deactivation_warning_acknowledgement_required')
          : noContent()
      },
    })
    renderAt(DETAIL_PATH)
    const panel = await openDeactivateDialog()
    await userEvent.click(await within(panel).findByRole('checkbox', { name: TEXT.warning.acknowledge }, CHAIN))
    await userEvent.click(submit(panel))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(1), CHAIN)

    await waitFor(() => expect(within(panel).getByText(TEXT.warning.changed)).toBeTruthy(), CHAIN)
    expect(fetches.deactivationWarningReads().length).toBeGreaterThanOrEqual(2)
    const box = within(panel).getByRole('checkbox', { name: TEXT.warning.acknowledge }) as HTMLInputElement
    expect(box.checked).toBe(false)

    await userEvent.click(box)
    await userEvent.click(submit(panel))
    await waitFor(() => expect(fetches.activeStateUpdates()).toHaveLength(2), CHAIN)
    // A new attempt, not a replay of the refused one.
    expect(fetches.activeStateUpdates()[1].idempotencyKey).not.toBe(fetches.activeStateUpdates()[0].idempotencyKey)
  })
})

describe('ALS-D99: a warning that cannot be read keeps the submit closed', () => {
  it('a failed read shows the retry; the retry reads again and a clear answer opens the submit', async () => {
    let reads = 0
    const fetches = installFetch({
      detail: () => json(activeAccount()),
      deactivationWarning: () => {
        reads += 1
        return reads === 1 ? problem(503, null) : json(deactivationWarningView(PARTY, A_ID))
      },
      activeState: () => noContent(),
    })
    renderAt(DETAIL_PATH)
    const panel = await openDeactivateDialog()
    await waitFor(() => expect(within(panel).getByText(TEXT.warning.failedTitle)).toBeTruthy(), CHAIN)
    expect(submit(panel).disabled).toBe(true)
    await userEvent.click(within(panel).getByRole('button', { name: TEXT.warning.retry }))
    await waitFor(() => expect(within(panel).getByText(TEXT.warning.clear)).toBeTruthy(), CHAIN)
    expect(submit(panel).disabled).toBe(false)
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })

  it('a forbidden read offers no retry and sends nothing', async () => {
    const fetches = installFetch({
      detail: () => json(activeAccount()),
      deactivationWarning: () => problem(403, 'forbidden'),
    })
    renderAt(DETAIL_PATH)
    const panel = await openDeactivateDialog()
    await waitFor(() => expect(within(panel).getByText(TEXT.warning.forbidden)).toBeTruthy(), CHAIN)
    expect(within(panel).queryByRole('button', { name: TEXT.warning.retry })).toBeNull()
    expect(submit(panel).disabled).toBe(true)
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })

  it('a warning bound to another company is malformed and treated as a failed read', async () => {
    const fetches = installFetch({
      detail: () => json(activeAccount()),
      deactivationWarning: () => json(deactivationWarningView(PARTY, B_ID)),
    })
    renderAt(DETAIL_PATH)
    const panel = await openDeactivateDialog()
    await waitFor(() => expect(within(panel).getByText(TEXT.warning.failedTitle)).toBeTruthy(), CHAIN)
    expect(submit(panel).disabled).toBe(true)
    expect(fetches.activeStateUpdates()).toHaveLength(0)
  })
})
