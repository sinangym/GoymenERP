/**
 * "Has the read I asked for landed?" must not be answered with a clock.
 *
 * The detail query's `dataUpdatedAt` is `Date.now()`, so two reads that land inside the
 * same millisecond carry the SAME stamp — indistinguishable from "no read landed". A
 * loaded machine produces exactly that: the event loop starves, then flushes several
 * queued callbacks in one millisecond. The screen then waits forever for a read that
 * already arrived: a definite 204 keeps "kayıt yenileniyor" and the refresh note never
 * appears. Freezing the clock makes that collision certain instead of occasional, so
 * these two cases fail on a stamp comparison and pass on the query's own read count.
 *
 * They are about the landing SIGNAL only; the wording, the single PUT and the kept key
 * are covered by CariAccountStatus.test.tsx and the regression files next to it.
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

function accountView(version: string, isActive: boolean) {
  return mutationView({ companyAccounts: [{ id: ACC_A, companyId: A_ID, roles: 0, isActive, version }] })
}

function accountState(): string | null {
  return document.querySelector('[data-account-state]')?.getAttribute('data-account-state') ?? null
}

/**
 * Every read reports the same `Date.now()`, whatever the real clock does — so the read a
 * baseline is taken from and the read that answers it share one stamp. It must be in
 * place BEFORE the first read lands, because the baseline is taken from that read.
 */
function collapseReadStamps() {
  const now = Date.now()
  vi.spyOn(Date, 'now').mockReturnValue(now)
}

async function deactivateWithReason() {
  await userEvent.click(screen.getByRole('button', { name: TEXT.action.deactivate }))
  const panel = await waitFor(() => {
    const node = document.querySelector<HTMLElement>('[role="dialog"][data-intent]')
    if (node === null) throw new Error('status dialog not open')
    return node
  }, CHAIN)
  const field = within(panel).getByLabelText(TEXT.reason)
  await userEvent.clear(field)
  await userEvent.type(field, REASON)
  await userEvent.click(within(panel).getByRole('button', { name: TEXT.submit.deactivate }))
  return panel
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

describe('a landed read is recognised by count, not by the clock', () => {
  it('adopts the fresh read after a 204 even when both reads share one millisecond', async () => {
    const account = { version: '7', isActive: true }
    const fetches = installFetch({
      detail: () => json(accountView(account.version, account.isActive)),
      activeState: () => {
        account.version = '8'
        account.isActive = false
        return noContent()
      },
    })
    collapseReadStamps()
    renderAt(DETAIL_PATH)
    await waitFor(() => expect(accountState()).toBe('active'), CHAIN)

    await deactivateWithReason()

    // The fresh read the 204 owes has landed, so the result is the READ's.
    const saved = await findStateBlock('account-status-saved')
    expect(saved.getAttribute('data-agrees')).toBe('yes')
    await waitFor(() => expect(accountState()).toBe('inactive'), CHAIN)
    // Still one PUT: recognising the read is not a reason to send anything again.
    expect(fetches.activeStateUpdates()).toHaveLength(1)
    expect(document.querySelector('[data-state="read-pending"]')).toBeNull()
  })

  it('re-seats the window on a refresh that lands in the same millisecond as the read before it', async () => {
    const account = { version: '7', isActive: true }
    const fetches = installFetch({
      detail: () => json(accountView(account.version, account.isActive)),
      activeState: () => {
        // Someone else moved the record while the window was open.
        account.version = '8'
        return problem(409, 'version_conflict')
      },
    })
    collapseReadStamps()
    renderAt(DETAIL_PATH)
    await waitFor(() => expect(accountState()).toBe('active'), CHAIN)

    const panel = await deactivateWithReason()
    const failed = await waitFor(() => {
      const node = document.querySelector<HTMLElement>('[data-attempt="failed"][data-failure="version-conflict"]')
      if (node === null) throw new Error('conflict notice not shown')
      return node
    }, CHAIN)

    const readsBefore = fetches.detailReads().length
    await userEvent.click(within(failed).getByRole('button', { name: TEXT.refreshRecord }))

    await waitFor(() => expect(fetches.detailReads().length).toBeGreaterThan(readsBefore), CHAIN)
    // The read landed, so the window says so and keeps the reason for review.
    await screen.findByText(TEXT.refreshedNote, {}, CHAIN)
    expect((within(panel).getByLabelText(TEXT.reason) as HTMLTextAreaElement).value).toBe(REASON)
    expect(fetches.activeStateUpdates()).toHaveLength(1)
  })
})
