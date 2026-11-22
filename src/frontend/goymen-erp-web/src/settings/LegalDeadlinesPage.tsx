/**
 * AY-YS-01 `/ayarlar/yasal-sureler` (WP15, DALGA_PLANI_v2 1.16): the upcoming legal deadlines, read from Work's deadline view
 * (`GET /api/v1/work/deadlines`) on `settings.legal-deadlines.read`. The server computes every due date and day count; an
 * obligation without a confirmed calendar entry is shown as "Tanımsız" with no date (fail closed). `LegalDeadlinesPanel` is the
 * same view for the Work inbox.
 */

import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { SETTINGS_CAPABILITY } from './capabilities'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { fetchWorkDeadlines } from './legalDeadlineCalendarApi'
import type { WorkDeadlinePage } from './workDeadlinesContract'
import { LEGAL_DEADLINE_CODE_TEXT, LEGAL_DEADLINE_TEXT as T } from './legalDeadlineText'
import styles from './settings.module.css'

/** `yyyy-MM-dd` as Turkish `dd.MM.yyyy`, by string handling only. */
const dateText = (date: string) => { const [y, m, d] = date.split('-'); return `${d}.${m}.${y}` }

export function LegalDeadlinesPage() {
  return <Frame title={T.deadlinesTitle} intro={T.deadlinesIntro}><LegalDeadlinesPanel /></Frame>
}

/** The deadline list for the active company; renders nothing when the caller lacks the read capability. */
export function LegalDeadlinesPanel({ heading = false }: { readonly heading?: boolean }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  if (!hasCapability(shell, SETTINGS_CAPABILITY.legalDeadlines)) return null
  return <Panel key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} heading={heading} />
}

function Panel({ namespace, heading }: { readonly namespace: string | null; readonly heading: boolean }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'work-deadlines'], ready,
    (signal) => fetchWorkDeadlines(company ?? '', signal))
  const state = stateOf<WorkDeadlinePage>(query.isPending, query.data)
  return <section aria-labelledby={heading ? 'legal-deadlines-heading' : undefined} data-testid="legal-deadlines">
    {heading ? <h2 id="legal-deadlines-heading">{T.deadlinesTitle}</h2> : null}
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.deadlinesSubject} />
      : state.kind === 'ready' ? <Deadlines page={state.data} />
        : <StateBlock state={state} subject={T.deadlinesSubject} onRetry={() => void query.refetch()} />}
  </section>
}

function Deadlines({ page }: { readonly page: WorkDeadlinePage }) {
  const unavailable = page.sources.some((source) => source.outcome === 'unavailable')
  const more = page.sources.some((source) => source.hasMore)
  return <>
    {unavailable ? <p role="alert">{T.sourceUnavailable}</p> : null}
    <p className={styles.muted}>{T.noShift}</p>
    {page.items.length === 0 ? <p data-testid="legal-deadlines-empty">{T.noItems}</p>
      : <table className={styles.table}>
        <caption>{T.deadlinesCaption}</caption>
        <thead><tr><th scope="col">{T.obligation}</th><th scope="col">{T.state}</th><th scope="col">{T.dueDate}</th><th scope="col">{T.daysRemaining}</th><th scope="col">{T.forPeriod}</th><th scope="col">{T.definition}</th></tr></thead>
        <tbody>{page.items.map((item) => <tr key={`${item.kind}:${item.code}`} data-state={item.state}>
          <td>{LEGAL_DEADLINE_CODE_TEXT[item.code] ?? item.code}</td>
          <td>{item.state === 'undefined' ? T.undefinedState : item.daysRemaining === 0 ? T.today : T.days(item.daysRemaining ?? 0)}</td>
          <td>{item.dueDate === null ? '—' : <><time dateTime={item.dueDate}>{dateText(item.dueDate)}</time>
            {item.dueDateAdjustment === 'moved-to-business-day' && item.originalDueDate !== null
              ? <small data-testid="legal-deadline-moved"> {T.movedNote(dateText(item.originalDueDate))}</small>
              : item.dueDateAdjustment === 'holiday-calendar-undefined'
                ? <small data-testid="legal-deadline-unshifted"> {T.calendarUndefinedNote}</small> : null}</>}</td>
          <td>{item.daysRemaining ?? '—'}</td>
          <td>{item.periodStart !== null && item.periodEnd !== null ? `${dateText(item.periodStart)} – ${dateText(item.periodEnd)}` : '—'}</td>
          <td><Link to={item.link}>{T.openDefinition}</Link></td>
        </tr>)}</tbody>
      </table>}
    {more ? <p className={styles.muted}>{T.hasMore}</p> : null}
  </>
}
