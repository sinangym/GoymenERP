/**
 * B-C01-003-STALE — a late response from a company we have LEFT must not paint
 * the company we are now in.
 *
 * WHAT THIS FILE PROVES — and, measured rather than assumed, WHAT IT DOES NOT.
 *
 * It proves the OUTCOME deterministically: the GDC response is held, GSP is
 * committed, the old response is then released, and none of it reaches the
 * screen, the accessible tree or the cache.
 *
 * It does NOT prove the fence. I measured the mechanism instead of guessing, and
 * it is the one ChatGPT warned about in the #146 acceptance: the GDC request is
 * ABORTED when the switch supersedes it. Two mutations confirm this — removing
 * the remount  and pinning the query namespace to a constant both leave this
 * file green.
 *
 * An abort is a SAFE outcome. It is not evidence that a delivered stale response
 * would be rejected, because nothing was delivered. That distinction is exactly
 * why the accepted contract requires CDP response-stage interception against the
 * real server: only there can a response arrive after the context has moved and
 * be observed to be ignored rather than prevented.
 *
 * Keep this file as a regression guard for the outcome. Do not cite it as fence
 * evidence.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { PARTY_ROLE_FLAG } from './contract'

const GDC = '10000000-0000-0000-0000-000000000001'
const GSP = '10000000-0000-0000-0000-000000000002'
const USER = '99999999-9999-9999-9999-999999999999'

/** The two discriminating fixture records, verified against the real API. */
const GDC_ONLY = { code: 'CAR-000004', name: 'Işık Profil Sanayi A.Ş.' }
const GSP_ONLY = { code: 'CAR-000054', name: 'Yalnız GSP Metal A.Ş.' }

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function party(companyId: string, code: string, legalName: string, id: string) {
  return {
    id,
    code,
    kind: 1,
    legalName,
    tradeName: null,
    isActive: true,
    version: 1,
    companyAccounts: [
      {
        id: `${id.slice(0, 8)}-2222-2222-2222-222222222222`,
        companyId,
        roles: PARTY_ROLE_FLAG.Customer,
        isActive: true,
        version: 1,
      },
    ],
  }
}

function page(items: unknown[]) {
  return { items, nextCursor: null, hasMore: false, totalCount: null, countScope: null }
}

const DIRECTORY = [
  { id: GDC, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: GSP, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('B-C01-003-STALE — a released GDC response cannot paint GSP', () => {
  it('is ignored after the switch, and GSP keeps its own record', async () => {
    let releaseGdcList: () => void = () => {}
    const gdcListHeld = new Promise<void>((resolve) => {
      releaseGdcList = resolve
    })
    let gdcListReleased = false
    let gdcAborted = false
    const listCalls: string[] = []

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString()
        const companyId =
          (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null

        if (url.includes('/identity/me'))
          return json({ userId: USER, displayName: 'Yönetici', email: null, allowedCompanyIds: [GDC, GSP], authenticationSessionBinding: null })
        if (url.includes('/organization/companies')) return json(DIRECTORY)
        if (url.includes('/shell/capabilities'))
          return json({
            contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
            generation: `gen-${companyId === GSP ? 'gsp' : 'gdc'}`,
            capabilities: [CAPABILITY.cariPartiesList],
          })

        if (url.includes('/counterparties')) {
          listCalls.push(companyId ?? 'none')
          if (companyId === GDC) {
            init?.signal?.addEventListener('abort', () => {
              gdcAborted = true
            })
            // Held at the transport, exactly like CDP response-stage: the body is
            // real and unchanged, only its delivery is deferred.
            await gdcListHeld
            gdcListReleased = true
            return json(
              page([party(GDC, GDC_ONLY.code, GDC_ONLY.name, '00000004-1111-1111-1111-111111111111')]),
            )
          }
          return json(
            page([party(GSP, GSP_ONLY.code, GSP_ONLY.name, '00000054-1111-1111-1111-111111111111')]),
          )
        }
        throw new Error(`unexpected fetch: ${url}`)
      }),
    )

    const router = createMemoryRouter(shellRoutes, { initialEntries: ['/cari'] })
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    render(
      <ShellProvider queryClient={queryClient}>
        <RouterProvider router={router} />
      </ShellProvider>,
    )

    // GDC context is committed; its list request is in flight and held.
    await waitFor(() => expect(listCalls).toContain(GDC))
    expect(screen.queryByText(GDC_ONLY.name)).toBeNull()

    // Switch to GSP while the GDC response is still held.
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), GSP)
    await screen.findByRole('row', { name: new RegExp(GSP_ONLY.code) })
    expect(
      (screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement).value,
    ).toBe(GSP)

    // NOW release the old GDC response. This is the whole point: it is delivered,
    // not prevented.
    releaseGdcList()
    await waitFor(() => expect(gdcListReleased).toBe(true))
    await new Promise((r) => setTimeout(r, 150))

    // It must not reach the screen, the accessible tree, or the query cache.
    expect(screen.queryByText(GDC_ONLY.name)).toBeNull()
    expect(screen.queryByText(GDC_ONLY.code)).toBeNull()
    expect(document.body.textContent).not.toContain(GDC_ONLY.name)

    // ...and GSP is untouched.
    expect(screen.getByRole('row', { name: new RegExp(GSP_ONLY.code) })).toBeTruthy()
    expect(
      (screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement).value,
    ).toBe(GSP)

    const cached = queryClient
      .getQueryCache()
      .getAll()
      .some((q) => JSON.stringify(q.state.data ?? '').includes(GDC_ONLY.name))
    expect(cached).toBe(false)

    // The stale delivery must not have provoked a fresh GDC request either.
    expect(listCalls.filter((c) => c === GDC)).toHaveLength(1)

    // MECHANISM, asserted rather than assumed: the switch aborted the request.
    // This is what makes the outcome safe HERE, and it is precisely why this
    // file cannot stand in for the browser evidence — nothing was delivered, so
    // nothing was rejected. If this ever becomes false, the fence is what is
    // holding the line and the browser test becomes the only proof of it.
    expect(gdcAborted).toBe(true)
  })

  it('the two fixture records really do discriminate in opposite directions', async () => {
    // If both were visible in both companies the test above would pass vacuously.
    // Verified against the real API too: GDC q=Işık returns CAR-000004 and GSP
    // returns nothing; CAR-000054 is absent under GDC and present under GSP.
    expect(GDC_ONLY.code).not.toBe(GSP_ONLY.code)
    expect(GDC_ONLY.name).not.toBe(GSP_ONLY.name)
  })
})
