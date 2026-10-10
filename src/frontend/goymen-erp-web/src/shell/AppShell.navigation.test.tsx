import { afterEach, describe, expect, it, vi } from 'vitest'
import { useEffect } from 'react'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { useShellDispatch } from './shellContext'
import { AppShell } from './AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'
import {
  CAPABILITY_NAV_LABEL,
  NAV_CAPABILITIES,
  NAV_GROUPS,
  NAV_HOME,
  NAV_TEXT,
  type NavEntryId,
} from './navigation'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE } from '../product/priceLists/capabilities'
import { PRICE_LIST_TEXT } from '../product/priceLists/priceListText'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const USER = '11111111-1111-1111-1111-111111111111'
const NAV_NAME = 'Modül navigasyonu'
const ACTIVE_HINT = NAV_TEXT.activeGroupHint

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

function installFetch(granted: readonly string[]) {
  vi.stubGlobal('navigator', { onLine: true })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url.includes('/identity/me')) {
      return json({
        userId: USER,
        displayName: 'Kullanici A',
        email: null,
        allowedCompanyIds: [COMPANY],
      })
    }
    if (url.includes('/organization/companies')) {
      return json([{ id: COMPANY, code: 'GDC', legalName: 'Goymen A', isPlaceholder: false }])
    }
    if (url.includes('/shell/capabilities')) {
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: 'gen-navigation',
        capabilities: granted,
      })
    }
    throw new Error(`unexpected fetch ${url}`)
  }))
}

/** A phone (`true`) or a desktop (`false`) as far as the shell's media query is concerned. */
function stubViewport(compact: boolean) {
  const viewport = { compact }
  const listeners = new Set<() => void>()
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
    get matches() {
      return viewport.compact
    },
    media: query,
    onchange: null,
    addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })))
  return {
    /** Turns the window into a phone or a desktop, as a rotation or a resize would. */
    resize(next: boolean) {
      viewport.compact = next
      act(() => {
        for (const listener of [...listeners]) listener()
      })
    },
  }
}

async function mountShell(path: string, granted: readonly string[]) {
  installFetch(granted)
  const router = createMemoryRouter([
    {
      path: '/',
      element: <AppShell />,
      children: [
        { path: CAPABILITY_ROUTE[CAPABILITY.salesDraftsList].slice(1), element: <h1>Siparis listesi</h1> },
        { path: CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate].slice(1), element: <h1>Yeni siparis</h1> },
        { path: PRICE_LIST_ROUTE.list.slice(1), element: <h1>{PRICE_LIST_TEXT.listHeading}</h1> },
        { path: 'sonda', element: <DispatchProbe /> },
        // Any other page: the sidebar is under test, not the screens it opens.
        { path: '*', element: <h1>Diger sayfa</h1> },
      ],
    },
  ], { initialEntries: [path] })
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  await screen.findByText('Kullanici A', undefined, { timeout: 5000 })
  return router
}

async function renderShell(path: string, granted: readonly string[]) {
  await mountShell(path, granted)
  return screen.getByRole('navigation', { name: NAV_NAME })
}

/** A group's header button. Its name carries the active-page hint when the open page is in it. */
const groupButton = (scope: HTMLElement, label: string) =>
  within(scope).getByRole('button', { name: new RegExp(`^${label}(${ACTIVE_HINT})?$`) })

const labelOf = (entry: NavEntryId) =>
  entry === PRICE_LIST_CAPABILITY.list ? PRICE_LIST_TEXT.listHeading : CAPABILITY_NAV_LABEL[entry]

const ALL_GRANTED = [...Object.values(CAPABILITY), PRICE_LIST_CAPABILITY.list]

// The stylesheet as written: jsdom applies no CSS, so a rule the browser depends on is read as source
// (the `src/logistics/capabilities.test.ts` precedent). Vitest runs from the web package.
const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const shellCss = nodeFs.readFileSync(`${nodeProcess.cwd()}/src/shell/AppShell.module.css`, 'utf8')

/** The shell's dispatch, captured by a page so a test can raise a gate (a 401) while the drawer is open. */
const probe: { dispatch: ReturnType<typeof useShellDispatch> | null } = { dispatch: null }
function DispatchProbe() {
  const dispatch = useShellDispatch()
  useEffect(() => {
    probe.dispatch = dispatch
  }, [dispatch])
  return <h1>Sonda</h1>
}

afterEach(() => {
  cleanup()
  try {
    window.localStorage?.clear()
  } catch {
    // The Node test host may not expose storage; the shell does not require it for these cases.
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AppShell navigation reachability and active leaf', () => {
  it('keeps Sales create reachable but out of the sidebar, with the list leaf inactive', async () => {
    const navigation = await renderShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate], [
      CAPABILITY.salesDraftsList,
      CAPABILITY.salesDraftsCreate,
    ])

    const list = within(navigation).getByRole('link', {
      name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList],
    })
    expect(list.getAttribute('aria-current')).toBeNull()
    expect(screen.getByRole('heading', { level: 1, name: 'Yeni siparis' })).toBeTruthy()
    expect(
      within(navigation).queryAllByRole('link')
        .some((link) => link.getAttribute('href') === CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate]),
    ).toBe(false)
    expect(
      within(navigation).getAllByRole('link')
        .filter((link) => link.getAttribute('aria-current') === 'page'),
    ).toEqual([])
  })

  it('publishes the existing price-list owner capability as one exact, authorized menu path', async () => {
    const navigation = await renderShell(PRICE_LIST_ROUTE.list, [PRICE_LIST_CAPABILITY.list])

    const link = within(navigation).getByRole('link', { name: PRICE_LIST_TEXT.listHeading })
    expect(link.getAttribute('href')).toBe(PRICE_LIST_ROUTE.list)
    expect(link.getAttribute('aria-current')).toBe('page')
  })

  it('does not disclose the price-list menu path without its own capability', async () => {
    const navigation = await renderShell('/', [])
    expect(within(navigation).queryByRole('link', { name: PRICE_LIST_TEXT.listHeading })).toBeNull()
  })

  it('draws the home link, then every group in order with exactly its entries, on the home page', async () => {
    // Every id the shell knows plus the price list's own: only the definitions may decide what shows.
    const navigation = await renderShell('/', ALL_GRANTED)

    expect(within(navigation).getAllByRole('button').map((button) => button.textContent))
      .toEqual(NAV_GROUPS.map((group) => group.label))
    const drawn = within(navigation).getAllByRole('link')
      .map((link) => [link.textContent, link.getAttribute('href')])
    expect(drawn).toEqual([
      [NAV_HOME.label, NAV_HOME.path],
      ...NAV_GROUPS.flatMap((group) => group.entries.map((entry) => [
        labelOf(entry),
        entry === PRICE_LIST_CAPABILITY.list ? PRICE_LIST_ROUTE.list : CAPABILITY_ROUTE[entry],
      ])),
    ])
    // The home link is the open page here, and it is the only one.
    expect(within(navigation).getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page')
      .map((link) => link.textContent)).toEqual([NAV_HOME.label])
  })
})

describe('navigation groups are headings, never permissions', () => {
  it('place every sidebar capability in exactly one group, and nothing else but the price list', () => {
    const placed = NAV_GROUPS.flatMap((group) => group.entries)
    expect(placed.filter((entry) => entry !== PRICE_LIST_CAPABILITY.list).toSorted())
      .toEqual([...NAV_CAPABILITIES].toSorted())
    expect(new Set(placed).size).toBe(placed.length)
    expect(placed.filter((entry) => entry === PRICE_LIST_CAPABILITY.list)).toEqual([PRICE_LIST_CAPABILITY.list])
    // The price list sits under Satış without needing any Sales capability (#590).
    expect(NAV_GROUPS.find((group) => group.entries.includes(PRICE_LIST_CAPABILITY.list))?.label).toBe('Satış')
  })

  it('use the approved work-area order, with no Raporlar or Belgeler group while nothing is published there', () => {
    expect(NAV_GROUPS.map((group) => group.label)).toEqual([
      'Satış', 'Satın alma', 'Ürün ve stok', 'Üretim', 'Sevkiyat', 'Cari', 'Finans', 'Muhasebe', 'Ayarlar',
    ])
    expect(NAV_HOME).toEqual({ label: 'Ana sayfa', path: '/' })
  })

  it('draw a group only around granted entries, and only those entries', async () => {
    const navigation = await renderShell('/', [CAPABILITY.cariPartiesList, CAPABILITY.purchasingOrdersList])

    expect(within(navigation).getAllByRole('button').map((button) => button.textContent)).toEqual(['Satın alma', 'Cari'])
    expect(within(navigation).getAllByRole('link').map((link) => link.textContent)).toEqual([
      NAV_HOME.label,
      CAPABILITY_NAV_LABEL[CAPABILITY.purchasingOrdersList],
      CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList],
    ])
    // Ungranted siblings stay unnamed: the group is not a key to its other entries.
    expect(within(navigation).queryByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.cariAccountsLink] })).toBeNull()
    expect(within(navigation).queryByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.purchasingRequestsList] }))
      .toBeNull()
  })

  it('hides every group, but keeps the home link and the honest empty state, when nothing is granted', async () => {
    const navigation = await renderShell('/', [])

    expect(within(navigation).queryAllByRole('button')).toEqual([])
    expect(within(navigation).getAllByRole('link').map((link) => link.textContent)).toEqual([NAV_HOME.label])
    expect(within(navigation).getByText(/henüz uygulanmış modül yok/i)).toBeTruthy()
  })

  it('gives the price list its own Satış group when no Sales capability is held', async () => {
    const navigation = await renderShell(PRICE_LIST_ROUTE.list, [PRICE_LIST_CAPABILITY.list])

    const sales = groupButton(navigation, 'Satış')
    expect(sales.getAttribute('aria-expanded')).toBe('true')
    expect(within(navigation).getAllByRole('link').map((link) => link.textContent))
      .toEqual([NAV_HOME.label, PRICE_LIST_TEXT.listHeading])
  })
})

describe('desktop groups open and close (UX v1.2)', () => {
  const GRANTS = [CAPABILITY.salesDraftsList, CAPABILITY.cariPartiesList, CAPABILITY.cariAccountsLink]

  it('opens only the open page’s group, marks it, and keeps exactly one active leaf', async () => {
    const navigation = await renderShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList], GRANTS)

    const sales = within(navigation).getByRole('button', { name: `Satış${ACTIVE_HINT}` })
    expect(sales.getAttribute('aria-expanded')).toBe('true')
    expect(groupButton(navigation, 'Cari').getAttribute('aria-expanded')).toBe('false')
    // A closed group's entries are out of the tree, not merely out of sight.
    expect(within(navigation).queryByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList] })).toBeNull()
    expect(within(navigation).getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page')
      .map((link) => link.textContent)).toEqual([CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList]])
    // The header is a heading, not a link: it never carries aria-current.
    expect(sales.getAttribute('aria-current')).toBeNull()
  })

  it('opens the group of a page below an entry, with no active leaf there (#590)', async () => {
    const navigation = await renderShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate], [
      ...GRANTS,
      CAPABILITY.salesDraftsCreate,
    ])

    expect(within(navigation).getByRole('button', { name: `Satış${ACTIVE_HINT}` }).getAttribute('aria-expanded'))
      .toBe('true')
    expect(within(navigation).queryAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page'))
      .toEqual([])
  })

  it('opens the group of a record page below its list entry', async () => {
    const navigation = await renderShell('/cari/7b1c0f2e-0000-4a00-8000-000000000003', GRANTS)

    expect(within(navigation).getByRole('button', { name: `Cari${ACTIVE_HINT}` }).getAttribute('aria-expanded'))
      .toBe('true')
    expect(groupButton(navigation, 'Satış').getAttribute('aria-expanded')).toBe('false')
  })

  it('opens a work area by its first path segment only when exactly one group owns it', async () => {
    // `/uretim/planlar` lies under no entry; only Üretim has an entry under `/uretim`.
    const manufacturing = await renderShell('/uretim/planlar', [...GRANTS, CAPABILITY.manufacturingOrdersList])
    expect(within(manufacturing).getByRole('button', { name: `Üretim${ACTIVE_HINT}` }).getAttribute('aria-expanded'))
      .toBe('true')
    expect(groupButton(manufacturing, 'Satış').getAttribute('aria-expanded')).toBe('false')
    cleanup()

    // `/lojistik/…` is shared by Satın alma and Sevkiyat: no guess, so no work area is claimed.
    const shared = await renderShell('/lojistik/bilinmeyen', [
      CAPABILITY.logisticsOperationsPurchase,
      CAPABILITY.logisticsOperationsSales,
    ])
    expect(within(shared).queryAllByRole('button', { name: new RegExp(ACTIVE_HINT) })).toEqual([])
  })

  it('opening another group closes the open one, and a header click closes its own group', async () => {
    const user = userEvent.setup()
    const navigation = await renderShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList], GRANTS)

    await user.click(groupButton(navigation, 'Cari'))
    expect(groupButton(navigation, 'Cari').getAttribute('aria-expanded')).toBe('true')
    expect(groupButton(navigation, 'Satış').getAttribute('aria-expanded')).toBe('false')
    expect(within(navigation).getByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.cariAccountsLink] })).toBeTruthy()
    expect(within(navigation).queryByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] })).toBeNull()

    await user.click(groupButton(navigation, 'Cari'))
    expect(groupButton(navigation, 'Cari').getAttribute('aria-expanded')).toBe('false')
    expect(within(navigation).queryByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList] })).toBeNull()
  })

  it('forgets a header choice when the page changes, so Back finds its own group open again', async () => {
    const user = userEvent.setup()
    const router = await mountShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList], GRANTS)
    const navigation = screen.getByRole('navigation', { name: NAV_NAME })

    await user.click(groupButton(navigation, 'Cari'))
    await user.click(within(navigation).getByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList] }))
    expect(router.state.location.pathname).toBe(CAPABILITY_ROUTE[CAPABILITY.cariPartiesList])
    await act(() => router.navigate(-1))

    expect(router.state.location.pathname).toBe(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])
    expect(within(navigation).getByRole('button', { name: `Satış${ACTIVE_HINT}` }).getAttribute('aria-expanded'))
      .toBe('true')
    expect(groupButton(navigation, 'Cari').getAttribute('aria-expanded')).toBe('false')
    expect(within(navigation).getByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] })
      .getAttribute('aria-current')).toBe('page')
  })

  it('keeps every group open on the home page, where no work area is open', async () => {
    const navigation = await renderShell('/', GRANTS)

    expect(within(navigation).getAllByRole('button').map((button) => button.getAttribute('aria-expanded')))
      .toEqual(['true', 'true'])
    // No group claims the home page.
    expect(within(navigation).queryAllByRole('button', { name: new RegExp(ACTIVE_HINT) })).toEqual([])
  })

  it('closing one open header on the home page leaves the other groups open', async () => {
    const user = userEvent.setup()
    const navigation = await renderShell('/', GRANTS)

    await user.click(groupButton(navigation, 'Satış'))
    expect(groupButton(navigation, 'Satış').getAttribute('aria-expanded')).toBe('false')
    expect(groupButton(navigation, 'Cari').getAttribute('aria-expanded')).toBe('true')
  })

  it('is no keyboard trap on a desktop: Tab leaves the sidebar after its last control', async () => {
    const user = userEvent.setup()
    const navigation = await renderShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList], GRANTS)

    groupButton(navigation, 'Cari').focus()
    await user.tab()
    expect(navigation.contains(document.activeElement)).toBe(false)
    expect(document.activeElement).toBe(screen.getByRole('combobox', { name: 'Şirket' }))
  })

  it('hands focus to "Menü" when a narrowing window hides the sidebar it was in', async () => {
    const viewport = stubViewport(false)
    const navigation = await renderShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList], GRANTS)
    within(navigation).getByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] }).focus()

    viewport.resize(true)

    expect(screen.queryByRole('navigation', { name: NAV_NAME })).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: NAV_TEXT.menuButton }))
  })

  it('pins the rule that lets `hidden` hide the sidebar and closed groups in a browser', () => {
    // jsdom applies no CSS, so the tests above prove only the accessibility tree. In a browser the
    // sidebar's own `display: flex` beats the user agent's `[hidden]` rule unless this rule exists.
    expect(shellCss).toMatch(/\.sidebar\[hidden\],\s*\.groupList\[hidden\]\s*\{\s*display:\s*none;/)
  })

  it('names each header’s list and is the only navigation landmark on a desktop', async () => {
    const navigation = await renderShell('/', GRANTS)

    for (const button of within(navigation).getAllByRole('button')) {
      const list = document.getElementById(button.getAttribute('aria-controls') ?? '')
      expect(list?.tagName).toBe('UL')
      expect(navigation.contains(list)).toBe(true)
    }
    expect(screen.getAllByRole('navigation')).toEqual([navigation])
    expect(screen.queryByRole('button', { name: NAV_TEXT.menuButton })).toBeNull()
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})

describe('phone menu drawer (UX v1.2)', () => {
  const GRANTS = [CAPABILITY.salesDraftsList, CAPABILITY.cariPartiesList, PRICE_LIST_CAPABILITY.list]

  async function renderPhone(path: string, granted: readonly string[] = GRANTS) {
    const viewport = stubViewport(true)
    const router = await mountShell(path, granted)
    return { router, viewport, menu: screen.getByRole('button', { name: NAV_TEXT.menuButton }) }
  }

  const drawer = () => screen.getByRole('dialog', { name: NAV_TEXT.drawer })
  const shellBody = () => screen.getByRole('main').parentElement as HTMLElement

  it('keeps the closed menu out of the accessibility tree: no navigation landmark, no entries', async () => {
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    expect(screen.queryByRole('navigation', { name: NAV_NAME })).toBeNull()
    expect(screen.queryByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] })).toBeNull()
    expect(menu.getAttribute('aria-expanded')).toBe('false')
    expect(menu.getAttribute('aria-controls')).toBe('shell-sidebar')
    expect(screen.getByRole('heading', { level: 1, name: 'Siparis listesi' })).toBeTruthy()
    expect(shellBody().hasAttribute('inert')).toBe(false)
  })

  it('opens as a modal dialog holding the one navigation, focused on the open page, with the page inert', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)

    const dialog = drawer()
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(menu.getAttribute('aria-expanded')).toBe('true')
    const navigation = within(dialog).getByRole('navigation', { name: NAV_NAME })
    expect(screen.getAllByRole('navigation')).toEqual([navigation])
    const current = within(navigation).getByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] })
    await waitFor(() => expect(document.activeElement).toBe(current))
    expect(shellBody().hasAttribute('inert')).toBe(true)
    expect(screen.getByRole('link', { name: 'Ana içeriğe geç', hidden: true }).hasAttribute('inert')).toBe(true)
    // UX v1.2: on a phone the signed-in name is at the foot of the drawer, not in the toolbar.
    expect(within(dialog).getByText('Kullanici A')).toBeTruthy()
  })

  it('focuses its close button when the open page has no entry of its own', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate], [
      ...GRANTS,
      CAPABILITY.salesDraftsCreate,
    ])

    await user.click(menu)
    await waitFor(() =>
      expect(document.activeElement).toBe(within(drawer()).getByRole('button', { name: NAV_TEXT.closeDrawer })))
  })

  it('closes on Escape and on its close button, returning focus to "Menü" each time', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    await waitFor(() => expect(drawer().contains(document.activeElement)).toBe(true))
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(menu))
    expect(menu.getAttribute('aria-expanded')).toBe('false')
    expect(shellBody().hasAttribute('inert')).toBe(false)

    await user.click(menu)
    await user.click(within(drawer()).getByRole('button', { name: NAV_TEXT.closeDrawer }))
    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(menu))
  })

  it('closes when the backdrop is clicked', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    const backdrop = drawer().nextElementSibling as HTMLElement
    expect(backdrop.getAttribute('aria-hidden')).toBe('true')
    await user.click(backdrop)
    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(menu))
  })

  it('keeps Tab and Shift+Tab inside the drawer', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    const dialog = drawer()
    const close = within(dialog).getByRole('button', { name: NAV_TEXT.closeDrawer })
    // In document order: the close button first, the last group header last (Cari, closed here).
    const reachable = [...within(dialog).getAllByRole('button'), ...within(dialog).getAllByRole('link')]
      .toSorted((a, b) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1))
    expect(reachable[0]).toBe(close)
    const last = reachable[reachable.length - 1]
    expect(last).toBe(groupButton(dialog, 'Cari'))

    last.focus()
    await user.tab()
    expect(document.activeElement).toBe(close)
    await user.tab({ shift: true })
    expect(document.activeElement).toBe(last)
  })

  it('closes after an entry is chosen and moves focus to the new page’s content', async () => {
    const user = userEvent.setup()
    const { router, menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    await user.click(within(drawer()).getByRole('link', { name: PRICE_LIST_TEXT.listHeading }))

    expect(router.state.location.pathname).toBe(PRICE_LIST_ROUTE.list)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getByRole('heading', { level: 1, name: PRICE_LIST_TEXT.listHeading })).toBeTruthy()
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('main')))
    expect(shellBody().hasAttribute('inert')).toBe(false)
  })

  it('is no keyboard trap while closed: Tab moves on from "Menü" into the page', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    menu.focus()
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('combobox', { name: 'Şirket' }))
  })

  it('locks the page’s scroll only while open, keeping its scrollbar gutter', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    expect(document.documentElement.style.overflow).toBe('hidden')
    expect(document.documentElement.style.scrollbarGutter).toBe('stable')
    await user.keyboard('{Escape}')
    expect(document.documentElement.style.overflow).toBe('')
    expect(document.documentElement.style.scrollbarGutter).toBe('')
  })

  it('closes when the page changes underneath it (Back, a redirect)', async () => {
    const user = userEvent.setup()
    const { router, menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    await act(() => router.navigate(PRICE_LIST_ROUTE.list))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(shellBody().hasAttribute('inert')).toBe(false)
    expect(document.documentElement.style.overflow).toBe('')
  })

  it('closes, and releases the page, when a gate (a 401) replaces the shell', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone('/sonda')

    await user.click(menu)
    expect(document.documentElement.style.overflow).toBe('hidden')
    act(() => probe.dispatch?.({ type: 'SESSION_INVALIDATED' }))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByRole('navigation', { name: NAV_NAME })).toBeNull()
    expect(document.documentElement.style.overflow).toBe('')
  })

  it('reopens on the open page’s entry even after another group was opened in it', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    await user.click(groupButton(drawer(), 'Cari'))
    expect(within(drawer()).queryByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] })).toBeNull()
    await user.click(within(drawer()).getByRole('button', { name: NAV_TEXT.closeDrawer }))
    await waitFor(() => expect(document.activeElement).toBe(menu))

    await user.click(menu)
    const current = within(drawer()).getByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] })
    await waitFor(() => expect(document.activeElement).toBe(current))
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('answers Escape and Tab even when focus has fallen out of the drawer', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    const close = within(drawer()).getByRole('button', { name: NAV_TEXT.closeDrawer })
    ;(document.activeElement as HTMLElement).blur()
    expect(document.activeElement).toBe(document.body)
    await user.tab()
    expect(document.activeElement).toBe(close)

    ;(document.activeElement as HTMLElement).blur()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(menu))
  })

  it('becomes the sidebar again when the window widens, without dropping focus', async () => {
    const user = userEvent.setup()
    const { viewport, menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate], [
      ...GRANTS,
      CAPABILITY.salesDraftsCreate,
    ])

    await user.click(menu)
    await waitFor(() =>
      expect(document.activeElement).toBe(within(drawer()).getByRole('button', { name: NAV_TEXT.closeDrawer })))
    viewport.resize(false)

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getByRole('navigation', { name: NAV_NAME })).toBeTruthy()
    expect(screen.queryByRole('button', { name: NAV_TEXT.menuButton })).toBeNull()
    // The create page has no entry of its own, so focus goes to its content rather than to nowhere.
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('main')))
    expect(shellBody().hasAttribute('inert')).toBe(false)
    expect(document.documentElement.style.overflow).toBe('')
  })

  it('keeps focus on the open page’s entry when the window widens with focus there', async () => {
    const user = userEvent.setup()
    const { viewport, menu } = await renderPhone(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList])

    await user.click(menu)
    const current = within(drawer()).getByRole('link', { name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList] })
    await waitFor(() => expect(document.activeElement).toBe(current))
    viewport.resize(false)

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(current)
  })

  it('shows only granted entries and groups in the drawer, as on a desktop', async () => {
    const user = userEvent.setup()
    const { menu } = await renderPhone('/', [CAPABILITY.cariPartiesList])

    await user.click(menu)
    const navigation = within(drawer()).getByRole('navigation', { name: NAV_NAME })
    expect(within(navigation).getAllByRole('button').map((button) => button.textContent)).toEqual(['Cari'])
    expect(within(navigation).getAllByRole('link').map((link) => link.textContent))
      .toEqual([NAV_HOME.label, CAPABILITY_NAV_LABEL[CAPABILITY.cariPartiesList]])
    expect(within(navigation).queryByRole('link', { name: PRICE_LIST_TEXT.listHeading })).toBeNull()
  })
})

/**
 * NAV-OPENING-01 (coordinator allocation 2026-10-10): the opening stock count is first set-up and data transfer, not
 * daily stock work, so its one sidebar entry moved from Ürün ve stok to Ayarlar as "Açılış stokları". The route
 * (`/stok/acilis` and its children), its capability and every screen stay as they were.
 */
describe('NAV-OPENING-01: Açılış stokları under Ayarlar', () => {
  const OPENING = CAPABILITY.inventoryOpeningStockRecord
  const OPENING_PATH = CAPABILITY_ROUTE[OPENING]
  const OPENING_LABEL = 'Açılış stokları'

  it('has exactly one placement, in Ayarlar, after the settings entries; none left in Ürün ve stok', () => {
    expect(OPENING_PATH).toBe('/stok/acilis')
    expect(CAPABILITY_NAV_LABEL[OPENING]).toBe(OPENING_LABEL)
    expect(NAV_CAPABILITIES.filter((entry) => entry === OPENING)).toHaveLength(1)
    const owners = NAV_GROUPS.filter((group) => group.entries.includes(OPENING))
    expect(owners.map((group) => group.label)).toEqual(['Ayarlar'])
    expect(owners[0].entries.at(-1)).toBe(OPENING)
    expect(NAV_GROUPS.find((group) => group.id === 'product-stock')?.entries)
      .toEqual([CAPABILITY.productItemsList, CAPABILITY.inventoryStockPositionsList])
  })

  it('draws Ayarlar with only this entry for an actor holding just the opening-stock capability (no admin gate)', async () => {
    const navigation = await renderShell('/', [OPENING])

    expect(within(navigation).getAllByRole('button').map((button) => button.textContent)).toEqual(['Ayarlar'])
    expect(within(navigation).getAllByRole('link').map((link) => [link.textContent, link.getAttribute('href')]))
      .toEqual([[NAV_HOME.label, NAV_HOME.path], [OPENING_LABEL, OPENING_PATH]])
    expect(within(navigation).queryByRole('button', { name: /^Ürün ve stok/ })).toBeNull()
  })

  it('is absent without its capability, even with every other Ayarlar and Ürün ve stok entry', async () => {
    const navigation = await renderShell('/', [
      CAPABILITY.organizationSitesList, CAPABILITY.organizationWorkCentersList, CAPABILITY.identityAssignmentsList,
      CAPABILITY.productItemsList, CAPABILITY.inventoryStockPositionsList,
    ])

    expect(within(navigation).queryByRole('link', { name: OPENING_LABEL })).toBeNull()
    expect(within(navigation).queryByRole('link', { name: 'Stok Açılışı' })).toBeNull()
    expect(within(navigation).getAllByRole('button').map((button) => button.textContent)).toEqual(['Ürün ve stok', 'Ayarlar'])
  })

  it('opens Ayarlar with the exact active leaf on /stok/acilis, and Ayarlar without an active leaf below it', async () => {
    const granted = [OPENING, CAPABILITY.inventoryStockPositionsList, CAPABILITY.organizationSitesList]
    const home = await renderShell(OPENING_PATH, granted)
    expect(within(home).getByRole('button', { name: `Ayarlar${ACTIVE_HINT}` }).getAttribute('aria-expanded')).toBe('true')
    expect(groupButton(home, 'Ürün ve stok').getAttribute('aria-expanded')).toBe('false')
    expect(within(home).getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page')
      .map((link) => [link.textContent, link.getAttribute('href')])).toEqual([[OPENING_LABEL, OPENING_PATH]])
    cleanup()

    // A child page (the new count, a count detail) belongs to Ayarlar's entry; no leaf claims to be that page (#590).
    for (const child of ['/stok/acilis/yeni', '/stok/acilis/COUNT-1']) {
      const below = await renderShell(child, granted)
      expect(within(below).getByRole('button', { name: `Ayarlar${ACTIVE_HINT}` }).getAttribute('aria-expanded')).toBe('true')
      expect(within(below).getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page')).toEqual([])
      cleanup()
    }

    // The stock positions keep their own group and active leaf.
    const positions = await renderShell(CAPABILITY_ROUTE[CAPABILITY.inventoryStockPositionsList], granted)
    expect(within(positions).getByRole('button', { name: `Ürün ve stok${ACTIVE_HINT}` }).getAttribute('aria-expanded')).toBe('true')
    expect(groupButton(positions, 'Ayarlar').getAttribute('aria-expanded')).toBe('false')
  })

  it('pins the existing first-segment rule now that Ayarlar and Ürün ve stok both own /stok', async () => {
    // A label card (`/stok/etiket/…`) lies under no entry. With both groups drawn, the rule claims no work area
    // (no guess, as for `/lojistik/…`); with only the stock positions drawn, Ürün ve stok still opens.
    const both = await renderShell('/stok/etiket/LBL-1', [OPENING, CAPABILITY.inventoryStockPositionsList])
    expect(within(both).queryAllByRole('button', { name: new RegExp(ACTIVE_HINT) })).toEqual([])
    cleanup()
    const positionsOnly = await renderShell('/stok/etiket/LBL-1', [CAPABILITY.inventoryStockPositionsList])
    expect(within(positionsOnly).getByRole('button', { name: `Ürün ve stok${ACTIVE_HINT}` }).getAttribute('aria-expanded'))
      .toBe('true')
  })

  it('is reachable from the phone menu under Ayarlar, and choosing it closes the drawer', async () => {
    stubViewport(true)
    const router = await mountShell('/', [OPENING, CAPABILITY.productItemsList])
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: NAV_TEXT.menuButton }))
    const drawerNav = within(screen.getByRole('dialog', { name: NAV_TEXT.drawer })).getByRole('navigation', { name: NAV_NAME })
    expect(within(drawerNav).getAllByRole('button').map((button) => button.textContent)).toEqual(['Ürün ve stok', 'Ayarlar'])
    await user.click(within(drawerNav).getByRole('link', { name: OPENING_LABEL }))
    await waitFor(() => expect(router.state.location.pathname).toBe(OPENING_PATH))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: NAV_TEXT.drawer })).toBeNull())
  })
})
