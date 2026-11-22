import { useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { useReportCatalog } from './useReportCatalog'
import { createSavedView, exportReport, fetchSavedViews, passivateSavedView, runReport, updateSavedView } from './reportingApi'
import { REPORTING_ROUTE, type ReportColumn, type ReportDefinition, type ReportTable, type SavedView } from './reportingContract'
import { namedProblem, refusalText } from './reportingProblems'
import { REPORTING_TEXT as T } from './reportingText'
import styles from './Reporting.module.css'

const PAGE_SIZE = 50

export function ReportRunPage() {
  const shell = useShellState()
  const context = contextKey(shell)
  const { reportId = '' } = useParams()
  return <ReportRunBody key={`${context === null ? 'no-context' : contextKeyString(context)}:${reportId}`} reportId={reportId} />
}

function ReportRunBody({ reportId }: { readonly reportId: string }) {
  const { shell, company, query, catalog } = useReportCatalog()
  if (capabilityFailClosed(shell) || company === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
  if (catalog === null) {
    return <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>}
      subject={T.subject} onRetry={() => void query.refetch()} />
  }
  // A report the actor may not open answers like an unknown one (non-disclosing).
  const definition = catalog.reports.find((report) => report.id === reportId)
  if (definition === undefined) return <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
  return <ReportScreen company={company} definition={definition} />
}

function ReportScreen({ company, definition }: { readonly company: string; readonly definition: ReportDefinition }) {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const boundary = useAuthBoundary()
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [submitted, setSubmitted] = useState<Readonly<Record<string, string>> | null>(null)
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<readonly string[]>(definition.columns.map((column) => column.id))
  const [message, setMessage] = useState<string | null>(null)
  const title = T.reports[definition.id] ?? definition.id

  const run = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'reporting-run', definition.id, submitted, page],
    enabled: submitted !== null,
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await runReport(company, definition.id, definition.columns, submitted!, page, PAGE_SIZE, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    },
  })
  const table = run.data?.type === 'ok-single' ? run.data.data : null
  const problem = namedProblem(run.data)

  async function download(format: 'csv' | 'xlsx') {
    if (submitted === null) return
    setMessage(T.exporting)
    const outcome = await exportReport(company, definition.id, format, submitted, page, PAGE_SIZE, selected)
    setMessage(outcome.type === 'saved' ? T.exported : outcome.type === 'refused' ? refusalText(outcome.status, outcome.code) : T.exportFailed)
  }

  return <section className={styles.page} aria-labelledby="report-heading">
    <Link to={REPORTING_ROUTE.center}>{T.back}</Link>
    <h1 id="report-heading">{title}</h1>
    <form className={styles.form} aria-label={T.parameters} onSubmit={(event) => { event.preventDefault(); setPage(1); setSubmitted({ ...draft }) }}>
      {definition.parameters.map((parameter) => <label key={parameter.id}>
        <span>{T.parameterLabels[parameter.id] ?? parameter.id}{parameter.required ? ` (${T.required})` : ''}</span>
        {parameter.kind === 'choice'
          ? <select value={draft[parameter.id] ?? ''} onChange={(event) => setDraft({ ...draft, [parameter.id]: event.target.value })}>
            <option value="">—</option>
            {parameter.choices.map((choice) => <option key={choice} value={choice}>{T.choiceLabels[choice] ?? choice}</option>)}
          </select>
          : <input type={parameter.kind === 'date' ? 'date' : 'text'} inputMode={parameter.kind === 'whole-number' || parameter.kind === 'decimal' ? 'decimal' : undefined}
            value={draft[parameter.id] ?? ''} onChange={(event) => setDraft({ ...draft, [parameter.id]: event.target.value })} />}
      </label>)}
      <Button type="submit">{T.run}</Button>
    </form>
    <fieldset className={styles.columns}>
      <legend>{T.columns}</legend>
      {definition.columns.map((column) => <label key={column.id}>
        <input type="checkbox" checked={selected.includes(column.id)} onChange={(event) => setSelected(event.target.checked
          ? definition.columns.map((item) => item.id).filter((id) => id === column.id || selected.includes(id))
          : selected.filter((id) => id !== column.id || selected.length === 1))} />
        {T.columnLabels[column.id] ?? column.id}
      </label>)}
    </fieldset>
    <SavedViews company={company} definition={definition} parameters={draft} columns={selected}
      onApply={(view) => { setDraft({ ...view.parameters }); setSelected(view.columns) }} />
    {submitted === null ? null
      : problem !== null ? <p role="alert" className={styles.problem}>{problem}</p>
        : table === null
          ? <StateBlock state={run.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(run.data) as RemoteViewState<unknown>}
            subject={T.subject} onRetry={() => void run.refetch()} />
          : <ReportResult table={table} definition={definition} selected={selected} page={page} onPage={setPage}
            onExport={definition.canExport ? download : null} />}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}

function Cell({ column, value }: { readonly column: ReportColumn; readonly value: string | null }) {
  if (value === null) return <td className={styles.note}>{T.unknownCell}</td>
  if (column.kind === 'flag') return <td>{value === 'true' ? T.flagTrue : T.flagFalse}</td>
  if (column.kind === 'instant') return <td><time dateTime={value}>{value}</time></td>
  return <td className={column.kind === 'number' ? styles.number : undefined}>{value}</td>
}

function summaryLabel(id: string) {
  return id.startsWith('bucketKg:') ? T.bucketPrefix + id.slice('bucketKg:'.length) : T.summaryLabels[id] ?? id
}

function ReportResult({ table, definition, selected, page, onPage, onExport }: {
  readonly table: ReportTable; readonly definition: ReportDefinition; readonly selected: readonly string[]; readonly page: number
  readonly onPage: (page: number) => void; readonly onExport: ((format: 'csv' | 'xlsx') => void) | null
}) {
  const shown = definition.columns.map((column, index) => ({ column, index })).filter(({ column }) => selected.includes(column.id))
  const title = T.reports[definition.id] ?? definition.id
  return <>
    {table.rows.length === 0 ? <StateBlock state={{ kind: 'empty-filtered' }} subject={T.subject} />
      : <div className={styles.tableWrap} role="region" aria-label={title} tabIndex={0}>
        <table className={styles.table}>
          <caption>{title} — {T.page} {table.page}</caption>
          <thead><tr>{shown.map(({ column }) => <th key={column.id} scope="col">{T.columnLabels[column.id] ?? column.id}</th>)}</tr></thead>
          <tbody>{table.rows.map((row, rowIndex) => <tr key={rowIndex}>{shown.map(({ column, index }) => <Cell key={column.id} column={column} value={row[index]} />)}</tr>)}</tbody>
        </table>
      </div>}
    <div className={styles.actions}>
      <Button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>{T.previous}</Button>
      <Button type="button" disabled={!table.hasMore} onClick={() => onPage(page + 1)}>{T.next}</Button>
      {onExport === null ? null : <>
        <Button type="button" onClick={() => onExport('csv')}>{T.exportCsv}</Button>
        <Button type="button" onClick={() => onExport('xlsx')}>{T.exportXlsx}</Button>
        <span className={styles.note}>{T.exportNote}</span>
      </>}
    </div>
    <h2>{T.summary}</h2>
    <p className={styles.note}>{T.pageOnly}</p>
    <dl>{table.summary.map((item) => <div key={item.id}><dt>{summaryLabel(item.id)}</dt><dd>{item.value}</dd></div>)}</dl>
    {table.sourceStatuses.length === 0 ? null : <><h2>{T.sourceStatus}</h2>
      <dl>{table.sourceStatuses.map((item) => <div key={item.id}><dt>{summaryLabel(item.id)}</dt><dd>{item.value}</dd></div>)}</dl></>}
    <p className={styles.note}>{T.generatedAt}: <time dateTime={table.generatedAtUtc}>{table.generatedAtUtc}</time></p>
  </>
}

function SavedViews({ company, definition, parameters, columns, onApply }: {
  readonly company: string; readonly definition: ReportDefinition; readonly parameters: Readonly<Record<string, string>>
  readonly columns: readonly string[]; readonly onApply: (view: SavedView) => void
}) {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const boundary = useAuthBoundary()
  const client = useQueryClient()
  const [name, setName] = useState('')
  const [chosen, setChosen] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  // The key of an attempt whose outcome is unknown is kept for its retry, so a retry can only replay (never save twice).
  const pending = useRef<{ readonly action: string; readonly key: string } | null>(null)
  const queryKey = [namespace, shell.routeEntryGeneration, 'reporting-saved-views', definition.id]
  const list = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchSavedViews(company, definition.id, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    },
  })
  const views = list.data?.type === 'ok-single' ? list.data.data.items : null
  const chosenView = views?.find((view) => view.id === chosen) ?? null

  function keyFor(action: string) {
    if (pending.current?.action === action) return pending.current.key
    const key = crypto.randomUUID()
    pending.current = { action, key }
    return key
  }

  async function command(action: string, run: (key: string) => Promise<Awaited<ReturnType<typeof createSavedView>>>, done: string) {
    const outcome = await run(keyFor(action))
    if (outcome.type === 'unknown') {
      setMessage(T.viewUnknown)
      return
    }
    pending.current = null
    if (outcome.type === 'refused') {
      setMessage(refusalText(outcome.status, outcome.code))
      return
    }
    setMessage(done)
    await client.invalidateQueries({ queryKey })
  }

  const body = (expectedVersion: string | null) => ({ reportId: definition.id, name: name.trim(), parameters, columns, expectedVersion })
  return <section aria-labelledby="saved-views-heading">
    <h2 id="saved-views-heading">{T.savedViews}</h2>
    {views === null
      ? <StateBlock state={list.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(list.data) as RemoteViewState<unknown>}
        subject={T.savedViews} onRetry={() => void list.refetch()} />
      : views.length === 0 ? <p className={styles.note}>{T.savedViewsNone}</p>
        : <ul className={styles.list}>{views.map((view) => <li key={view.id} className={styles.actions}>
          <label><input type="radio" name="saved-view" checked={chosen === view.id} onChange={() => { setChosen(view.id); setName(view.name) }} />{view.name}</label>
          <Button type="button" onClick={() => onApply(view)}>{T.applyView}</Button>
          <Button type="button" onClick={() => void command(`passivate:${view.id}:${view.version}`,
            (key) => passivateSavedView({ companyId: company, idempotencyKey: key }, view.id, definition.id, view.version), T.viewPassivated)}>{T.passivateView}</Button>
        </li>)}</ul>}
    <div className={styles.actions}>
      <label>{T.savedViewName}<input value={name} maxLength={80} onChange={(event) => setName(event.target.value)} /></label>
      <Button type="button" disabled={name.trim().length === 0} onClick={() => void command(`create:${name.trim()}`,
        (key) => createSavedView({ companyId: company, idempotencyKey: key }, body(null)), T.viewSaved)}>{T.saveView}</Button>
      {chosenView === null ? null : <Button type="button" disabled={name.trim().length === 0} onClick={() => void command(`update:${chosenView.id}:${chosenView.version}`,
        (key) => updateSavedView({ companyId: company, idempotencyKey: key }, chosenView.id, body(chosenView.version)), T.viewSaved)}>{T.updateView}</Button>}
    </div>
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
