/**
 * "Denetim kaydı" — UI-K02 read-only audit log (SY-07), under Ayarlar › Sistem Yönetimi (KLN-D2).
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - Read-only. One GET (`GET /api/v1/audit/events`), no write, export or delete control and no placeholder for one.
 *  - Remounted per committed context, route entry and filter set: a stale render would put one company's audit trail
 *    under another company's label. Every read runs behind one request fence; an answer of a superseded read or
 *    context is dropped before it can paint.
 *  - Filters are the SERVER's exact-match filters, kept in the URL (back, reload and a shared link show the same view),
 *    trimmed and length-checked here so an over-long value is never sent. Rows are never filtered, sorted or counted
 *    on the client beyond de-duplicating a repeated id across pages.
 *  - Privacy (Decision No 135): client IP and user agent are never rendered; the before / after / approval payloads
 *    are shown only through `maskAuditJson`.
 *  - Paging: the opaque `nextCursor`; a failed next page keeps the rows already shown and offers a retry of the SAME
 *    cursor; a cursor the server refuses (400) restarts the list from the first page with a message.
 */

import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { createRequestFence } from '../purchasing/requestFence'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchAuditEventPage } from './api'
import {
  AUDIT_CAPABILITY,
  AUDIT_FILTERS,
  auditListPath,
  filterKey,
  filtersFromSearch,
  hasActiveFilter,
  overLongFilters,
  type AuditFilterKey,
  type AuditFilters,
} from './capabilities'
import type { AuditEvent, AuditEventPage } from './contract'
import { maskAuditJson, maskFreeText } from './mask'
import { AUDIT_TEXT as T, FILTER_LABEL, formatInstant } from './auditText'
import styles from './audit.module.css'

/** Why a continuation did not extend the list; the rows already shown stay on screen. */
type MoreProblem = 'unavailable' | 'malformed'
type MoreState = 'idle' | 'loading' | MoreProblem

type State =
  | { readonly type: 'loading'; readonly cursorReset: boolean }
  /** The first page failed: nothing is shown. `invalid-filter` is a filter the server (or the length check) refused. */
  | { readonly type: 'failed'; readonly view: RemoteViewState<unknown> | { readonly kind: 'invalid-filter' } }
  | {
      readonly type: 'ready'
      readonly items: readonly AuditEvent[]
      readonly nextCursor: string | null
      readonly more: MoreState
      readonly cursorReset: boolean
    }

export function AuditLogPage() {
  const shell = useShellState()
  const [searchParams] = useSearchParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const filters = filtersFromSearch(searchParams)
  const companyId =
    namespace !== null && (shell.activeContext?.scopeMode ?? 'single') === 'single'
      ? (shell.activeContext?.companyId ?? null)
      : null
  const canRead = hasCapability(shell, AUDIT_CAPABILITY.list)
  const filtersKey = filterKey(filters)

  return (
    <section aria-labelledby="audit-log-heading" className={styles.page} data-screen="SY-07">
      <h1 id="audit-log-heading">{T.heading}</h1>
      <p className={styles.lead}>{T.lead}</p>
      <p className={styles.note}>{T.maskNote}</p>
      {companyId === null ? (
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.contextSubject} />
      ) : !canRead ? (
        <StateBlock state={{ kind: 'not-found' }} subject={T.contextSubject} />
      ) : (
        <>
          <FilterForm key={filtersKey} filters={filters} />
          <AuditLogView
            key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}::${filtersKey}`}
            companyId={companyId}
            filters={filters}
          />
        </>
      )}
    </section>
  )
}

function FilterForm({ filters }: { readonly filters: AuditFilters }) {
  const navigate = useNavigate()
  const [values, setValues] = useState<AuditFilters>(filters)
  const [tooLong, setTooLong] = useState<readonly AuditFilterKey[]>([])

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const trimmed: AuditFilters = {
      correlationId: values.correlationId.trim(),
      entityType: values.entityType.trim(),
      entityId: values.entityId.trim(),
    }
    const refused = overLongFilters(trimmed)
    setTooLong(refused)
    if (refused.length > 0) return
    void navigate(auditListPath(trimmed))
  }

  return (
    <form className={styles.panel} onSubmit={submit} aria-labelledby="audit-filter-heading" noValidate>
      <h2 id="audit-filter-heading">{T.filtersHeading}</h2>
      <p className={styles.muted}>{T.filtersHelp}</p>
      <div className={styles.filters}>
        {AUDIT_FILTERS.map((filter) => (
          <Field
            key={filter.key}
            label={FILTER_LABEL[filter.key]}
            error={tooLong.includes(filter.key) ? T.filterTooLong(filter.maxLength) : undefined}
          >
            {(control) => (
              <input
                {...control}
                type="text"
                name={filter.param}
                autoComplete="off"
                spellCheck={false}
                value={values[filter.key]}
                onChange={(event) => setValues({ ...values, [filter.key]: event.target.value })}
              />
            )}
          </Field>
        ))}
      </div>
      <div className={styles.actions}>
        <Button type="submit" variant="primary">
          {T.filterApply}
        </Button>
        {hasActiveFilter(filters) ? (
          <Button type="button" onClick={() => void navigate(auditListPath())}>
            {T.filterClear}
          </Button>
        ) : null}
      </div>
    </form>
  )
}

function AuditLogView({ companyId, filters }: { readonly companyId: string; readonly filters: AuditFilters }) {
  const boundary = useAuthBoundary()
  const boundaryRef = useRef(boundary)
  useEffect(() => {
    boundaryRef.current = boundary
  }, [boundary])
  const fence = useRef(createRequestFence())
  // The filters are fixed for this mount: a filter change remounts the view through its key.
  const filtersRef = useRef(filters)
  const filterActive = hasActiveFilter(filters)
  // A filter the server would refuse for its length is never sent: the view starts in the invalid-filter state.
  const refusedLocally = overLongFilters(filters).length > 0
  const [state, setState] = useState<State>(
    refusedLocally ? { type: 'failed', view: { kind: 'invalid-filter' } } : { type: 'loading', cursorReset: false },
  )

  /** One read; null when the answer belongs to a superseded request or context and must not be applied. */
  const read = useCallback(
    async (cursor: string | null): Promise<ViewOutcome<AuditEventPage> | null> => {
      const token = fence.current.capture()
      const security = boundaryRef.current.capture()
      const outcome = await fetchAuditEventPage(companyId, filtersRef.current, cursor, token.signal)
      if (!fence.current.isCurrent(token)) return null
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundaryRef.current.report(outcome.status, security)
      }
      return outcome
    },
    [companyId],
  )

  const applyFirst = useCallback(
    (outcome: ViewOutcome<AuditEventPage> | null, cursorReset: boolean) => {
      if (outcome === null) return
      if (outcome.type === 'ok-single') {
        setState({ type: 'ready', items: outcome.data.items, nextCursor: outcome.data.nextCursor, more: 'idle', cursorReset })
      } else if (outcome.type === 'http' && outcome.status === 400 && filterActive) {
        setState({ type: 'failed', view: { kind: 'invalid-filter' } })
      } else {
        setState({ type: 'failed', view: classifyOutcome(outcome) })
      }
    },
    [filterActive],
  )

  // The view starts in `loading`, so the mount read sets no state before its answer.
  useEffect(() => {
    const currentFence = fence.current
    if (!refusedLocally) void read(null).then((outcome) => applyFirst(outcome, false))
    return () => currentFence.invalidate()
  }, [read, applyFirst, refusedLocally])

  /** Restart from the first page: what was shown is dropped, nothing is carried into the new read. */
  const loadFirst = (cursorReset: boolean) => {
    setState({ type: 'loading', cursorReset })
    void read(null).then((outcome) => applyFirst(outcome, cursorReset))
  }

  const loadMore = async (shown: readonly AuditEvent[], cursor: string) => {
    setState({ type: 'ready', items: shown, nextCursor: cursor, more: 'loading', cursorReset: false })
    const outcome = await read(cursor)
    if (outcome === null) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      // The authority to read is gone: nothing read under it stays on screen.
      setState({ type: 'failed', view: classifyOutcome(outcome) })
      return
    }
    if (outcome.type === 'http' && outcome.status === 400) {
      // The owner refuses a cursor that expired or was issued for another view: start again from the first page.
      loadFirst(true)
      return
    }
    if (outcome.type === 'ok-single') {
      // A page that repeats an event already shown does not show it twice.
      const seen = new Set(shown.map((item) => item.id.toLowerCase()))
      const fresh = outcome.data.items.filter((item) => !seen.has(item.id.toLowerCase()))
      setState({ type: 'ready', items: [...shown, ...fresh], nextCursor: outcome.data.nextCursor, more: 'idle', cursorReset: false })
      return
    }
    const problem: MoreProblem = outcome.type === 'malformed' || (outcome.type === 'http' && outcome.status < 500) ? 'malformed' : 'unavailable'
    setState({ type: 'ready', items: shown, nextCursor: cursor, more: problem, cursorReset: false })
  }

  // While "Daha fazla" is loading it is disabled and focus falls to the page body. When the read settles, focus goes
  // back to the button if it is still there, else to the shown-count status, so keyboard users keep their place.
  const moreButtonRef = useRef<HTMLButtonElement>(null)
  const shownStatusRef = useRef<HTMLParagraphElement>(null)
  const previousMore = useRef<MoreState | null>(null)
  const currentMore = state.type === 'ready' ? state.more : null
  useEffect(() => {
    const settled = previousMore.current === 'loading' && currentMore !== 'loading'
    previousMore.current = currentMore
    if (!settled) return
    const active = document.activeElement
    if (active !== null && active !== document.body) return
    ;(moreButtonRef.current ?? shownStatusRef.current)?.focus()
  }, [currentMore])

  const cursorResetNotice =
    (state.type === 'loading' || state.type === 'ready') && state.cursorReset ? (
      <p role="status" className={styles.notice}>
        {T.cursorReset}
      </p>
    ) : null

  if (state.type === 'loading') {
    return (
      <>
        {cursorResetNotice}
        <StateBlock state={{ kind: 'initial-loading' }} subject={T.subject} />
      </>
    )
  }

  if (state.type === 'failed') {
    if (state.view.kind === 'invalid-filter') {
      return (
        <div role="alert" className={styles.notice} data-state="invalid-filter">
          <p>
            <strong>{T.invalidFilterTitle}</strong> — {T.invalidFilterDetail}
          </p>
        </div>
      )
    }
    return (
      <StateBlock state={state.view as RemoteViewState<unknown>} subject={T.subject} onRetry={() => loadFirst(false)} />
    )
  }

  if (state.items.length === 0) {
    return (
      <>
        {cursorResetNotice}
        <StateBlock state={{ kind: filterActive ? 'empty-filtered' : 'empty-no-data' }} subject={T.subject} />
      </>
    )
  }

  const { items, nextCursor, more } = state
  return (
    <>
      {cursorResetNotice}
      <div className={styles.tableWrap}>
        <table className={styles.table} aria-busy={more === 'loading'}>
          <caption>{T.tableCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{T.columns.time}</th>
              <th scope="col">{T.columns.eventType}</th>
              <th scope="col">{T.columns.entity}</th>
              <th scope="col">{T.columns.operation}</th>
              <th scope="col">{T.columns.version}</th>
              <th scope="col">{T.columns.actor}</th>
              <th scope="col">{T.columns.permission}</th>
              <th scope="col">{T.columns.reason}</th>
              <th scope="col">{T.columns.correlation}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <AuditEventRows key={item.id} item={item} />
            ))}
          </tbody>
        </table>
      </div>
      <div className={styles.footer}>
        <p role="status" tabIndex={-1} ref={shownStatusRef}>
          {T.shownCount(items.length)}
        </p>
        {nextCursor === null ? <p>{T.end}</p> : null}
        {nextCursor !== null && (more === 'idle' || more === 'loading') ? (
          <Button type="button" ref={moreButtonRef} disabled={more === 'loading'} onClick={() => void loadMore(items, nextCursor)}>
            {more === 'loading' ? T.loadingMore : T.loadMore}
          </Button>
        ) : null}
      </div>
      {nextCursor !== null && (more === 'unavailable' || more === 'malformed') ? (
        <div role="alert" className={styles.notice}>
          <p>{more === 'unavailable' ? T.continuationUnavailable : T.continuationMalformed}</p>
          <Button type="button" onClick={() => void loadMore(items, nextCursor)}>
            {T.retry}
          </Button>
        </div>
      ) : null}
    </>
  )
}

function version(value: number | null): string {
  return value === null ? T.noValue : String(value)
}

function AuditEventRows({ item }: { readonly item: AuditEvent }) {
  return (
    <>
      <tr className={styles.eventRow} data-audit-id={item.id}>
        <td>
          <time dateTime={item.occurredAtUtc}>{formatInstant(item.occurredAtUtc)}</time>
        </td>
        <td className={styles.code}>{item.eventType}</td>
        <td>
          <span className={styles.code}>{item.entityType}</span>
          <br />
          <span className={`${styles.code} ${styles.secondary}`}>{item.entityId}</span>
        </td>
        <td className={styles.code}>{item.operation}</td>
        <td className={styles.code}>
          {version(item.entityVersionBefore)} {T.versionArrow} {version(item.entityVersionAfter)}
        </td>
        <td>
          <span className={styles.code}>{item.actorType}</span>
          <br />
          <span className={`${styles.code} ${styles.secondary}`}>{item.actorId}</span>
        </td>
        <td className={styles.code}>{item.permissionCode}</td>
        {/* The reason is free text a person typed: the same free-text masking as the payloads applies (No 135). */}
        <td>{item.reason === '' ? T.noValue : maskFreeText(item.reason)}</td>
        <td className={styles.code}>{item.correlationId}</td>
      </tr>
      <tr>
        <td colSpan={9}>
          <AuditDetails item={item} />
        </td>
      </tr>
    </>
  )
}

/** The payloads are masked and rendered only once the disclosure is opened. */
function AuditDetails({ item }: { readonly item: AuditEvent }) {
  const [open, setOpen] = useState(false)
  return (
    <details className={styles.details} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>{T.details}</summary>
      {open ? (
        <div className={styles.payloads}>
          <Payload title={T.before} text={item.beforeJson} />
          <Payload title={T.after} text={item.afterJson} />
          {item.approvalContextJson === null ? (
            <div>
              <h3>{T.approvalContext}</h3>
              <p className={styles.muted}>{T.noApprovalContext}</p>
            </div>
          ) : (
            <Payload title={T.approvalContext} text={item.approvalContextJson} />
          )}
        </div>
      ) : null}
    </details>
  )
}

function Payload({ title, text }: { readonly title: string; readonly text: string }) {
  const masked = maskAuditJson(text)
  return (
    <div>
      <h3>{title}</h3>
      {masked === null ? (
        <p className={styles.muted}>{T.unreadablePayload}</p>
      ) : (
        <pre className={styles.payload} role="region" aria-label={title} tabIndex={0}>
          {masked}
        </pre>
      )}
    </div>
  )
}
