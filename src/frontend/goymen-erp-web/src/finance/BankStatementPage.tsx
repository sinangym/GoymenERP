import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { documentsQueryKey, fetchDocuments } from '../documents/api'
import { OWNER_KIND } from '../documents/contract'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { FINANCE_CAPABILITY } from './capabilities'
import { fetchCreditBanks } from './creditMovementApi'
import { formatDecimalTextTr } from './decimalText'
import { useFinanceBinding } from './financePort'
import { confirmStatementMatch, fetchStatementDetail, fetchStatementImports, parseStatement, reserveStatement,
  reverseStatementMatch, type Reply } from './BankStatementHttp'
import type { StatementFormat, StatementIssue, StatementLine } from './BankStatementContract'
import { BANK_STATEMENT_TEXT as T } from './BankStatementText'
import styles from './Finance.module.css'

function ReadStatus<TValue>({ outcome, loading, onRetry }: { readonly outcome?: ViewOutcome<TValue>; readonly loading: boolean; readonly onRetry?: () => void }) {
  if (loading) return <StateBlock subject={T.heading} state={{ kind: 'initial-loading' }} />
  if (outcome === undefined || outcome.type === 'ok' || outcome.type === 'ok-single') return null
  return <StateBlock subject={T.heading} state={classifyOutcome(outcome)} onRetry={onRetry} />
}

/** One command at a time; an unknown outcome keeps the SAME key and body for the retry (never a new key). */
interface Pending { readonly key: string; readonly send: (key: string) => Promise<Reply<{ readonly outcome: string; readonly code: string | null }>>; readonly done: (result: unknown) => void }

export function BankStatementPage() {
  const bound = useFinanceBinding()
  const shell = useShellState()
  if (!hasCapability(shell, FINANCE_CAPABILITY.bankStatements)) return <StateBlock subject={T.heading} state={{ kind: 'forbidden' }} />
  if (bound === null) return <StateBlock subject={T.heading} state={{ kind: 'no-authorized-company' }} />
  return <StatementScreen key={`${bound.namespace}:${shell.routeEntryGeneration}`} namespace={bound.namespace}
    companyId={bound.binding.companyId} entry={shell.routeEntryGeneration}
    canImport={hasCapability(shell, FINANCE_CAPABILITY.bankStatementsImport)}
    canMatch={hasCapability(shell, FINANCE_CAPABILITY.bankStatementsMatch)} />
}

function StatementScreen({ namespace, companyId, entry, canImport, canMatch }: {
  readonly namespace: string; readonly companyId: string; readonly entry: number; readonly canImport: boolean; readonly canMatch: boolean
}) {
  const boundary = useAuthBoundary()
  const client = useQueryClient()
  const mounted = useRef(true)
  useEffect(() => () => { mounted.current = false }, [])
  const [selected, setSelected] = useState<string | null>(null)
  const [bankAccountId, setBankAccountId] = useState('')
  const [format, setFormat] = useState<StatementFormat>('Csv')
  const [bankKey, setBankKey] = useState('')
  const [documentId, setDocumentId] = useState('')
  const [reversing, setReversing] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [pending, setPending] = useState<Pending | null>(null)
  const [unknown, setUnknown] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [issues, setIssues] = useState<readonly StatementIssue[]>([])

  const guarded = useCallback(async <TValue,>(action: () => Promise<ViewOutcome<TValue>>) => {
    const fence = boundary.capture()
    const answer = await action()
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
    return answer
  }, [boundary])
  const list = useQuery({ queryKey: [namespace, 'finance', 'bank-statements', entry],
    queryFn: ({ signal }) => guarded(() => fetchStatementImports(companyId, signal)) })
  const detail = useQuery({ queryKey: [namespace, 'finance', 'bank-statement', entry, selected], enabled: selected !== null,
    queryFn: ({ signal }) => guarded(() => fetchStatementDetail(companyId, selected!, signal)) })
  const banks = useQuery({ queryKey: [namespace, 'finance', 'bank-statement-banks', entry], enabled: canImport,
    queryFn: ({ signal }) => guarded(() => fetchCreditBanks(companyId, signal)) })
  const current = !detail.isFetching && detail.data?.type === 'ok-single' ? detail.data.data : null
  const documents = useQuery({
    queryKey: documentsQueryKey(namespace, entry, OWNER_KIND.financeBankStatementImport, selected ?? ''),
    enabled: canImport && current?.import.state === 'Reserved',
    queryFn: ({ signal }) => guarded(() => fetchDocuments(OWNER_KIND.financeBankStatementImport, selected!, companyId, signal)),
  })
  const files = !documents.isFetching && documents.data?.type === 'ok-single' ? documents.data.data.items : []
  const approvedBanks = banks.data?.type === 'ok-single'
    ? banks.data.data.items.filter((item) => item.account.isActive).map((item) => item.account) : []

  const refresh = () => void client.invalidateQueries({ queryKey: [namespace, 'finance'] })
  const run = async (value: Pending) => {
    const fence = boundary.capture()
    const reply = await value.send(value.key)
    if (!mounted.current) return
    if (reply.kind === 'refused' && (reply.status === 401 || reply.status === 403)) boundary.report(reply.status, fence)
    if (reply.kind === 'unknown') { setUnknown(true); setMessage(T.unknown); return }
    setPending(null); setUnknown(false)
    if (reply.kind === 'answered' && reply.result.outcome === 'Succeeded') value.done(reply.result)
    else if (reply.kind === 'answered' && reply.result.outcome === 'VersionConflict' && reply.result.code === null) setMessage(T.conflict)
    else if (reply.kind === 'answered' && reply.result.code !== null) setMessage(T.codes[reply.result.code] ?? T.refused)
    else setMessage(T.refused)
    refresh()
  }
  const start = (send: Pending['send'], done: Pending['done']) => {
    if (pending !== null) return
    const value: Pending = { key: crypto.randomUUID(), send, done }
    setPending(value); setMessage(null); setIssues([])
    void run(value)
  }
  const reserve = () => {
    if (bankAccountId === '' || (format === 'Csv' && bankKey.trim() === '')) return
    const importId = crypto.randomUUID()
    const request = { importId, bankAccountId, format, bankKey: format === 'Csv' ? bankKey.trim() : null }
    start((key) => reserveStatement(companyId, key, request), () => { setSelected(importId); setMessage(T.reserved) })
  }
  const parse = () => {
    const file = files.find((item) => item.id === documentId)
    if (current === null || file === undefined) return
    const request = { expectedVersion: current.import.version, documentId: file.id, documentVersion: file.versionNo }
    const importId = current.import.id
    start(async (key) => {
      const reply = await parseStatement(companyId, importId, key, request)
      if (reply.kind === 'answered' && reply.result.issues.length > 0) setIssues(reply.result.issues)
      return reply
    }, () => setMessage(T.parsed))
  }
  const confirm = (line: StatementLine) => {
    if (current === null || line.suggestion.candidate === null) return
    const request = { matchId: crypto.randomUUID(), lineId: line.id, kind: line.suggestion.candidate.kind, recordId: line.suggestion.candidate.recordId }
    const importId = current.import.id
    start((key) => confirmStatementMatch(companyId, importId, key, request), () => setMessage(T.confirmed))
  }
  const reverse = (line: StatementLine) => {
    const match = line.suggestion.match
    if (match === null) return
    if (reason.trim() === '') { setMessage(T.reasonRequired); return }
    const request = { expectedVersion: match.version, reversalId: crypto.randomUUID(), reason: reason.trim() }
    start((key) => reverseStatementMatch(companyId, match.id, key, request), () => { setReversing(null); setReason(''); setMessage(T.reversed) })
  }

  const rows = list.data?.type === 'ok-single' ? list.data.data : null
  return <section className={styles.panel} aria-labelledby="bank-statement-heading">
    <h2 id="bank-statement-heading">{T.heading}</h2>
    <p>{T.explanation}</p>
    <ReadStatus outcome={list.data} loading={list.isFetching && list.data === undefined} onRetry={() => void list.refetch()} />
    {rows === null ? null : rows.length === 0 ? <p>{T.none}</p> : <table>
      <caption>{T.imports}</caption>
      <thead><tr><th scope="col">{T.period}</th><th scope="col">{T.format}</th><th scope="col">{T.lines}</th><th scope="col">{T.duplicates}</th>
        <th scope="col"><span className="visually-hidden">{T.open}</span></th></tr></thead>
      <tbody>{rows.map((row) => <tr key={row.id} aria-current={row.id === selected ? 'true' : undefined}>
        <td>{row.firstTransactionOn === null ? T.states[row.state] : `${row.firstTransactionOn} – ${row.lastTransactionOn}`}</td>
        <td>{T.formats[row.format]}</td><td>{row.lineCount}</td><td>{row.duplicateLineCount}</td>
        <td><button type="button" onClick={() => { setSelected(row.id); setMessage(null); setIssues([]) }}>{T.open}</button></td>
      </tr>)}</tbody>
    </table>}

    {canImport ? <fieldset disabled={pending !== null}>
      <legend>{T.newImport}</legend>
      <ReadStatus outcome={banks.data} loading={banks.isFetching && banks.data === undefined} />
      <label>{T.bankAccount}<select value={bankAccountId} onChange={(event) => setBankAccountId(event.target.value)}>
        <option value="">{T.choose}</option>{approvedBanks.map((bank) => <option key={bank.id} value={bank.id}>{bank.displayName}</option>)}
      </select></label>
      <label>{T.format}<select value={format} onChange={(event) => setFormat(event.target.value as StatementFormat)}>
        {(['Csv', 'Mt940', 'Xlsx'] as const).map((value) => <option key={value} value={value}>{T.formats[value]}</option>)}
      </select></label>
      {format === 'Csv' ? <label>{T.bankKey}<input value={bankKey} maxLength={32} onChange={(event) => setBankKey(event.target.value.toUpperCase())} /></label> : null}
      <button type="button" disabled={bankAccountId === '' || (format === 'Csv' && bankKey.trim() === '')} onClick={reserve}>{T.reserve}</button>
    </fieldset> : null}

    {selected === null ? null : <ReadStatus outcome={detail.data} loading={detail.isFetching} onRetry={() => void detail.refetch()} />}
    {current === null ? null : <>
      <p>{T.states[current.import.state]} — {T.lineCount}: {current.import.lineCount}, {T.duplicates}: {current.import.duplicateLineCount}</p>
      {current.import.state === 'Reserved' && canImport ? <>
        <DocumentsPanel ownerKind={OWNER_KIND.financeBankStatementImport} ownerId={current.import.id} companyId={companyId}
          namespace={namespace} entryGeneration={entry} heading={T.upload} />
        <label>{T.document}<select value={documentId} disabled={pending !== null} onChange={(event) => setDocumentId(event.target.value)}>
          <option value="">{T.choose}</option>{files.map((file) => <option key={file.id} value={file.id}>{file.fileName} — v{file.versionNo}</option>)}
        </select></label>
        <button type="button" disabled={pending !== null || documentId === ''} onClick={parse}>{T.parse}</button>
      </> : null}
      {current.lines.length === 0 ? null : <table>
        <caption>{T.lines}</caption>
        <thead><tr><th scope="col">{T.date}</th><th scope="col">{T.direction}</th><th scope="col">{T.amount}</th>
          <th scope="col">{T.description}</th><th scope="col">{T.counterparty}</th><th scope="col">{T.suggestion}</th></tr></thead>
        <tbody>{current.lines.map((line) => <tr key={line.id} data-match-status={line.suggestion.status}>
          <td>{line.transactionOn}</td><td>{T.directions[line.direction]}</td>
          <td>{formatDecimalTextTr(line.amount) ?? line.amount}{line.currencyText === null ? '' : ` ${line.currencyText}`}</td>
          <td>{line.description}</td><td>{line.counterpartyIban ?? '—'}</td>
          <td>
            <strong>{T.statuses[line.suggestion.status]}</strong>
            {line.suggestion.manualReason === null ? null : <span> — {T.reasons[line.suggestion.manualReason]}</span>}
            {line.suggestion.candidate === null ? null : <span> — {T.kinds[line.suggestion.candidate.kind]} {line.suggestion.candidate.occurredOn}
              ({T.checks[line.suggestion.candidate.counterpartyCheck]})</span>}
            {canMatch && line.suggestion.status === 'Suggested'
              ? <button type="button" disabled={pending !== null} onClick={() => confirm(line)}>{T.confirm}</button> : null}
            {canMatch && line.suggestion.status === 'Confirmed' && reversing !== line.id
              ? <button type="button" disabled={pending !== null} onClick={() => { setReversing(line.id); setReason('') }}>{T.reverse}</button> : null}
            {reversing === line.id ? <span>
              <label>{T.reverseReason}<textarea value={reason} maxLength={1000} disabled={pending !== null}
                onChange={(event) => setReason(event.target.value)} /></label>
              <button type="button" disabled={pending !== null} onClick={() => reverse(line)}>{T.reverse}</button>
              <button type="button" disabled={pending !== null} onClick={() => setReversing(null)}>{T.cancel}</button>
            </span> : null}
          </td>
        </tr>)}</tbody>
      </table>}
    </>}
    {issues.length === 0 ? null : <div role="alert"><p>{T.issues}</p><ul>{issues.map((issue, index) =>
      <li key={index}>{issue.lineNumber}: {issue.field} — {issue.code}</li>)}</ul></div>}
    {unknown && pending !== null ? <button type="button" onClick={() => void run(pending)}>{T.retry}</button> : null}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
