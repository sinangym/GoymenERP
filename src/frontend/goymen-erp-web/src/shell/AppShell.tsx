/**
 * SHELL-PILOT-01 common shell: dark-navy nav, global toolbar with the company
 * selector, breadcrumb slot, main outlet.
 *
 * Honest scope: NO business feature is mounted (there are none yet). The nav
 * shows only what the server capability snapshot allows — currently nothing —
 * so it renders an explicit "no module available" state, never a mock menu
 * (DEC-GOV-007). Company switch is fenced by the reducer + orchestrator.
 *
 * P1-b (UX v1.2): the sidebar is grouped by work area (`NAV_GROUPS`) and stays in place on every
 * desktop screen. On a phone it is a drawer behind the toolbar's "Menü" button, so a page starts at
 * the top instead of under the whole menu. It is ONE sidebar either way: there is never a second,
 * hidden navigation for the keyboard or a screen reader to find.
 */

import { Fragment, useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router'
import { capabilityFailClosed, hasCapability, type ShellState } from './shellState'
import { useShellDispatch, useShellState } from './shellContext'
import { useShellOrchestrator } from './useShellOrchestrator'
import { StateBlock } from './StateBlock'
import { SwitchConfirmDialog } from './SwitchConfirmDialog'
import { GlobalSearchBox } from '../search/GlobalSearchBox'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE } from '../product/priceLists/capabilities'
import { PRICE_LIST_TEXT } from '../product/priceLists/priceListText'
import { CAPABILITY_ROUTE } from './capabilities'
import {
  CAPABILITY_NAV_LABEL,
  NAV_CAPABILITIES,
  NAV_GROUPS,
  NAV_HOME,
  NAV_PATH_OWNERS,
  NAV_TEXT,
  type NavEntryId,
  type NavGroupId,
} from './navigation'
import styles from './AppShell.module.css'

/** The nav order is defined in `./navigation`; re-exported for the tests that read it from the shell. */
export { NAV_CAPABILITIES }

/** Below this width the sidebar is a drawer: the width at which the shell already stopped being two columns. */
const COMPACT_VIEWPORT = '(max-width: 767px)'
const SIDEBAR_ID = 'shell-sidebar'

interface NavLeaf {
  readonly key: NavEntryId
  readonly path: string
  readonly label: string
  /** NAV-OPENING-01: the intermediate label drawn above this entry, when its group names one. */
  readonly subheading?: string | undefined
}

interface DrawnGroup {
  readonly id: NavGroupId
  readonly label: string
  readonly leaves: readonly NavLeaf[]
}

function leafOf(entry: NavEntryId): NavLeaf {
  // #590: the price list is drawn on its OWN capability, with its own route and heading.
  if (entry === PRICE_LIST_CAPABILITY.list) {
    return { key: entry, path: PRICE_LIST_ROUTE.list, label: PRICE_LIST_TEXT.listHeading }
  }
  return { key: entry, path: CAPABILITY_ROUTE[entry], label: CAPABILITY_NAV_LABEL[entry] }
}

/** The groups this actor sees: each with only its granted entries, and none left empty. */
function drawnGroups(state: ShellState): DrawnGroup[] {
  return NAV_GROUPS.map((group) => ({
    id: group.id,
    label: group.label,
    // A subheading travels with its granted entry, so it is never drawn on its own or for an ungranted one.
    leaves: group.entries.filter((entry) => hasCapability(state, entry))
      .map((entry) => ({ ...leafOf(entry), subheading: group.subheadings?.[entry] })),
  })).filter((group) => group.leaves.length > 0)
}

const firstSegment = (path: string) => path.split('/')[1] ?? ''

/**
 * The work area the open page belongs to: the group of the longest entry path the page is or lies
 * under (`/satis/siparisler/yeni` is Satış; `/stok/acilis/yeni` is the opening-stock entry's group). A page
 * under no entry first asks `NAV_PATH_OWNERS`: a declared prefix belongs to the group that draws its owner
 * ENTRY (a label card under `/stok` is Ürün ve stok's while the stock positions are granted) and to no group
 * when that entry is not drawn — never to another group that merely shares the segment. Any other page (a
 * plan) falls back to its first path segment, but only when exactly one group owns that segment.
 */
function activeGroupOf(groups: readonly DrawnGroup[], pathname: string): NavGroupId | null {
  let best: { readonly id: NavGroupId; readonly length: number } | null = null
  for (const group of groups) {
    for (const leaf of group.leaves) {
      const under = pathname === leaf.path || pathname.startsWith(`${leaf.path}/`)
      if (under && (best === null || leaf.path.length > best.length)) {
        best = { id: group.id, length: leaf.path.length }
      }
    }
  }
  if (best) return best.id
  const declared = NAV_PATH_OWNERS.find((owner) => pathname === owner.prefix || pathname.startsWith(`${owner.prefix}/`))
  if (declared) {
    return groups.find((group) => group.leaves.some((leaf) => leaf.key === declared.entry))?.id ?? null
  }
  const segment = firstSegment(pathname)
  if (!segment) return null
  const owners = groups.filter((group) => group.leaves.some((leaf) => firstSegment(leaf.path) === segment))
  return owners.length === 1 ? owners[0].id : null
}

function subscribeCompact(onChange: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {}
  const query = window.matchMedia(COMPACT_VIEWPORT)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}

/** A host without `matchMedia` (the jsdom test host) is a desktop. */
function readCompact(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia(COMPACT_VIEWPORT).matches
}

const FOCUSABLE = 'a[href], button:not([disabled])'

/** The sidebar's link to the open page, when it is in an open group (a closed group's links are hidden). */
function visibleCurrentEntry(sidebar: HTMLElement | null): HTMLElement | null {
  const current = sidebar?.querySelector<HTMLElement>('[aria-current="page"]') ?? null
  return current !== null && current.closest('[hidden]') === null ? current : null
}

/** UX v1.2 icons: decorative, so they are hidden from assistive technology. */
const ICON_PATHS: Record<NavGroupId | 'home' | 'menu' | 'close' | 'chevron', string> = {
  home: 'M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z',
  sales: 'M3 4h2l2.2 10.2a1 1 0 0 0 1 .8h8.6a1 1 0 0 0 1-.8L20 7H6.3M8.1 19a1.4 1.4 0 1 0 2.8 0a1.4 1.4 0 1 0-2.8 0M15.1 19a1.4 1.4 0 1 0 2.8 0a1.4 1.4 0 1 0-2.8 0',
  purchasing: 'M4 13h4l1.5 2.5h5L16 13h4M5.5 5h13L20 13v6H4v-6z',
  'product-stock': 'M3 7.5L12 3l9 4.5-9 4.5zM3 7.5v9L12 21l9-4.5v-9M12 12v9',
  manufacturing: 'M3 20V9l5 4V9l5 4V5h6v15zM2 20h20',
  logistics: 'M2 6h12v10H2zM14 10h4l3 3v3h-7M4.7 18a1.8 1.8 0 1 0 3.6 0a1.8 1.8 0 1 0-3.6 0M15.2 18a1.8 1.8 0 1 0 3.6 0a1.8 1.8 0 1 0-3.6 0',
  cari: 'M5.5 8a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0-7 0M2.5 20a6.5 6.5 0 0 1 13 0M16 4.6a3.5 3.5 0 0 1 0 6.8M18.5 14.5a5.5 5.5 0 0 1 3 5.5',
  finance: 'M4.5 6h15a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-15a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2zM9.4 12a2.6 2.6 0 1 0 5.2 0a2.6 2.6 0 1 0-5.2 0M6 9.5v5M18 9.5v5',
  accounting: 'M5 3.5h10.5L19 7v13.5H5zM15 3.5V7h4M8.5 11h7M8.5 14.5h7M8.5 18h4',
  settings: 'M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1M13 6a2 2 0 1 0 4 0a2 2 0 1 0-4 0M7 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0M15 18a2 2 0 1 0 4 0a2 2 0 1 0-4 0',
  menu: 'M4 6h16M4 12h16M4 18h16',
  close: 'M6 6l12 12M18 6L6 18',
  chevron: 'M9 6l6 6-6 6',
}

function Icon({ name, className }: { name: keyof typeof ICON_PATHS; className?: string }) {
  return (
    <svg
      className={className ? `${styles.icon} ${className}` : styles.icon}
      viewBox="0 0 24 24"
      aria-hidden="true"
      focusable="false"
    >
      <path d={ICON_PATHS[name]} />
    </svg>
  )
}

export function AppShell() {
  const state = useShellState()
  const dispatch = useShellDispatch()
  const {
    identity,
    directory,
    identityState,
    directoryState,
    bootstrapping,
    bootstrapFailure,
    switchFailure,
    sessionInvalid,
    actorMismatch,
    retry,
  } = useShellOrchestrator()
  // The states in which no shell is drawn, in order. Computed before the hooks below so the drawer
  // can never stay "open" (focus, inert page, scroll lock) behind one of them.
  const gate = (() => {
    // 401 anywhere — session is dead. Terminal (no login flow yet).
    if (sessionInvalid || identityState.kind === 'unauthenticated') {
      return <StateBlock state={{ kind: 'unauthenticated' }} subject="oturum" />
    }

    // Codex #139 P1-1: the authenticated actor no longer matches the bound context.
    // This is a SYNCHRONOUS gate on purpose — the reset runs in an effect, which is one
    // paint too late, and that paint would have shown the new user the previous user's
    // company, nav and capabilities. Nothing context-bound may render below this line.
    if (actorMismatch) {
      return <StateBlock state={{ kind: 'initial-loading' }} subject="oturum" />
    }

    // Directory / identity problems block the whole shell — fail closed.
    if (identityState.kind === 'initial-loading' || directoryState.kind === 'initial-loading') {
      return <StateBlock state={{ kind: 'initial-loading' }} subject="oturum" />
    }
    if (identityState.kind !== 'ready') {
      return <StateBlock state={identityState} subject="kimlik" onRetry={retry} />
    }
    if (directoryState.kind === 'no-authorized-company' || directoryState.kind === 'empty-no-data') {
      return <StateBlock state={{ kind: 'no-authorized-company' }} subject="şirket" />
    }
    if (directoryState.kind !== 'ready') {
      return <StateBlock state={directoryState} subject="şirket dizini" onRetry={retry} />
    }

    // Bootstrap could not reach a verified company context — retryable, no shell yet.
    if (bootstrapFailure) {
      return (
        <StateBlock
          state={{ kind: 'service-unavailable' }}
          subject="şirket bağlamı kurulamadı"
          onRetry={retry}
        />
      )
    }

    // P2-1: identity + directory resolved, context still being established — this is
    // loading, not "no authorized company".
    if (bootstrapping && state.phase === 'no-active-company') {
      return <StateBlock state={{ kind: 'initial-loading' }} subject="şirket bağlamı" />
    }
    return null
  })()
  const { pathname } = useLocation()
  const compact = useSyncExternalStore(subscribeCompact, readCompact, () => false)

  const groups = drawnGroups(state)
  const activeGroup = activeGroupOf(groups, pathname)
  // On a work-area page only that area's group is open (UX v1.2). With no work area open — the home
  // page — every group the actor has is open, so the whole menu is one look away and the sidebar
  // keeps offering every entry it offered before grouping. ASSUMPTION (P1-b), reversible here alone.
  const defaultExpanded: readonly NavGroupId[] = activeGroup ? [activeGroup] : groups.map((group) => group.id)
  // A user's open/close choice lasts until the page changes; every page starts from its own default,
  // including the one Back returns to.
  const [toggled, setToggled] = useState<{ readonly path: string; readonly open: readonly NavGroupId[] } | null>(null)
  if (toggled !== null && toggled.path !== pathname) setToggled(null)
  const expanded = toggled?.path === pathname ? toggled.open : defaultExpanded

  // The drawer belongs to the page it was opened on and only exists on a compact screen with a shell,
  // so a page change, a widening window or a gate (401, actor change, bootstrap) closes it.
  const [drawerPath, setDrawerPath] = useState<string | null>(null)
  if (drawerPath !== null && (drawerPath !== pathname || !compact || gate !== null)) setDrawerPath(null)
  const drawerOpen = drawerPath !== null

  // Focus lands somewhere safe when the dirty-switch dialog closes (ERP-000K
  // §13.8): back on the company picker when the user stays, on the main region
  // when the switch proceeds and the picker is disabled for its duration.
  const companySelectRef = useRef<HTMLSelectElement>(null)
  const mainRef = useRef<HTMLElement>(null)
  const previousPhaseRef = useRef(state.phase)
  useEffect(() => {
    const previous = previousPhaseRef.current
    previousPhaseRef.current = state.phase
    if (previous !== 'confirming-switch' || state.phase === 'confirming-switch') return
    if (state.phase === 'idle') companySelectRef.current?.focus()
    else mainRef.current?.focus()
  }, [state.phase])

  // The drawer is modal (UX v1.2): opening it moves focus to the open page's entry, or to its close
  // button when the page has none; closing returns focus to "Menü" — or to the new page's content
  // when the drawer closed because an entry was chosen, or to the sidebar's open entry (else the
  // content) when a widening window turned the drawer back into the sidebar.
  const sidebarRef = useRef<HTMLDivElement>(null)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const closeButtonRef = useRef<HTMLButtonElement>(null)
  const focusMainOnCloseRef = useRef(false)
  const drawerWasOpenRef = useRef(false)
  useEffect(() => {
    const wasOpen = drawerWasOpenRef.current
    drawerWasOpenRef.current = drawerOpen
    if (drawerOpen && !wasOpen) {
      ;(visibleCurrentEntry(sidebarRef.current) ?? closeButtonRef.current)?.focus()
    } else if (!drawerOpen && wasOpen) {
      if (focusMainOnCloseRef.current) mainRef.current?.focus()
      else if (compact) menuButtonRef.current?.focus()
      else if (!sidebarRef.current?.contains(document.activeElement)) {
        ;(visibleCurrentEntry(sidebarRef.current) ?? mainRef.current)?.focus()
      }
      focusMainOnCloseRef.current = false
    }
  }, [drawerOpen, compact])

  // Escape and Tab belong to the drawer wherever focus is, so neither is ever lost to the page behind.
  useEffect(() => {
    if (!drawerOpen) return
    const onKeyDown = (event: KeyboardEvent) => {
      const drawer = sidebarRef.current
      if (!drawer) return
      if (event.key === 'Escape') {
        event.preventDefault()
        setDrawerPath(null)
        return
      }
      if (event.key !== 'Tab') return
      // The page behind the drawer is inert, so the next stop after its last control is the browser.
      const focusable = Array.from(drawer.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
        (element) => element.closest('[hidden]') === null,
      )
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const active = document.activeElement
      if (!drawer.contains(active)) {
        event.preventDefault()
        ;(event.shiftKey ? last : first).focus()
      } else if (event.shiftKey && active === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && active === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [drawerOpen])

  // Narrowing the window while focus is in the sidebar hides it; hand focus to "Menü" instead of losing it.
  // A layout effect: it must run before the browser recalculates styles and drops focus from the hidden link.
  useLayoutEffect(() => {
    if (compact && sidebarRef.current?.contains(document.activeElement)) menuButtonRef.current?.focus()
  }, [compact])

  // The page behind an open drawer does not scroll, and does not shift sideways where a scrollbar goes.
  useEffect(() => {
    if (!drawerOpen) return
    const root = document.documentElement
    const previous = { overflow: root.style.overflow, gutter: root.style.scrollbarGutter }
    root.style.overflow = 'hidden'
    root.style.scrollbarGutter = 'stable'
    return () => {
      root.style.overflow = previous.overflow
      root.style.scrollbarGutter = previous.gutter
    }
  }, [drawerOpen])

  if (gate) return gate

  const switching = state.phase === 'switching'
  // Asking is not switching: the verified context is unchanged while the user
  // decides, so the outlet — and the dirty form in it — stays mounted underneath
  // the dialog. Unmounting here would destroy the very state "Kal" preserves.
  const confirming = state.phase === 'confirming-switch'
  const featuresClosed = capabilityFailClosed(state)

  const toggleGroup = (id: NavGroupId) => {
    // Opening a group closes the others, as in UX v1.2; closing one leaves the rest as they are.
    const open = expanded.includes(id) ? expanded.filter((group) => group !== id) : [id]
    setToggled({ path: pathname, open })
  }
  const openDrawer = () => {
    // Every opening starts from the page's own default, so its entry is in an open group and focusable.
    setToggled(null)
    setDrawerPath(pathname)
  }
  const closeDrawer = () => setDrawerPath(null)
  const chooseEntry = () => {
    if (!drawerOpen) return
    focusMainOnCloseRef.current = true
    closeDrawer()
  }
  const linkClass = ({ isActive }: { isActive: boolean }) => (isActive ? styles.navLinkActive : styles.navLink)

  return (
    <div className={styles.shell}>
      <a className={styles.skipLink} href="#main-content" inert={drawerOpen}>
        Ana içeriğe geç
      </a>

      <div
        id={SIDEBAR_ID}
        ref={sidebarRef}
        className={drawerOpen ? `${styles.sidebar} ${styles.drawer}` : styles.sidebar}
        hidden={compact && !drawerOpen}
        role={drawerOpen ? 'dialog' : undefined}
        aria-modal={drawerOpen ? true : undefined}
        aria-label={drawerOpen ? NAV_TEXT.drawer : undefined}
      >
        <div className={styles.brand}>
          <span>Göymen ERP</span>
          {drawerOpen ? (
            <button
              type="button"
              ref={closeButtonRef}
              className={styles.closeButton}
              aria-label={NAV_TEXT.closeDrawer}
              onClick={closeDrawer}
            >
              <Icon name="close" />
            </button>
          ) : null}
        </div>
        <nav className={styles.nav} aria-label="Modül navigasyonu">
          <ul className={styles.navList}>
            <li>
              <NavLink
                to={NAV_HOME.path}
                end
                className={({ isActive }) => (isActive ? styles.topLinkActive : styles.topLink)}
                onClick={chooseEntry}
              >
                <Icon name="home" />
                <span>{NAV_HOME.label}</span>
              </NavLink>
            </li>
            {/* DEC-GOV-007 / ERP-000K §8.1: an entry appears only when the server granted
                the capability AND the frontend has an implementation for it. No inert item,
                and a group is drawn only around at least one such entry. */}
            {groups.map((group) => {
              const open = expanded.includes(group.id)
              const listId = `nav-group-${group.id}`
              const active = group.id === activeGroup
              return (
                <li key={group.id} className={active ? styles.groupActive : undefined}>
                  <button
                    type="button"
                    className={styles.groupButton}
                    aria-expanded={open}
                    aria-controls={listId}
                    // A name, not hidden text: a browser reads the flex label and an extra span as
                    // separate blocks ("Satış , açık…"). It still begins with the visible label.
                    aria-label={active ? `${group.label}${NAV_TEXT.activeGroupHint}` : undefined}
                    onClick={() => toggleGroup(group.id)}
                  >
                    <Icon name={group.id} />
                    <span className={styles.groupLabel}>{group.label}</span>
                    <Icon name="chevron" className={styles.chevron} />
                  </button>
                  <ul id={listId} className={styles.groupList} hidden={!open}>
                    {group.leaves.map((leaf) => (
                      <Fragment key={leaf.key}>
                        {/* NAV-OPENING-01: words above the entry — not a link, not focusable, no control. */}
                        {leaf.subheading
                          ? <li className={styles.subheading} data-nav-subheading={leaf.key}>{leaf.subheading}</li>
                          : null}
                        <li>
                          <NavLink to={leaf.path} end className={linkClass} onClick={chooseEntry}>
                            {leaf.label}
                          </NavLink>
                        </li>
                      </Fragment>
                    ))}
                  </ul>
                </li>
              )
            })}
          </ul>
          {groups.length === 0 ? (
            <p className={styles.navEmpty}>
              {featuresClosed
                ? 'Yetenek bilgisi çözülene kadar modül gösterilmez.'
                : 'Bu şirket bağlamında henüz uygulanmış modül yok.'}
            </p>
          ) : null}
        </nav>
        {drawerOpen ? <div className={styles.sidebarFoot}>{identity ? identity.displayName : '…'}</div> : null}
      </div>
      {drawerOpen ? <div className={styles.backdrop} aria-hidden="true" onClick={closeDrawer} /> : null}

      <div className={styles.body} inert={drawerOpen}>
        <header className={styles.toolbar}>
          {compact ? (
            <button
              type="button"
              ref={menuButtonRef}
              className={styles.menuButton}
              aria-expanded={drawerOpen}
              aria-controls={SIDEBAR_ID}
              onClick={openDrawer}
            >
              <Icon name="menu" />
              {NAV_TEXT.menuButton}
            </button>
          ) : null}

          <label className={styles.companyPicker}>
            <span className={styles.companyPickerLabel}>Şirket</span>
            <select
              ref={companySelectRef}
              value={state.activeContext?.companyId ?? ''}
              disabled={switching || state.phase !== 'idle'}
              onChange={(event) => {
                const companyId = event.target.value
                if (companyId && companyId !== state.activeContext?.companyId) {
                  dispatch({
                    type: 'REQUEST_SWITCH',
                    target: { scopeMode: 'single', companyId },
                  })
                }
              }}
            >
              {/* ERP-000K §13.7: with no bound context the picker must not keep showing
                  a previous or in-flight target. An explicit empty option makes
                  value="" match a real option instead of leaving a stale selection. */}
              {state.activeContext === null ? <option value="">—</option> : null}
              {directory.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.legalName}
                  {company.isPlaceholder ? ' (geçici)' : ''}
                </option>
              ))}
            </select>
          </label>

          {/* AT-002: the global search box; it renders only with a searchable owner list grant. */}
          <GlobalSearchBox />

          <div className={styles.user}>{identity ? identity.displayName : '…'}</div>
        </header>

        {switchFailure ? (
          <div className={styles.switchFailure} role="alert">
            Şirket değiştirilemedi; önceki şirkette kalındı.{' '}
            <button type="button" onClick={retry}>
              Tekrar dene
            </button>
          </div>
        ) : null}

        {confirming ? (
          <SwitchConfirmDialog
            targetCompanyLabel={
              directory.find((company) => company.id === state.pendingTarget?.companyId)
                ?.legalName ?? null
            }
            onStay={() => dispatch({ type: 'CONFIRM_SWITCH_STAY' })}
            onDiscard={() => dispatch({ type: 'CONFIRM_SWITCH_DISCARD' })}
          />
        ) : null}

        <main id="main-content" className={styles.main} tabIndex={-1} ref={mainRef}>
          {switching ? (
            <StateBlock
              state={{ kind: 'initial-loading' }}
              subject={
                state.pendingTarget?.companyId ? 'şirket değiştiriliyor' : 'şirket bağlamı'
              }
            />
          ) : state.phase === 'no-active-company' ? (
            <StateBlock state={{ kind: 'no-authorized-company' }} subject="şirket" />
          ) : (
            <Outlet />
          )}
        </main>
      </div>
    </div>
  )
}
