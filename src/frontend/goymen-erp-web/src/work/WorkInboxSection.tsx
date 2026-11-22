/**
 * W1-A Work inbox — the "Benden karar bekleyen" view (AI-D31, AI-D32, YOL-D18): item cards (kalem kartı) with the kind,
 * module, document, since when it waits, its duration indicator and who it waits on, an owner link where the decision is
 * given, and an expandable explanation card built only from the owner's rule results and references. There is no approve
 * or reject control here, and the screen computes nothing: every number and the due time are the server's.
 *
 * The section reads on its own: its failure never hides the B-25 queue beside it, and the queue's never hides it.
 * Opening a card's explanation and following the owner link each send one fire-and-forget measurement event.
 */

import { useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { Button } from '../ui/Button'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchWorkInbox, recordWorkInboxEvent } from './workInboxApi'
import {
  DURATION_DEFINED,
  DURATION_UNDEFINED,
  HOLIDAY_CALENDAR_DEFINED,
  HOLIDAY_CALENDAR_UNDEFINED,
  INBOX_EVENT_OPENED,
  INBOX_EVENT_OWNER_SCREEN_OPENED,
  INBOX_SOURCE_READY,
  INBOX_SOURCE_UNAVAILABLE,
  type WorkInbox,
  type WorkInboxDuration,
  type WorkInboxHolidayCalendarState,
  type WorkInboxItem,
  type WorkInboxReference,
  type WorkInboxRule,
} from './workInboxContract'
import {
  INBOX_FILTER_PARAM,
  WORK_INBOX_TEXT as T,
  durationClassText,
  formatWithUnit,
  inboxKindText,
  inboxModuleText,
  inboxPermissionText,
  inboxRoleText,
  inboxRuleText,
  shortDocumentLabel,
} from './workInboxText'
import { formatInstant } from './workText'
import styles from './Work.module.css'

export function WorkInboxSection({
  companyId,
  namespace,
  routeEntryGeneration,
  enabled,
}: {
  readonly companyId: string | null
  readonly namespace: string | null
  readonly routeEntryGeneration: number
  /** The actor holds at least one inbox kind's approve capability. */
  readonly enabled: boolean
}) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', routeEntryGeneration, 'work-inbox'],
    enabled: enabled && namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchWorkInbox(companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  if (!enabled) return <p className={styles.muted} data-testid="work-inbox-no-authority">{T.noInboxAuthority}</p>
  const inbox = query.data?.type === 'ok-single' ? query.data.data : null
  if (inbox === null || companyId === null) {
    return <StateBlock
      state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>}
      subject={T.subject}
      onRetry={() => void query.refetch()} />
  }
  return <InboxBody inbox={inbox} companyId={companyId} />
}

function InboxBody({ inbox, companyId }: { readonly inbox: WorkInbox; readonly companyId: string }) {
  const [params, setParams] = useSearchParams()
  const kindFilter = params.get(INBOX_FILTER_PARAM.kind)
  const moduleFilter = params.get(INBOX_FILTER_PARAM.module)
  const overdueOnly = params.get(INBOX_FILTER_PARAM.overdue) === '1'
  const filterActive = kindFilter !== null || moduleFilter !== null || overdueOnly

  const unavailable = inbox.sources.filter((source) => source.state === INBOX_SOURCE_UNAVAILABLE)
  const more = inbox.sources.filter((source) => source.state === INBOX_SOURCE_READY && source.hasMore)
  const kinds = inbox.sources.map((source) => source.kind)
  const modules = [...new Set(inbox.sources.map((source) => source.module))]
  const items = inbox.items.filter((item) =>
    (kindFilter === null || item.kind === kindFilter)
    && (moduleFilter === null || item.module === moduleFilter)
    && (!overdueOnly || (item.duration.state === DURATION_DEFINED && item.duration.isOverdue)))

  const setFilter = (name: string, value: string | null) => {
    const next = new URLSearchParams(params)
    if (value === null || value === '') next.delete(name)
    else next.set(name, value)
    setParams(next, { replace: true })
  }
  const clearFilters = () => {
    const next = new URLSearchParams(params)
    for (const name of Object.values(INBOX_FILTER_PARAM)) next.delete(name)
    setParams(next, { replace: true })
  }

  return <>
    {unavailable.length > 0
      ? <p role="alert" data-testid="work-inbox-unavailable">{T.unavailable(unavailable.map((source) => inboxKindText(source.kind)))}</p>
      : null}
    {more.length > 0
      ? <p role="note" className={styles.muted} data-testid="work-inbox-has-more">{T.hasMore(more.map((source) => inboxKindText(source.kind)))}</p>
      : null}
    {inbox.items.length === 0
      ? unavailable.length === 0
        ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />
        : <p className={styles.muted} data-testid="work-inbox-empty-readable">{T.emptyReadable}</p>
      : <>
        <fieldset className={styles.filters} data-testid="work-inbox-filters">
          <legend>{T.filtersLabel}</legend>
          <label className={styles.filterField}>
            <span>{T.filterKind}</span>
            <select value={kindFilter ?? ''} onChange={(event) => setFilter(INBOX_FILTER_PARAM.kind, event.target.value)}>
              <option value="">{T.filterAll}</option>
              {kinds.map((kind) => <option key={kind} value={kind}>{inboxKindText(kind)}</option>)}
              {kindFilter !== null && !kinds.includes(kindFilter) ? <option value={kindFilter}>{kindFilter}</option> : null}
            </select>
          </label>
          <label className={styles.filterField}>
            <span>{T.filterModule}</span>
            <select value={moduleFilter ?? ''} onChange={(event) => setFilter(INBOX_FILTER_PARAM.module, event.target.value)}>
              <option value="">{T.filterAll}</option>
              {modules.map((module) => <option key={module} value={module}>{inboxModuleText(module)}</option>)}
              {moduleFilter !== null && !modules.includes(moduleFilter) ? <option value={moduleFilter}>{moduleFilter}</option> : null}
            </select>
          </label>
          <label className={styles.filterCheck}>
            <input
              type="checkbox"
              checked={overdueOnly}
              onChange={(event) => setFilter(INBOX_FILTER_PARAM.overdue, event.target.checked ? '1' : null)} />
            <span>{T.filterOverdue}</span>
          </label>
          {filterActive ? <Button type="button" size="sm" variant="ghost" onClick={clearFilters}>{T.clearFilters}</Button> : null}
        </fieldset>
        {items.length === 0
          ? <StateBlock state={{ kind: 'empty-filtered' }} subject={T.subject} />
          : <ul className={styles.cards} aria-label={T.listLabel} data-testid="work-inbox-list">
            {items.map((item) => <li key={`${item.kind}::${item.itemId}`}>
              <ItemCard item={item} companyId={companyId} />
            </li>)}
          </ul>}
      </>}
  </>
}

export function ItemCard({ item, companyId }: { readonly item: WorkInboxItem; readonly companyId: string }) {
  const [expanded, setExpanded] = useState(false)
  const openedSent = useRef(false)
  const panelId = useId()
  const headingId = useId()
  const kindText = inboxKindText(item.kind)
  const toggle = () => {
    const next = !expanded
    setExpanded(next)
    if (next && !openedSent.current) {
      openedSent.current = true
      void recordWorkInboxEvent(companyId, item, INBOX_EVENT_OPENED)
    }
  }
  return <article className={styles.card} aria-labelledby={headingId} data-testid="work-inbox-item" data-kind={item.kind}>
    <h3 id={headingId} className={styles.cardTitle}>{kindText}</h3>
    <dl className={styles.facts}>
      <div><dt>{T.module}</dt><dd>{inboxModuleText(item.module)}</dd></div>
      <div><dt>{T.document}</dt><dd title={item.documentLabel}>{shortDocumentLabel(item.documentLabel)}</dd></div>
      <div><dt>{T.waiting}</dt><dd><time dateTime={item.waitingSinceUtc}>{formatInstant(item.waitingSinceUtc)}</time></dd></div>
      <div><dt>{T.duration}</dt><dd><DurationIndicator duration={item.duration} /></dd></div>
      <div><dt>{T.awaiting}</dt><dd data-testid="work-inbox-awaiting">{T.awaitingText(inboxPermissionText(item.awaitingPermission))}</dd></div>
    </dl>
    <div className={styles.actions}>
      <Link
        to={item.link}
        aria-label={T.openOwnerLabel(kindText, item.documentLabel)}
        onClick={() => void recordWorkInboxEvent(companyId, item, INBOX_EVENT_OWNER_SCREEN_OPENED)}>
        {T.openOwner}
      </Link>
      <Button type="button" size="sm" variant="ghost" aria-expanded={expanded} aria-controls={panelId} onClick={toggle}>
        {T.explanationToggle}
      </Button>
    </div>
    <div id={panelId} hidden={!expanded} className={styles.explanation} data-testid="work-inbox-explanation">
      {expanded ? <ExplanationCard item={item} /> : null}
    </div>
  </article>
}

function ExplanationCard({ item }: { readonly item: WorkInboxItem }) {
  return <>
    <h4>{T.rulesHeading}</h4>
    <ul className={styles.ruleList}>
      {item.explanation.rules.map((rule, index) => <li key={`${rule.ruleCode}::${index}`}><RuleLine rule={rule} /></li>)}
    </ul>
    <h4>{T.referencesHeading}</h4>
    {item.explanation.references.length === 0
      ? <p className={styles.muted}>{T.noReferences}</p>
      : <ul className={styles.ruleList}>
        {item.explanation.references.map((reference, index) =>
          <li key={`${reference.role}::${index}`}><ReferenceLine reference={reference} /></li>)}
      </ul>}
    <p className={styles.muted}>{T.explanationNote}</p>
  </>
}

function RuleLine({ rule }: { readonly rule: WorkInboxRule }) {
  const text = inboxRuleText(rule.ruleCode)
  const numbers: [string, string][] = []
  if (rule.threshold !== null) numbers.push([T.threshold, formatWithUnit(rule.threshold, rule.unit)])
  if (rule.actual !== null) numbers.push([T.actual, formatWithUnit(rule.actual, rule.unit)])
  if (rule.deviation !== null) numbers.push([T.deviation, formatWithUnit(rule.deviation, rule.unit)])
  return <div data-testid="work-inbox-rule" data-rule={rule.ruleCode}>
    {text === null
      ? <p>{T.ruleUnknown} <code className={styles.techCode}>{rule.ruleCode}</code></p>
      : <p>{text}</p>}
    {numbers.length > 0
      ? <dl className={styles.ruleNumbers}>
        {numbers.map(([label, value]) => <div key={label}><dt>{label}</dt><dd className={styles.numeric}>{value}</dd></div>)}
      </dl>
      : null}
  </div>
}

function ReferenceLine({ reference }: { readonly reference: WorkInboxReference }) {
  const role = inboxRoleText(reference.role)
  return <span data-testid="work-inbox-reference">
    {role === null
      ? <>{T.roleUnknown} <code className={styles.techCode}>{reference.role}</code></>
      : role}
    {': '}
    {reference.link === null
      ? <span title={reference.label}>{shortDocumentLabel(reference.label)}</span>
      : <Link to={reference.link} title={reference.label}>{shortDocumentLabel(reference.label)}</Link>}
  </span>
}

/** YOL-D22: a business-day due time says whether official holidays were deducted or only weekends. */
function HolidayCalendarNote({ state }: { readonly state: WorkInboxHolidayCalendarState | null }) {
  if (state === null) return null
  if (state === HOLIDAY_CALENDAR_DEFINED) {
    return <span className={styles.muted} data-testid="work-inbox-holiday-calendar" data-state={state}> · {T.holidaysDeducted}</span>
  }
  return <span className={styles.holidayWarning} role="note" data-testid="work-inbox-holiday-calendar" data-state={state}>
    {state === HOLIDAY_CALENDAR_UNDEFINED ? T.holidayCalendarUndefined : T.holidayCalendarUnavailable}
  </span>
}

export function DurationIndicator({ duration }: { readonly duration: WorkInboxDuration }) {
  if (duration.state === DURATION_DEFINED) {
    return <span data-testid="work-inbox-duration" data-state={duration.state}>
      <time dateTime={duration.dueAtUtc}>{T.due(formatInstant(duration.dueAtUtc))}</time>
      {' · '}
      <span>{durationClassText(duration.class, duration.limit)}</span>
      {duration.isOverdue ? <> <strong className={styles.overdue}>{T.overdue}</strong></> : null}
      <HolidayCalendarNote state={duration.holidayCalendar} />
    </span>
  }
  return <span data-testid="work-inbox-duration" data-state={duration.state} className={styles.muted}>
    {duration.state === DURATION_UNDEFINED ? T.durationUndefined : T.durationUnavailable}
  </span>
}
