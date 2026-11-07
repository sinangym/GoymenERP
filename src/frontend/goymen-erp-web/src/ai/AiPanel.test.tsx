import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { AI_PANEL_CAPABILITY } from './aiCapabilities'
import { AI_TEXT } from './aiText'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const USER_A = '11111111-1111-1111-1111-111111111111'

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function installFetch(capabilities: readonly string[] = []) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const companyId = (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
    if (url.includes('/identity/me')) {
      return json({
        userId: USER_A,
        displayName: 'Kullanıcı A',
        email: 'test@example.com',
        allowedCompanyIds: [A_ID, B_ID],
        authenticationSessionBinding: `v1.${'a1'.repeat(32)}`,
      })
    }
    if (url.includes('/organization/companies')) {
      return json([
        { id: A_ID, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true },
        { id: B_ID, code: 'GSP', legalName: 'Göymen B', isPlaceholder: true },
      ])
    }
    if (url.includes('/shell/capabilities')) {
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: companyId === B_ID ? 'gen-b-1' : 'gen-a-1',
        capabilities,
      })
    }
    // Plan AI-5: the panel has no AI endpoint to call; any AI request would be a mock answer.
    throw new Error(`unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderShell() {
  const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
}

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

describe('Plan AI-5 — sağ AI paneli', () => {
  it('her sayfada açma düğmesi var; panel kapalı başlar ve açılınca AI kapalı durumunu dürüstçe gösterir', async () => {
    const fetchMock = installFetch()
    renderShell()
    const toggle = await screen.findByRole('button', { name: AI_TEXT.toggle })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()

    await userEvent.click(toggle)
    const panel = screen.getByRole('complementary', { name: AI_TEXT.panelLabel })
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(toggle.getAttribute('aria-controls')).toBe(panel.id)
    expect(within(panel).getByText(AI_TEXT.unavailableTitle)).toBeTruthy()
    expect(within(panel).getByText(AI_TEXT.unavailableBody)).toBeTruthy()
    // No input, no send button, no mock answer: AI is off and the panel says so.
    expect(within(panel).queryByRole('textbox')).toBeNull()
    expect(within(panel).getAllByRole('button')).toHaveLength(1)
    // ERP stays usable: the page content is still there next to the panel.
    expect(screen.getByText('Ortak kabuk hazır')).toBeTruthy()
    expect(fetchMock.mock.calls.some(([input]) => String(input).includes('/ai/'))).toBe(false)
  })

  it('yetenek bildirilse de runtime yokken sahte cevap veya giriş alanı çizilmez', async () => {
    installFetch([AI_PANEL_CAPABILITY])
    renderShell()
    await userEvent.click(await screen.findByRole('button', { name: AI_TEXT.toggle }))
    const panel = screen.getByRole('complementary', { name: AI_TEXT.panelLabel })
    expect(within(panel).getByText(AI_TEXT.unavailableTitle)).toBeTruthy()
    expect(within(panel).queryByRole('textbox')).toBeNull()
  })

  it('Escape ve kapat düğmesi paneli kapatır; odak açma düğmesine döner', async () => {
    installFetch()
    renderShell()
    const toggle = await screen.findByRole('button', { name: AI_TEXT.toggle })
    await userEvent.click(toggle)
    expect(document.activeElement?.id).toBe('ai-panel-heading')
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()
    expect(document.activeElement).toBe(toggle)

    await userEvent.click(toggle)
    await userEvent.click(screen.getByRole('button', { name: AI_TEXT.close }))
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()
    expect(document.activeElement).toBe(toggle)
  })

  it('Escape panel dışındayken (sayfada) paneli kapatmaz; panel kapanınca odak dışarıdaysa yerinde kalır', async () => {
    installFetch()
    renderShell()
    const toggle = await screen.findByRole('button', { name: AI_TEXT.toggle })
    await userEvent.click(toggle)
    const main = screen.getByRole('main')
    main.focus()
    await userEvent.keyboard('{Escape}')
    expect(screen.getByRole('complementary', { name: AI_TEXT.panelLabel })).toBeTruthy()
    // Closing from the toggle while focus is on the toggle: focus stays on the toggle.
    await userEvent.click(toggle)
    expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull()
    expect(document.activeElement).toBe(toggle)
  })

  it('şirket değişince panel kapanır; panel durumu başka şirket bağlamına taşınmaz', async () => {
    installFetch()
    renderShell()
    await userEvent.click(await screen.findByRole('button', { name: AI_TEXT.toggle }))
    expect(screen.getByRole('complementary', { name: AI_TEXT.panelLabel })).toBeTruthy()

    const picker = screen.getByRole('combobox')
    await userEvent.selectOptions(picker, B_ID)
    await waitFor(() => expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(B_ID))
    // Focus was on the company picker, not in the panel: closing the panel does not move it to the AI toggle.
    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: AI_TEXT.toggle }))
    await waitFor(() => expect(screen.queryByRole('complementary', { name: AI_TEXT.panelLabel })).toBeNull())
    expect(screen.getByRole('button', { name: AI_TEXT.toggle }).getAttribute('aria-expanded')).toBe('false')
  })
})
