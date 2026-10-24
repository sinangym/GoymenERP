/**
 * "Denetim kayıtları" — the read-only audit trail of the active company (`GET /api/v1/audit/events`).
 *
 * The flow: open the list (newest first) → narrow it by correlation id, record type or record id → follow a row's
 * record or request to its own history → page to older records and back. Filters and the page cursor live in the
 * URL, so back, reload and a shared link show the same page; nothing is kept in browser storage.
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - Read-only. The audit trail is append-only; there is no control that writes, and none is drawn disabled.
 *  - Default-deny: nothing is read unless the shell granted `audit.events.list` for this route entry. The backend
 *    does not publish that capability yet (./capabilities.ts), so today this screen is the neutral not-found block
 *    for everyone — by design.
 *  - Remounted per committed context and route entry, and every query key carries the namespace: a stale render
 *    would put one company's audit trail under another company's label. A group (multi-company) context shows the
 *    no-company state rather than guessing a company.
 *  - The page shows only the minimal fields (./contract.ts DATA MINIMISATION); payloads, reason, IP and user agent
 *    never reach this component.
 *  - No paging policy: the server's default page size is used and only its opaque cursor is followed. A 400 (an
 *    expired cursor) offers the first page of the same filter instead of a generic error.
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { fetchAuditEvents } from './api'
import type { AuditEventPage, AuditEventRow } from './contract'
import {
  AUDIT_CAPABILITY,
  AUDIT_FILTER_MAX_LENGTH,
  auditEventsPath,
  hasAuditFilter,
  readAuditQuery,
  type AuditQuery,
} from './capabilities'
import {
  AUDIT_TEXT,
  actorTypeLabel,
  aiInvolvementLabel,
  formatInstant,
  outcomeLabel,
  versionLabel,
} from './auditText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import styles from './audit.module.css'

export function AuditEventsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <AuditScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function AuditScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const [searchParams] = useSearchParams()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const canRead = hasCapability(shell, AUDIT_CAPABILITY.eventsList)
  const ready = namespace !== null && singleCompany && canRead
  const query = readAuditQuery(searchParams)

  const events = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      shell.routeEntryGeneration,
      'audit-events',
      query.correlationId,
      query.entityType,
      query.entityId,
      query.cursor,
    ],
    enabled: ready,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchAuditEvents(companyId ?? '', query, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!singleCompany) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={AUDIT_TEXT.subject} />
      </Frame>
    )
  }

  if (!ready) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'not-found' }} subject={AUDIT_TEXT.subject} />
      </Frame>
    )
  }

  const outcome = events.data
  const invalidQuery = outcome?.type === 'http' && outcome.status === 400
  const state = (
    events.isPending ? { kind: 'initial-loading' } : classifyOutcome(outcome ?? { type: 'malformed' })
  ) as RemoteViewState<AuditEventPage>

  return (
    <Frame>
      <FilterForm key={filterKey(query)} query={query} />
      {invalidQuery ? (
        <div className={styles.notice} role="alert" data-state="invalid-query">
          <p className={styles.noticeTitle}>{AUDIT_TEXT.invalidQueryTitle}</p>
          <p className={styles.muted}>{AUDIT_TEXT.invalidQueryDetail}</p>
          <Link className={styles.link} to={auditEventsPath({ ...query, cursor: null })}>
            {AUDIT_TEXT.firstPage}
          </Link>
        </div>
      ) : state.kind !== 'ready' ? (
        <StateBlock state={state} subject={AUDIT_TEXT.subject} onRetry={() => void events.refetch()} />
      ) : state.data.items.length === 0 ? (
        <StateBlock
          state={{ kind: hasAuditFilter(query) ? 'empty-filtered' : 'empty-no-data' }}
          subject={AUDIT_TEXT.subject}
        />
      ) : (
        <EventList page={state.data} query={query} />
      )}
      <p className={styles.muted}>{AUDIT_TEXT.hiddenFieldsNote}</p>
    </Frame>
  )
}

function filterKey(query: AuditQuery): string {
  return JSON.stringify([query.correlationId, query.entityType, query.entityId])
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="audit-events-heading" className={styles.page}>
      <h1 id="audit-events-heading">{AUDIT_TEXT.heading}</h1>
      <p className={styles.muted}>{AUDIT_TEXT.lead}</p>
      {children}
    </section>
  )
}

type FilterName = 'correlationId' | 'entityType' | 'entityId'

const FILTERS: readonly { readonly name: FilterName; readonly label: string; readonly help?: string }[] = [
  { name: 'correlationId', label: AUDIT_TEXT.correlationLabel, help: AUDIT_TEXT.correlationHelp },
  { name: 'entityType', label: AUDIT_TEXT.entityTypeLabel, help: AUDIT_TEXT.entityTypeHelp },
  { name: 'entityId', label: AUDIT_TEXT.entityIdLabel },
]

/** The filter form. Applying it always starts again from the newest page: a cursor belongs to one filter only. */
function FilterForm({ query }: { readonly query: AuditQuery }) {
  const navigate = useNavigate()
  const [values, setValues] = useState<Record<FilterName, string>>({
    correlationId: query.correlationId ?? '',
    entityType: query.entityType ?? '',
    entityId: query.entityId ?? '',
  })
  const [submitted, setSubmitted] = useState(false)
  const tooLong = (name: FilterName) => values[name].trim().length > AUDIT_FILTER_MAX_LENGTH[name]
  const invalid = FILTERS.some((filter) => tooLong(filter.name))

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitted(true)
    if (invalid) return
    void navigate(
      auditEventsPath({
        correlationId: values.correlationId.trim() || null,
        entityType: values.entityType.trim() || null,
        entityId: values.entityId.trim() || null,
      }),
    )
  }

  return (
    <form className={styles.filters} onSubmit={submit} noValidate role="search" aria-label={AUDIT_TEXT.filtersLegend}>
      <div className={styles.filterFields}>
        {FILTERS.map((filter) => (
          <Field
            key={filter.name}
            label={filter.label}
            help={filter.help}
            error={submitted && tooLong(filter.name) ? AUDIT_TEXT.tooLong(AUDIT_FILTER_MAX_LENGTH[filter.name]) : undefined}
          >
            {(control) => (
              <input
                {...control}
                className={styles.input}
                type="text"
                name={filter.name}
                autoComplete="off"
                spellCheck={false}
                value={values[filter.name]}
                onChange={(event) => {
                  const next = event.target.value
                  setValues((current) => ({ ...current, [filter.name]: next }))
                }}
              />
            )}
          </Field>
        ))}
      </div>
      <div className={styles.actions}>
        <Button type="submit" variant="primary">
          {AUDIT_TEXT.apply}
        </Button>
        {hasAuditFilter(query) ? (
          <Link className={styles.link} to={auditEventsPath()}>
            {AUDIT_TEXT.clear}
          </Link>
        ) : null}
      </div>
    </form>
  )
}

function EventList({ page, query }: { readonly page: AuditEventPage; readonly query: AuditQuery }) {
  const columns: readonly DataTableColumn<AuditEventRow>[] = [
    {
      key: 'time',
      header: AUDIT_TEXT.columnTime,
      pin: 'start',
      cell: (row) => <time dateTime={row.occurredAtUtc}>{formatInstant(row.occurredAtUtc)}</time>,
    },
    { key: 'event', header: AUDIT_TEXT.columnEvent, cell: (row) => <code className={styles.code}>{row.eventType}</code> },
    {
      key: 'entity',
      header: AUDIT_TEXT.columnEntity,
      cell: (row) => (
        <>
          <code className={styles.code}>{row.entityType}</code>
          <div className={styles.id}>{row.entityId}</div>
          <Link
            className={styles.link}
            to={auditEventsPath({ entityType: row.entityType, entityId: row.entityId })}
            aria-label={`${AUDIT_TEXT.filterByEntity}: ${row.entityType} ${row.entityId}`}
          >
            {AUDIT_TEXT.filterByEntity}
          </Link>
        </>
      ),
    },
    { key: 'operation', header: AUDIT_TEXT.columnOperation, cell: (row) => row.operation },
    {
      key: 'version',
      header: AUDIT_TEXT.columnVersion,
      align: 'end',
      cell: (row) => versionLabel(row.entityVersionBefore, row.entityVersionAfter),
    },
    {
      key: 'actor',
      header: AUDIT_TEXT.columnActor,
      cell: (row) => (
        <>
          <span>{actorTypeLabel(row.actorType)}</span>
          <div className={styles.id}>{row.actorId}</div>
        </>
      ),
    },
    {
      key: 'permission',
      header: AUDIT_TEXT.columnPermission,
      cell: (row) => <code className={styles.code}>{row.permissionCode}</code>,
    },
    { key: 'channel', header: AUDIT_TEXT.columnChannel, cell: (row) => row.sourceChannel },
    { key: 'ai', header: AUDIT_TEXT.columnAi, cell: (row) => aiInvolvementLabel(row.aiInvolvement) },
    { key: 'outcome', header: AUDIT_TEXT.columnOutcome, cell: (row) => outcomeLabel(row.outcome) },
    {
      key: 'correlation',
      header: AUDIT_TEXT.columnCorrelation,
      cell: (row) => (
        <>
          <div className={styles.id}>{row.correlationId}</div>
          {query.correlationId === row.correlationId ? null : (
            <Link
              className={styles.link}
              to={auditEventsPath({ correlationId: row.correlationId })}
              aria-label={`${AUDIT_TEXT.filterByCorrelation}: ${row.correlationId}`}
            >
              {AUDIT_TEXT.filterByCorrelation}
            </Link>
          )}
        </>
      ),
    },
  ]

  return (
    <>
      <DataTable
        label={AUDIT_TEXT.tableLabel}
        columns={columns}
        rows={page.items}
        rowKey={(row) => row.id}
        rowAttributes={(row) => ({ 'data-audit-event': row.id })}
      />
      <nav className={styles.pager} aria-label={AUDIT_TEXT.pagerLabel}>
        {query.cursor === null ? null : (
          <Link className={styles.link} to={auditEventsPath({ ...query, cursor: null })}>
            {AUDIT_TEXT.newer}
          </Link>
        )}
        {page.nextCursor === null ? (
          <span className={styles.muted}>{AUDIT_TEXT.noOlder}</span>
        ) : (
          <Link className={styles.link} to={auditEventsPath({ ...query, cursor: page.nextCursor })}>
            {AUDIT_TEXT.older}
          </Link>
        )}
      </nav>
    </>
  )
}
