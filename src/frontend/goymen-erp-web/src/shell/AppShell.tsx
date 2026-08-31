/**
 * SHELL-PILOT-01 common shell: dark-navy nav, global toolbar with the company
 * selector, breadcrumb slot, main outlet.
 *
 * Honest scope: NO business feature is mounted (there are none yet). The nav
 * shows only what the server capability snapshot allows — currently nothing —
 * so it renders an explicit "no module available" state, never a mock menu
 * (DEC-GOV-007). Company switch is fenced by the reducer + orchestrator.
 */

import { Outlet } from 'react-router'
import { capabilityFailClosed } from './shellState'
import { useShellDispatch, useShellState } from './shellContext'
import { useShellOrchestrator } from './useShellOrchestrator'
import { StateBlock } from './StateBlock'
import styles from './AppShell.module.css'

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
    retry,
  } = useShellOrchestrator()

  // 401 anywhere — session is dead. Terminal (no login flow yet).
  if (sessionInvalid || identityState.kind === 'unauthenticated') {
    return <StateBlock state={{ kind: 'unauthenticated' }} subject="oturum" />
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

  const switching = state.phase === 'switching' || state.phase === 'confirming-switch'
  const featuresClosed = capabilityFailClosed(state)

  return (
    <div className={styles.shell}>
      <a className={styles.skipLink} href="#main-content">
        Ana içeriğe geç
      </a>

      <nav className={styles.nav} aria-label="Modül navigasyonu">
        <div className={styles.brand}>Göymen ERP</div>
        {/* Capability-gated: nothing is mounted until the server snapshot allows it. */}
        <p className={styles.navEmpty}>
          {featuresClosed
            ? 'Yetenek bilgisi çözülene kadar modül gösterilmez.'
            : 'Bu şirket bağlamında henüz uygulanmış modül yok.'}
        </p>
      </nav>

      <div className={styles.body}>
        <header className={styles.toolbar}>
          <label className={styles.companyPicker}>
            <span className={styles.companyPickerLabel}>Şirket</span>
            <select
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

        <main id="main-content" className={styles.main} tabIndex={-1}>
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
