import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, onlineManager } from '@tanstack/react-query'
import { MemoryRouter, RouterProvider, createMemoryRouter, useLocation } from 'react-router'
import { ShellProvider } from '../ShellProvider'
import { ShellStateContext } from '../shellContext'
import { AppShell } from '../AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../api'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from '../capabilities'
import { fixtureContext } from '../fixtures'
import { initialShellState } from '../shellState'
import { PRICE_LIST_CAPABILITY } from '../../product/priceLists/capabilities'
import { CommandPalette } from './CommandPalette'
import { PALETTE_CREATE_CAPABILITIES } from './paletteCommands'
import { PALETTE_CREATE_LABEL, PALETTE_TEXT as T } from './paletteText'
import { recentKey, type StorageAccessor } from './recentScreens'

// SENTETIK: every actor, company and snapshot below is synthetic test data.
const COMPANY = 'aaaaaaaa-0000-0000-0000-0000000000c1'
const USER = '11111111-1111-1111-1111-1111111111c1'
const DISPLAY_NAME = 'SENTETIK Kullanici'

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

let requested: string[] = []

function installFetch(granted: readonly string[]) {
  requested = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    requested.push(`${init?.method ?? 'GET'} ${url}`)
    if (url.includes('/identity/me')) {
      return json({
        userId: USER,
        displayName: DISPLAY_NAME,
        email: null,
        allowedCompanyIds: [COMPANY],
        mustChangePassword: false, localCredentialVersion: null, authenticationSessionBinding: null,
      })
    }
    if (url.includes('/organization/companies')) {
      return json([{ id: COMPANY, code: 'SNT', legalName: 'SENTETIK Sirket', isPlaceholder: false }])
    }
    if (url.includes('/shell/capabilities')) {
      return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'sentetik-palette-gen', capabilities: granted })
    }
    throw new Error(`unexpected fetch ${url}`)
  }))
}

function LocationProbe() {
  const location = useLocation()
  return <h1 data-testid="page">{`${location.pathname}${location.search}`}</h1>
}

async function mountShell(granted: readonly string[], path = '/') {
  installFetch(granted)
  const router = createMemoryRouter(
    [{ path: '/', element: <AppShell />, children: [{ index: true, element: <LocationProbe /> }, { path: '*', element: <LocationProbe /> }] }],
    { initialEntries: [path] },
  )
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  await screen.findAllByText(DISPLAY_NAME, undefined, { timeout: 5000 })
  return router
}

const openWithKeyboard = (init: { ctrlKey?: boolean; metaKey?: boolean } = { ctrlKey: true }) =>
  act(() => {
    fireEvent.keyDown(document, { key: 'k', ...init })
  })

const queryDialog = () => screen.queryByRole('dialog', { name: T.dialog })
const dialog = () => screen.getByRole('dialog', { name: T.dialog })
const combobox = () => within(dialog()).getByRole('combobox', { name: T.inputLabel })
const labelOf = (option: HTMLElement) => option.firstElementChild?.textContent ?? ''
const optionLabels = () => within(dialog()).queryAllByRole('option').map(labelOf)
const attr = (element: Element | null | undefined, name: string) => element?.getAttribute(name) ?? null

beforeEach(() => {
  vi.stubGlobal('navigator', { onLine: true })
  window.sessionStorage.clear()
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  // TanStack's online manager is process-wide: undo an "offline" a test announced so the next test's reads run.
  onlineManager.setOnline(true)
  window.sessionStorage.clear()
})

describe('W1-B command palette in the shell', () => {
  it('opens with Ctrl+K and with ⌘K, and closes with Escape, returning focus', async () => {
    await mountShell([CAPABILITY.cariPartiesList])
    const button = screen.getByRole('button', { name: new RegExp(`^${T.open}`) })
    expect(attr(button, 'aria-keyshortcuts')).toBe('Control+K Meta+K')
    expect(attr(button, 'aria-haspopup')).toBe('dialog')
    button.focus()

    openWithKeyboard({ ctrlKey: true })
    expect(attr(dialog(), 'aria-modal')).toBe('true')
    expect(attr(button, 'aria-expanded')).toBe('true')
    await waitFor(() => expect(document.activeElement).toBe(combobox()))
    fireEvent.keyDown(combobox(), { key: 'Escape' })
    expect(queryDialog()).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(button))

    openWithKeyboard({ metaKey: true })
    expect(queryDialog()).not.toBeNull()
    // The same shortcut closes it again.
    openWithKeyboard({ metaKey: true })
    expect(queryDialog()).toBeNull()

    await userEvent.click(button)
    expect(queryDialog()).not.toBeNull()
    await userEvent.click(screen.getByTestId('command-palette-backdrop'))
    expect(queryDialog()).toBeNull()
  })

  it('is an ARIA combobox owning a listbox, moved with the arrow keys and opened with Enter', async () => {
    await mountShell([CAPABILITY.cariPartiesList, CAPABILITY.productItemsList, CAPABILITY.cariPartiesCreate])
    openWithKeyboard()
    const input = combobox()
    const listbox = within(dialog()).getByRole('listbox', { name: T.listLabel })
    expect(attr(input, 'aria-controls')).toBe(listbox.id)
    expect(attr(input, 'aria-expanded')).toBe('true')
    expect(attr(input, 'aria-autocomplete')).toBe('list')

    const options = within(listbox).getAllByRole('option')
    expect(options.map(labelOf)).toEqual([
      CAPABILITY_NAV_LABEL[CAPABILITY.productItemsList],
      CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList],
      PALETTE_CREATE_LABEL[CAPABILITY.cariPartiesCreate],
    ])
    expect(attr(input, 'aria-activedescendant')).toBe(options[0].id)
    expect(attr(options[0], 'aria-selected')).toBe('true')
    expect(attr(options[1], 'aria-selected')).toBe('false')
    for (const option of options) expect(option.hasAttribute('tabindex')).toBe(false)

    fireEvent.keyDown(input, { key: 'ArrowDown' })
    expect(attr(input, 'aria-activedescendant')).toBe(options[1].id)
    fireEvent.keyDown(input, { key: 'ArrowUp' })
    fireEvent.keyDown(input, { key: 'ArrowUp' })
    // Wraps to the last option.
    expect(attr(input, 'aria-activedescendant')).toBe(options[2].id)
    fireEvent.keyDown(input, { key: 'Tab' })
    expect(document.activeElement).toBe(input)

    fireEvent.keyDown(input, { key: 'Enter' })
    expect(queryDialog()).toBeNull()
    expect(screen.getByTestId('page').textContent).toBe(CAPABILITY_ROUTE[CAPABILITY.cariPartiesCreate])
    await waitFor(() => expect(document.activeElement).toBe(document.getElementById('main-content')))
  })

  it('finds screens by their Turkish names, Turkish-insensitively', async () => {
    await mountShell([CAPABILITY.salesDraftsList, CAPABILITY.productItemsList, CAPABILITY.salesDraftsCreate])
    openWithKeyboard()
    await userEvent.type(combobox(), 'SATIS SIPARIS')
    expect(optionLabels()).toEqual([
      CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList],
      PALETTE_CREATE_LABEL[CAPABILITY.salesDraftsCreate],
      T.searchFor('SATIS SIPARIS'),
    ])
    await userEvent.clear(combobox())
    await userEvent.type(combobox(), 'ÜRÜNLER')
    expect(optionLabels().slice(0, 1)).toEqual([CAPABILITY_NAV_LABEL[CAPABILITY.productItemsList]])
    await userEvent.clear(combobox())
    await userEvent.type(combobox(), 'q')
    expect(optionLabels()).toEqual([])
    expect(within(dialog()).getByText(T.noMatch).isConnected).toBe(true)
    expect(attr(combobox(), 'aria-expanded')).toBe('false')
    expect(within(dialog()).getByRole('status').textContent).toBe(T.resultCount(0))
  })

  it('hands a typed text to the global search only where the search is granted', async () => {
    const router = await mountShell([CAPABILITY.cariPartiesList])
    openWithKeyboard()
    await userEvent.type(combobox(), 'çelik')
    const search = within(dialog()).getAllByRole('option').find((option) => labelOf(option) === T.searchFor('çelik'))
    expect(search).toBeDefined()
    await userEvent.click(search as HTMLElement)
    expect(router.state.location.pathname).toBe('/arama')
    expect(new URLSearchParams(router.state.location.search).get('q')).toBe('çelik')
  })

  it('offers no search to an actor holding no searchable list', async () => {
    await mountShell([CAPABILITY.salesDraftsCreate])
    openWithKeyboard()
    await userEvent.type(combobox(), 'çelik')
    expect(optionLabels()).not.toContain(T.searchFor('çelik'))
  })

  it('never lists an ungranted screen or create command, whatever is typed or stored (leak test)', async () => {
    const granted: readonly string[] = [CAPABILITY.cariPartiesList, CAPABILITY.salesDraftsCreate]
    // A tampered tab storage naming screens this actor does not hold, under every plausible session epoch.
    for (const epoch of [0, 1, 2]) {
      window.sessionStorage.setItem(
        recentKey(epoch, USER, COMPANY) ?? '',
        JSON.stringify([`screen:${CAPABILITY.identityUsersManage}`, `create:${CAPABILITY.productItemsCreate}`, `screen:${CAPABILITY.cariPartiesList}`]),
      )
    }
    await mountShell(granted)
    const allowed = new Set([CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList], PALETTE_CREATE_LABEL[CAPABILITY.salesDraftsCreate]])
    const denied = [
      ...Object.values(CAPABILITY)
        .filter((capability) => !granted.includes(capability))
        .map((capability) => CAPABILITY_NAV_LABEL[capability])
        .filter((label) => label !== '' && !allowed.has(label)),
      ...PALETTE_CREATE_CAPABILITIES.filter((capability) => capability !== CAPABILITY.salesDraftsCreate).map(
        (capability) => PALETTE_CREATE_LABEL[capability],
      ),
    ]
    expect(denied.length).toBeGreaterThan(50)
    openWithKeyboard()
    const shownAtOpen = optionLabels()
    expect(shownAtOpen.length).toBeGreaterThan(0)
    for (const label of shownAtOpen) expect(allowed.has(label), label).toBe(true)
    // Typing a denied screen's own name surfaces nothing but the search hand-off of that very text.
    for (const label of denied) {
      fireEvent.change(combobox(), { target: { value: label } })
      for (const shown of optionLabels()) {
        expect(allowed.has(shown) || shown === T.searchFor(label.trim()), `${label} → ${shown}`).toBe(true)
      }
    }
    fireEvent.change(combobox(), { target: { value: '' } })
    const text = dialog().textContent ?? ''
    for (const label of denied) {
      if (![...allowed].some((ok) => ok.includes(label))) expect(text.includes(label), label).toBe(false)
    }
  })

  it('says so when the context grants no screen', async () => {
    await mountShell([])
    openWithKeyboard()
    expect(optionLabels()).toEqual([])
    expect(within(dialog()).getByText(T.noCommands).isConnected).toBe(true)
  })

  it('remembers the last screens opened in this tab, newest first, and shows them before the rest', async () => {
    const router = await mountShell([CAPABILITY.cariPartiesList, CAPABILITY.productItemsList, PRICE_LIST_CAPABILITY.list])
    await act(() => router.navigate(CAPABILITY_ROUTE[CAPABILITY.productItemsList]))
    await act(() => router.navigate(CAPABILITY_ROUTE[CAPABILITY.cariPartiesList]))
    // A record page or an ungranted path is not a remembered screen.
    await act(() => router.navigate('/cari/aaaaaaaa-0000-0000-0000-0000000000ff'))
    await act(() => router.navigate(CAPABILITY_ROUTE[CAPABILITY.identityUsersManage]))
    openWithKeyboard()
    const recentGroup = within(dialog()).getByRole('group', { name: T.groupRecent })
    expect(within(recentGroup).getAllByRole('option').map(labelOf)).toEqual([
      CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList],
      CAPABILITY_NAV_LABEL[CAPABILITY.productItemsList],
    ])
    // Typing hides the recent list; the screens stay findable.
    await userEvent.type(combobox(), 'urun')
    expect(within(dialog()).queryByRole('group', { name: T.groupRecent })).toBeNull()
    expect(optionLabels()[0]).toBe(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsList])
  })

  it('stays silent and empty when the browser refuses storage', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError (SENTETIK)')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('SecurityError (SENTETIK)')
    })
    const router = await mountShell([CAPABILITY.cariPartiesList])
    await act(() => router.navigate(CAPABILITY_ROUTE[CAPABILITY.cariPartiesList]))
    openWithKeyboard()
    expect(within(dialog()).queryByRole('group', { name: T.groupRecent })).toBeNull()
    expect(optionLabels()).toEqual([CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList]])
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('warns offline that the opened screen may not load', async () => {
    await mountShell([CAPABILITY.cariPartiesList])
    vi.stubGlobal('navigator', { onLine: false })
    act(() => {
      window.dispatchEvent(new Event('offline'))
    })
    openWithKeyboard()
    expect(within(dialog()).getByText(T.offline).isConnected).toBe(true)
  })

  it('runs no command: choosing an option sends no request at all', async () => {
    await mountShell([CAPABILITY.cariPartiesList, CAPABILITY.salesDraftsCreate])
    const before = requested.length
    openWithKeyboard()
    await userEvent.type(combobox(), 'yeni')
    fireEvent.keyDown(combobox(), { key: 'Enter' })
    expect(screen.getByTestId('page').textContent).toBe(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate])
    expect(requested.slice(before)).toEqual([])
  })

  it('makes the page behind it inert while open', async () => {
    await mountShell([CAPABILITY.cariPartiesList])
    openWithKeyboard()
    expect(document.getElementById('main-content')?.closest('[inert]')).not.toBeNull()
    expect(document.getElementById('shell-sidebar')?.hasAttribute('inert')).toBe(true)
    fireEvent.keyDown(combobox(), { key: 'Escape' })
    expect(document.getElementById('main-content')?.closest('[inert]')).toBeNull()
  })
})

describe('W1-B command palette without a resolved snapshot', () => {
  it('offers nothing and says the capabilities are being verified (fail-closed)', () => {
    const storage: StorageAccessor = () => ({ getItem: () => JSON.stringify([`screen:${CAPABILITY.cariPartiesList}`]), setItem: () => {} })
    const state = { ...initialShellState, phase: 'idle' as const, activeContext: fixtureContext(COMPANY, null, Object.values(CAPABILITY)) }
    render(
      <ShellStateContext.Provider value={state}>
        <MemoryRouter>
          <CommandPalette storage={storage} onClose={() => {}} onNavigate={() => {}} />
        </MemoryRouter>
      </ShellStateContext.Provider>,
    )
    expect(optionLabels()).toEqual([])
    expect(within(dialog()).getAllByText(T.capabilitiesPending).length).toBeGreaterThan(0)
  })
})

describe('W1-B palette stylesheet', () => {
  const nodeProcess = (globalThis as unknown as {
    readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
  }).process
  const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
  const css = nodeFs.readFileSync(`${nodeProcess.cwd()}/src/shell/commandPalette/CommandPalette.module.css`, 'utf8')

  it('animates only without prefers-reduced-motion', () => {
    const outside = css.replace(/@media \(prefers-reduced-motion: no-preference\)\s*\{[\s\S]*?\n\}/, '')
    expect(css).toContain('@media (prefers-reduced-motion: no-preference)')
    expect(outside).not.toMatch(/animation\s*:/)
    expect(outside).not.toMatch(/transition\s*:/)
  })
})
