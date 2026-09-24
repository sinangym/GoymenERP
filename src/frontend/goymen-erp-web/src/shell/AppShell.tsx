/**
 * SHELL-PILOT-01 common shell: dark-navy nav, global toolbar with the company
 * selector, breadcrumb slot, main outlet.
 *
 * Honest scope: NO business feature is mounted (there are none yet). The nav
 * shows only what the server capability snapshot allows — currently nothing —
 * so it renders an explicit "no module available" state, never a mock menu
 * (DEC-GOV-007). Company switch is fenced by the reducer + orchestrator.
 */

import { useEffect, useRef } from 'react'
import { NavLink, Outlet } from 'react-router'
import { capabilityFailClosed, hasCapability } from './shellState'
import { useShellDispatch, useShellState } from './shellContext'
import { useShellOrchestrator } from './useShellOrchestrator'
import { StateBlock } from './StateBlock'
import { SwitchConfirmDialog } from './SwitchConfirmDialog'
import {
  CAPABILITY,
  CAPABILITY_NAV_LABEL,
  CAPABILITY_ROUTE,
  type CapabilityId,
} from './capabilities'
import styles from './AppShell.module.css'

/** Every capability the frontend can actually mount, in nav order. */
/** Exported for `navEntryPointInvariant.test.ts`: the nav list and the label table must agree. */
export const NAV_CAPABILITIES: readonly CapabilityId[] = [
  CAPABILITY.cariPartiesList,
  /**
   * `cariPartiesCreate` is deliberately ABSENT, and it is the one removal here that is a decision
   * rather than a layout fact: K5 (Sinan, 20 Eylül 2026, #66 5748258299) makes creation a single
   * authorised "Yeni …" action INSIDE the relevant list and names cari among the modules whose
   * parallel sidebar-create must not be duplicated. C-01 carries that action from this change on,
   * so the route keeps exactly one entry point — the invariant `TEKNIK_TUZAKLAR §8` states, whose
   * CHOICE clause K5 narrowed.
   *
   * The two moves are in ONE change on purpose: `navEntryPointInvariant.test.ts` warns that
   * removing an entry without adding its replacement produces a screen reachable from nowhere,
   * which is the MFG-02 condition that file exists to catch.
   */
  CAPABILITY.cariAccountsLink,
  /**
   * K5 gives Product exactly one sidebar entry: the list. Create remains a mounted,
   * separately authorised route reached through the list's single create action; the
   * 360 and editor likewise stay route-only. Product capabilities still come from the
   * global Product authority, never from a company permission.
   */
  CAPABILITY.productItemsList,
  /**
   * Listed together with the route that serves it, on purpose: a nav entry whose route
   * is not mounted is a link to the not-found block. It still renders only where the
   * server granted the capability, so until Backend #246 publishes `organization.*` it
   * does not appear. The card is deliberately absent — it is reached from a location
   * row, never from the sidebar.
   */
  CAPABILITY.organizationSitesList,
  /**
   * ORG-03. The sidebar is not one option among several here, it is the ONLY one: the
   * route stands on `organization.work-centers.read` alone, so a holder of that and not
   * `organization.locations.read` never opens ORG-01 and could never be linked from it.
   * Nothing else renders only where the server granted it, as ever.
   */
  CAPABILITY.organizationWorkCentersList,
  /**
   * Listed together with the routes that serve them (S03). Each renders only where the server's
   * snapshot carries the capability; the 360 is reached from a row, never from the sidebar.
   */
  CAPABILITY.salesDraftsList,
  CAPABILITY.salesDraftsCreate,
  /**
   * MFG-02, listed with the route that serves it (MFG-ORD). It renders only where the server's
   * snapshot carries the capability; the order detail is reached from a list row, never the sidebar.
   *
   * It was MISSING until now, and the gap was invisible: the capability, its route and its label all
   * existed and `manufacturingMountDrift` asserted the label — but nothing asserted that any nav entry
   * NAMED it, and nothing in the tree links to `/uretim/emirler`. So a delivered screen could only be
   * opened by typing the URL, and the order detail sat behind a list nobody could reach (#363 review,
   * P3-3). The lesson is in the case beside this file: assert the LINK, not the label it would use.
   */
  CAPABILITY.manufacturingOrdersList,
  /**
   * Listed together with the routes that serve them (FIN-UI-1b / FIN-T06). Each renders only where
   * the server's snapshot carries the capability; the new draft, the detail and the propose screen
   * have none. The reversal checker queue is a separate work list and therefore has its own entry.
   */
  CAPABILITY.financeReceiptsList,
  CAPABILITY.financeReceiptsReverseApprove,
  CAPABILITY.financeBankAccountsList,
  /**
   * Listed together with the routes that serve them (ACC-V0b). Each renders only where the server's
   * snapshot carries the capability — staged until the acc01 acceptance — and the propose screen and
   * the journal detail are reached from their own list, never from the sidebar.
   */
  CAPABILITY.accountingPeriodsList,
  CAPABILITY.accountingJournalsList,
  CAPABILITY.accountingPostingQueues,
  CAPABILITY.inventoryOpeningStockRecord,
  CAPABILITY.inventoryStockPositionsList,
  /**
   * Listed together with the routes that serve them (LOG-4b). Each renders only where the server's
   * snapshot carries the capability (staged until the l04 suite); the detail is reached from a row.
   */
  // The L-03 creation screen is NOT here: the L-02 list carries its own primary link, and the rule
  // is exactly one entry point per creation route (coordinator, 19 September 2026). Two links with
  // the same accessible name to one route is what l04's strict-mode violation found, and a screen
  // reader announces it twice.
  CAPABILITY.logisticsInstructionsList,
  /**
   * P-01 and P-03 are Purchasing's two list entries. Their create actions live inside their lists
   * under K5, and each detail is reached from a row. Submit and approve are controls inside their
   * respective details, never routes or navigation items.
   */
  CAPABILITY.purchasingRequestsList,
  CAPABILITY.purchasingOrdersList,
]

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

  const switching = state.phase === 'switching'
  // Asking is not switching: the verified context is unchanged while the user
  // decides, so the outlet — and the dirty form in it — stays mounted underneath
  // the dialog. Unmounting here would destroy the very state "Kal" preserves.
  const confirming = state.phase === 'confirming-switch'
  const featuresClosed = capabilityFailClosed(state)
  const navItems = NAV_CAPABILITIES.filter((capability) =>
    hasCapability(state, capability),
  ).map((capability) => ({
    capability,
    path: CAPABILITY_ROUTE[capability],
    label: CAPABILITY_NAV_LABEL[capability],
  }))

  return (
    <div className={styles.shell}>
      <a className={styles.skipLink} href="#main-content">
        Ana içeriğe geç
      </a>

      <nav className={styles.nav} aria-label="Modül navigasyonu">
        <div className={styles.brand}>Göymen ERP</div>
        {/* DEC-GOV-007 / ERP-000K §8.1: an entry appears only when the server granted
            the capability AND the frontend has an implementation for it. No inert item. */}
        {navItems.length > 0 ? (
          <ul className={styles.navList}>
            {navItems.map((entry) => (
              <li key={entry.capability}>
                <NavLink
                  to={entry.path}
                  className={({ isActive }) => (isActive ? styles.navLinkActive : styles.navLink)}
                >
                  {entry.label}
                </NavLink>
              </li>
            ))}
          </ul>
        ) : (
          <p className={styles.navEmpty}>
            {featuresClosed
              ? 'Yetenek bilgisi çözülene kadar modül gösterilmez.'
              : 'Bu şirket bağlamında henüz uygulanmış modül yok.'}
          </p>
        )}
      </nav>

      <div className={styles.body}>
        <header className={styles.toolbar}>
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
