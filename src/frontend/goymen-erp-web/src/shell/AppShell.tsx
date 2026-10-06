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
import { GlobalSearchBox } from '../search/GlobalSearchBox'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE } from '../product/priceLists/capabilities'
import { PRICE_LIST_TEXT } from '../product/priceLists/priceListText'
import { CAPABILITY_ROUTE } from './capabilities'
import { CAPABILITY_NAV_LABEL, NAV_CAPABILITIES } from './navigation'
import styles from './AppShell.module.css'

/** The nav order is defined in `./navigation`; re-exported for the tests that read it from the shell. */
export { NAV_CAPABILITIES }

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
  const navItems = [
    ...NAV_CAPABILITIES.filter((capability) => hasCapability(state, capability)).map((capability) => ({
      key: capability,
      path: CAPABILITY_ROUTE[capability],
      label: CAPABILITY_NAV_LABEL[capability],
    })),
    ...(hasCapability(state, PRICE_LIST_CAPABILITY.list)
      ? [{
          key: PRICE_LIST_CAPABILITY.list,
          path: PRICE_LIST_ROUTE.list,
          label: PRICE_LIST_TEXT.listHeading,
        }]
      : []),
  ]
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
              <li key={entry.key}>
                <NavLink
                  to={entry.path}
                  end
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
