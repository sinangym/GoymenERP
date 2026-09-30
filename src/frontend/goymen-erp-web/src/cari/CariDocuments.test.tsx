/**
 * AT-065 on C-03 in the real shell (component evidence, NOT_ENV): the Cari card shows its documents panel under the
 * active company; a file uploaded there is on the card again after the card is closed and reopened; the panel is
 * absent when the vault answers 403; the documents read carries the active company and never another's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { PARTY_ROLE_FLAG } from './contract'
import { DOCUMENTS_TEXT } from '../documents/documentsText'

const A_ID = 'aaaaaaaa-0000-4000-8000-000000000001'
const B_ID = 'bbbbbbbb-0000-4000-8000-000000000002'
const PARTY = '11111111-1111-4111-8111-111111111111'
const ACC = 'cccccccc-1111-4111-8111-111111111111'
const CHAIN = { timeout: 5000 } as const
const LIMITS = { maximumBytes: 20971520, extensions: ['.pdf', '.png', '.jpg', '.jpeg'], contentTypes: ['application/pdf', 'image/png', 'image/jpeg'] }

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function documentRow(fileName: string) {
  return {
    id: 'd0000000-0000-4000-8000-000000000001',
    companyId: A_ID,
    ownerKind: 'counterparty.party',
    ownerId: PARTY,
    versionNo: 1,
    fileName,
    contentType: 'application/pdf',
    sizeBytes: 4,
    sha256: 'b'.repeat(64),
    scanState: 'Clean',
    downloadable: true,
    uploadedBy: 'company-a-user',
    uploadedAtUtc: '2026-09-30T09:00:00Z',
  }
}

/** A tiny server: the shell's reads, the Cari detail, and a document store that remembers uploads across renders. */
function installServer(documentsStatus = 200) {
  const stored: ReturnType<typeof documentRow>[] = []
  const documentCalls: { method: string; companyId: string | null }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers = (init?.headers ?? {}) as Record<string, string>
      const companyId = headers['X-Company-Id'] ?? null
      if (url.includes('/identity/me'))
        return json({ userId: '99999999-9999-4999-8999-999999999999', displayName: 'Kullanıcı A', email: null, allowedCompanyIds: [A_ID, B_ID] })
      if (url.includes('/organization/companies'))
        return json([
          { id: A_ID, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
          { id: B_ID, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
        ])
      if (url.includes('/shell/capabilities'))
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: `gen-${companyId?.slice(-1) ?? 'x'}`,
          capabilities: [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail],
        })
      if (url.includes('/documents')) {
        documentCalls.push({ method: init?.method ?? 'GET', companyId })
        if (documentsStatus !== 200) return json({ title: 'x' }, documentsStatus)
        if (init?.method === 'POST') {
          const file = (init.body as FormData).get('file') as File
          const row = documentRow(file.name)
          stored.push(row)
          return json({ document: row, duplicates: [], isReplay: false, originalCorrelationId: 'c-1' }, 201)
        }
        return json({ items: [...stored], canUpload: true, limits: LIMITS })
      }
      if (/\/counterparties\/[^?]+/.test(url))
        return json({
          id: PARTY,
          code: 'CAR-000001',
          kind: 1,
          legalName: 'Örnek Metal Sanayi ve Ticaret A.Ş.',
          tradeName: 'Örnek Metal',
          isActive: true,
          version: '3',
          companyAccounts: [{ id: ACC, companyId: A_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: true, version: '2' }],
        })
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return { stored, documentCalls }
}

function renderCard() {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [`/cari/${PARTY}`] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('AT-065 documents on the Cari card (C-03)', () => {
  it('uploads a document on the card and shows it again after the card is closed and reopened', async () => {
    const server = installServer()
    renderCard()
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)
    const panel = (await screen.findByRole('heading', { name: DOCUMENTS_TEXT.heading }, CHAIN)).closest('section') as HTMLElement
    expect(within(panel).getByText(DOCUMENTS_TEXT.none)).toBeTruthy()

    await userEvent.upload(within(panel).getByLabelText(DOCUMENTS_TEXT.fileLabel), new File(['%PDF'], 'Sözleşme.pdf', { type: 'application/pdf' }))
    await userEvent.click(within(panel).getByRole('button', { name: DOCUMENTS_TEXT.upload }))
    expect(await within(panel).findByText(DOCUMENTS_TEXT.uploaded('Sözleşme.pdf'))).toBeTruthy()

    // Close and reopen the card: a fresh mount, a fresh read, the document is there.
    cleanup()
    renderCard()
    const reopened = (await screen.findByRole('heading', { name: DOCUMENTS_TEXT.heading }, CHAIN)).closest('section') as HTMLElement
    expect(await within(reopened).findByRole('rowheader', { name: 'Sözleşme.pdf' })).toBeTruthy()
    expect(within(reopened).getByRole('button', { name: DOCUMENTS_TEXT.download('Sözleşme.pdf') })).toBeTruthy()

    expect(server.stored).toHaveLength(1)
    expect(server.documentCalls.every((call) => call.companyId === A_ID)).toBe(true)
  })

  it('shows no documents panel at all when the vault answers 403', async () => {
    const server = installServer(403)
    renderCard()
    await screen.findByRole('heading', { name: /Örnek Metal/ }, CHAIN)
    await vi.waitFor(() => expect(server.documentCalls.length).toBeGreaterThan(0))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(screen.queryByRole('heading', { name: DOCUMENTS_TEXT.heading })).toBeNull()
    expect(document.querySelector('[data-section="documents"]')).toBeNull()
  })
})
