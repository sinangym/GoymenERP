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
const NAV_CAPABILITIES: readonly CapabilityId[] = [
  CAPABILITY.cariPartiesList,
  CAPABILITY.cariPartiesCreate,
  CAPABILITY.cariAccountsLink,
  /**
   * Listed together with the routes that serve them, on purpose: a nav entry whose
   * route is not mounted is a link to the not-found block. Each still renders only
   * where the server's snapshot carries the capability — the backend registry grants
   * `product.items.*` from the global Product authority (read / draft create), never
   * from a company permission — so an actor without that authority sees neither. The
   * 360 and the editor are deliberately absent — reached from a row and from the card,
   * never from the sidebar.
   */
  CAPABILITY.productItemsList,
  CAPABILITY.productItemsCreate,
  /**
   * Listed together with the route that serves it, on purpose: a nav entry whose route
   * is not mounted is a link to the not-found block. It still renders only where the
   * server granted the capability, so until Backend #246 publishes `organization.*` it
   * does not appear. The card is deliberately absent — it is reached from a location
   * row, never from the sidebar.
   */
  CAPABILITY.organizationSitesList,
  /**
   * Listed together with the routes that serve them (S03). Each renders only where the server's
   * snapshot carries the capability; the 360 is reached from a row, never from the sidebar.
   */
  CAPABILITY.salesDraftsList,
  CAPABILITY.salesDraftsCreate,
  /**
   * Listed together with the routes that serve them (FIN-UI-1b). Each renders only where the server's
   * snapshot carries the capability; the new draft, the detail and the propose screen have none.
   */
  CAPABILITY.financeReceiptsList,
  CAPABILITY.financeBankAccountsList,
  /**
   * Listed together with the routes that serve them (LOG-4b). Each renders only where the server's
   * snapshot carries the capability (staged until the l04 suite); the detail is reached from a row.
   */
  CAPABILITY.logisticsInstructionsList,
  CAPABILITY.logisticsInstructionsCreate,
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
