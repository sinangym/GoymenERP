/**
 * M-01 Muhasebe Kontrol (ACC-V0), READ-ONLY: the facts whose posting waits.
 *  - a missing account mapping stays a queue entry, never a guessed account;
 *  - an unknown amount stays pending, never a zero.
 * Nothing here posts, maps or values anything.
 */

import { useQuery } from '@tanstack/react-query'
import { fetchPostingQueues } from './api'
import type { PostingQueues } from './contract'
import {
  EMPTY_EXCEPTIONS,
  EMPTY_PENDING,
  EXCEPTIONS_NOTE,
  EXCEPTIONS_TITLE,
  PENDING_NOTE,
  PENDING_TITLE,
  QUEUES_TITLE,
} from './accountingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './Accounting.module.css'

export function PostingQueuesPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Queues key={namespace ?? 'no-context'} namespace={namespace} />
}

function Queues({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting-posting-queues'],
    enabled: namespace !== null && singleCompany,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPostingQueues(companyId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!singleCompany) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="muhasebe kontrolü için tek şirket bağlamı" />
      </Frame>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<PostingQueues>
  if (state.kind !== 'ready') {
    return (
      <Frame>
        <StateBlock state={state} subject="muhasebe kuyrukları" onRetry={() => void query.refetch()} />
      </Frame>
    )
  }

  const { exceptions, pending } = state.data
  return (
    <Frame>
      <section aria-labelledby="accounting-exceptions-title" data-queue="exceptions">
        <h2 id="accounting-exceptions-title">{EXCEPTIONS_TITLE}</h2>
        <p className={styles.help}>{EXCEPTIONS_NOTE}</p>
        {exceptions.length === 0 ? (
          <p className={styles.help}>{EMPTY_EXCEPTIONS}</p>
        ) : (
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">Kaynak</th>
                  <th scope="col">Amaç · kural</th>
                  <th scope="col">Eksik eşlemeler</th>
                </tr>
              </thead>
              <tbody>
                {exceptions.map((entry) => (
                  <tr key={`${entry.sourceId}|${entry.postingPurpose}`}>
                    <td className={styles.mono}>{entry.sourceId}</td>
                    <td>
                      {entry.postingPurpose} · {entry.postingRuleVersion}
                    </td>
                    <td className={styles.mono}>{entry.missingMappingKeys.join(', ')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      <section aria-labelledby="accounting-pending-title" data-queue="pending">
        <h2 id="accounting-pending-title">{PENDING_TITLE}</h2>
        <p className={styles.help}>{PENDING_NOTE}</p>
        {pending.length === 0 ? (
          <p className={styles.help}>{EMPTY_PENDING}</p>
        ) : (
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">Kaynak</th>
                  <th scope="col">Amaç · kural</th>
                  <th scope="col">Tutarı bekleyen satır</th>
                </tr>
              </thead>
              <tbody>
                {pending.map((entry) => (
                  <tr key={`${entry.sourceId}|${entry.postingPurpose}`}>
                    <td className={styles.mono}>{entry.sourceId}</td>
                    <td>
                      {entry.postingPurpose} · {entry.postingRuleVersion}
                    </td>
                    <td>{entry.linesAwaitingAmount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </Frame>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="accounting-queues-title" className={styles.page}>
      <h1 id="accounting-queues-title">{QUEUES_TITLE}</h1>
      {children}
    </section>
  )
}
