import { useLayoutEffect, useRef, useState, useSyncExternalStore, type ChangeEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../../shell/shellState'
import { StateBlock } from '../../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../../shell/uiState'
import { useAuthBoundary } from '../../shell/useAuthBoundary'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { fetchPaintCandidates, fetchPaintCatalog, sendPaintCommand, validatePaintFile } from './api'
import { DEFAULT_PAINT_COLUMN, PAINT_LIMITS, PAINT_MEMBERSHIP_CAPABILITY,
  PAINT_CANDIDATE_QUERY_LIMIT, type PaintCandidate, type PaintCandidates, type PaintCommand, type PaintEntry, type PaintPreview, type PaintPreviewRow } from './contract'
import { beginPaintAttempt, finishPaintAttempt, leavePaintAttempt, paintAttempt, paintAttemptFrozen,
  paintBinding, subscribePaintAttempt } from './paintAttempt'
import { freezePaintFile, validPaintFile, type FrozenPaintFile } from './paintFile'
import { PAINT_TEXT as T } from './paintMembershipText'
import styles from './PaintMembershipPage.module.css'

/** The key remounts every local file, preview and read when the committed authority/context or route entry changes. */
export function PaintMembershipPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (key === null || companyId === null) return <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
  if (!hasCapability(shell, PAINT_MEMBERSHIP_CAPABILITY)) return <StateBlock subject={T.subject} state={{ kind: 'forbidden' }} />
  const namespace = contextKeyString(key)
  return <PaintMembershipEditor key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration])}
    shell={shell} companyId={companyId} namespace={namespace} />
}

function PaintMembershipEditor({ shell, companyId, namespace }: {
  readonly shell: ShellState; readonly companyId: string; readonly namespace: string
}) {
  const boundary = useAuthBoundary()
  const binding = paintBinding(shell)
  const attempt = useSyncExternalStore((listener) => subscribePaintAttempt(binding, listener),
    () => paintAttempt(binding), () => paintAttempt(null))
  const frozen = paintAttemptFrozen(attempt)
  const alive = useRef(false)
  const fileGeneration = useRef(0)
  const validationAbort = useRef<AbortController | null>(null)
  const candidateGeneration = useRef(0)
  const candidateAbort = useRef<AbortController | null>(null)
  useLayoutEffect(() => {
    alive.current = true
    return () => {
      alive.current = false
      fileGeneration.current += 1
      validationAbort.current?.abort()
      candidateGeneration.current += 1
      candidateAbort.current?.abort()
      leavePaintAttempt(binding)
    }
  }, [binding])
  const [file, setFile] = useState<FrozenPaintFile | null>(null)
  const [reading, setReading] = useState(false)
  const [column, setColumn] = useState<string>(DEFAULT_PAINT_COLUMN)
  const [sourceLabel, setSourceLabel] = useState('')
  const [reason, setReason] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)
  const [previewOutcome, setPreviewOutcome] = useState<ViewOutcome<PaintPreview> | null>(null)
  const [candidateText, setCandidateText] = useState('')
  const [candidateOutcome, setCandidateOutcome] = useState<ViewOutcome<PaintCandidates> | null>(null)
  const query = useQuery({
    queryKey: [namespace, 'paint-memberships', shell.routeEntryGeneration, 'current'],
    retry: false,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const result = await fetchPaintCatalog(companyId, signal)
      if (result.type === 'http') boundary.report(result.status, security)
      return result
    },
  })
  const catalog = query.data?.type === 'ok-single' && query.data.data.outcome !== 3 ? query.data.data : null
  const preview = previewOutcome?.type === 'ok-single' ? previewOutcome.data : null
  const previewCurrent = preview !== null && catalog !== null && preview.version === catalog.version
  const candidates = candidateOutcome?.type === 'ok-single' ? candidateOutcome.data : null
  const candidatesCurrent = candidates !== null && catalog?.outcome === 1 && candidates.outcome === 1 && candidates.version === catalog.version
  const busy = frozen || reading || previewOutcome?.type === 'loading' || candidateOutcome?.type === 'loading'
  const canEdit = catalog?.outcome === 1 && !query.isFetching && !busy

  function invalidatePreview() {
    fileGeneration.current += 1
    validationAbort.current?.abort()
    setPreviewOutcome(null)
    setLocalError(null)
  }
  function invalidateCandidates() {
    candidateGeneration.current += 1
    candidateAbort.current?.abort()
    setCandidateOutcome(null)
  }
  async function searchCandidates() {
    if (!canEdit) return
    if (candidateText.length > PAINT_CANDIDATE_QUERY_LIMIT || /[\u0000-\u001f\u007f-\u009f]/.test(candidateText)) {
      setLocalError(T.candidateQueryInvalid); return
    }
    invalidateCandidates()
    setLocalError(null)
    const generation = candidateGeneration.current
    const controller = new AbortController()
    candidateAbort.current = controller
    const security = boundary.capture()
    setCandidateOutcome({ type: 'loading' })
    const result = await fetchPaintCandidates(companyId, candidateText.trim() || null, controller.signal)
    if (result.type === 'http') boundary.report(result.status, security)
    if (alive.current && generation === candidateGeneration.current) setCandidateOutcome(result)
  }
  async function chooseFile(event: ChangeEvent<HTMLInputElement>) {
    if (frozen) return
    invalidatePreview()
    setFile(null)
    setReading(false)
    const selected = event.currentTarget.files?.[0]
    if (selected === undefined) return
    if (!validPaintFile(selected)) { setLocalError(T.fileInvalid); return }
    const generation = fileGeneration.current
    setReading(true)
    try {
      const snapshot = await freezePaintFile(selected)
      if (alive.current && generation === fileGeneration.current) setFile(snapshot)
    } catch {
      if (alive.current && generation === fileGeneration.current) setLocalError(T.fileUnreadable)
    } finally {
      if (alive.current && generation === fileGeneration.current) setReading(false)
    }
  }
  async function validate() {
    if (busy || file === null) return
    const mappedColumn = column.trim()
    if (mappedColumn === '' || mappedColumn.length > PAINT_LIMITS.column) { setLocalError(T.columnRequired); return }
    invalidatePreview()
    const generation = fileGeneration.current
    const controller = new AbortController()
    validationAbort.current = controller
    const security = boundary.capture()
    setPreviewOutcome({ type: 'loading' })
    const result = await validatePaintFile(companyId,
      { fileName: file.fileName, contentBase64: file.contentBase64, productCodeColumn: mappedColumn }, file.sha256, controller.signal)
    if (result.type === 'http') boundary.report(result.status, security)
    if (alive.current && generation === fileGeneration.current) setPreviewOutcome(result)
  }
  async function send(command: PaintCommand) {
    const snapshot = beginPaintAttempt(binding, companyId, command)
    if (snapshot === null) return
    setLocalError(null)
    const security = boundary.capture()
    const result = await sendPaintCommand(snapshot.companyId, snapshot.command, snapshot.key)
    if (result.type === 'refused') boundary.report(result.status, security)
    finishPaintAttempt(snapshot, result)
    if (!alive.current) return
    if (result.type === 'applied') {
      invalidatePreview()
      invalidateCandidates()
      void query.refetch()
    } else if (result.type === 'refused' && (result.status === 409 || result.status === 404)
      && !paintAttemptFrozen(paintAttempt(binding))) {
      invalidatePreview()
      invalidateCandidates()
      void query.refetch()
    }
  }
  function applyImport() {
    if (busy || query.isFetching || !preview?.ready || !previewCurrent || file === null) return
    if (sourceLabel.trim() === '' || reason.trim() === '') { setLocalError(T.metadataRequired); return }
    void send({ kind: 'import', expectedProductIds: preview.rows.filter((row) => !row.alreadyMember).map((row) => row.productId), payload: {
      expectedVersion: preview.version, fileName: file.fileName, contentBase64: file.contentBase64,
      productCodeColumn: preview.productCodeColumn, fileSha256: preview.fileSha256, previewToken: preview.previewToken,
      sourceLabel: sourceLabel.trim(), reason: reason.trim(),
    } })
  }
  function setMembership(row: PaintEntry | PaintPreviewRow | PaintCandidate, isMember: boolean, source: 'preview' | 'candidate' = 'preview') {
    if (!canEdit || catalog === null || (isMember && (source === 'candidate' ? !candidatesCurrent : !preview?.ready || !previewCurrent))) return
    if (reason.trim() === '') { setLocalError(T.reasonRequired); return }
    void send({ kind: 'set', productId: row.productId, payload: {
      expectedVersion: catalog.version, expectedProductVersion: row.productVersion,
      expectedMembershipVersion: row.membershipVersion, isMember, reason: reason.trim(),
    } })
  }
  const readState = query.isFetching ? { kind: 'initial-loading' as const }
    : query.data?.type === 'ok-single' && query.data.data.outcome === 3 ? { kind: 'service-unavailable' as const }
      : classifyOutcome(query.data ?? { type: 'loading' })

  return <section className={styles.page} aria-labelledby="paint-membership-heading">
    <header className={styles.header}>
      <div><h1 id="paint-membership-heading">{T.heading}</h1><p>{T.description}</p></div>
      <Button type="button" onClick={() => void query.refetch()} disabled={query.isFetching}>{T.refresh}</Button>
    </header>
    {attempt.phase === 'sending' ? <p role="status">{T.sending}</p> : null}
    {attempt.phase === 'completed' ? <p role="status" className={styles.success}>{attempt.result.isReplay ? T.replayed : T.succeeded}</p> : null}
    {attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolved) ? <div className={styles.notice}>
      <p role="alert">{T.unknown}</p>
      <StateBlock subject={T.subject} state={attempt.phase === 'unknown' ? { kind: attempt.reason } : classifyOutcome({ type: 'http', status: attempt.status })} />
      {attempt.phase === 'failed' ? <p>{T.unknownRefusal}</p> : null}
      <Button type="button" onClick={() => void send(attempt.snapshot.command)}>{T.retry}</Button>
    </div> : null}
    {attempt.phase === 'failed' && !attempt.unresolved ? (
      attempt.status === 400 || attempt.status === 422
        ? <p role="alert" className={styles.error}>{T.refused}</p>
        : <StateBlock subject={T.subject} state={classifyOutcome({ type: 'http', status: attempt.status })} />
    ) : null}
    {localError === null ? null : <p role="alert" className={styles.error}>{localError}</p>}
    {catalog === null || query.isFetching ? <StateBlock subject={T.subject} state={readState} onRetry={() => void query.refetch()} /> : <>
      {catalog.outcome === 2 ? <p role="status" className={styles.notice}>{T.noSource}</p> : null}
      <section className={styles.section} aria-labelledby="paint-import-heading">
        <h2 id="paint-import-heading">{T.importHeading}</h2>
        <p>{T.importHelp}</p><p>{T.absentUnchanged}</p>
        <fieldset disabled={frozen} className={styles.fields}>
          <Field label={T.file}>{(control) => <input {...control} type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            onChange={(event) => void chooseFile(event)} />}</Field>
          <Field label={T.column}>{(control) => <input {...control} type="text" value={column} maxLength={PAINT_LIMITS.column} disabled={reading}
            onChange={(event) => { invalidatePreview(); setColumn(event.currentTarget.value) }} />}</Field>
          <Field label={T.sourceLabel}>{(control) => <input {...control} value={sourceLabel} maxLength={PAINT_LIMITS.sourceLabel}
            onChange={(event) => setSourceLabel(event.currentTarget.value)} />}</Field>
          <Field label={T.reason}>{(control) => <textarea {...control} value={reason} maxLength={PAINT_LIMITS.reason} rows={3}
            onChange={(event) => setReason(event.currentTarget.value)} />}</Field>
        </fieldset>
        {file === null ? <p>{T.selectFile}</p> : <dl className={styles.fileFacts}><dt>{T.fileName}</dt><dd>{file.fileName}</dd>
          <dt>{T.hash}</dt><dd className={styles.hash}>{file.sha256}</dd></dl>}
        <div className={styles.actions}>
          <Button type="button" onClick={() => void validate()} disabled={busy || file === null}>{previewOutcome?.type === 'loading' ? T.validating : T.validate}</Button>
          <Button type="button" variant="primary" onClick={applyImport} disabled={busy || !preview?.ready || !previewCurrent || query.isFetching}>{T.apply}</Button>
        </div>
        {reading ? <StateBlock subject={T.file} state={{ kind: 'initial-loading' }} /> : null}
        {previewOutcome !== null && previewOutcome.type !== 'ok-single' ? (
          previewOutcome.type === 'http' && (previewOutcome.status === 400 || previewOutcome.status === 422)
            ? <p role="alert" className={styles.error}>{T.validationRefused}</p>
            : <StateBlock subject={T.preview} state={classifyOutcome(previewOutcome)} onRetry={() => void validate()} />
        ) : null}
        {preview === null ? null : <section aria-labelledby="paint-preview-heading">
          <h3 id="paint-preview-heading">{T.preview}</h3>
          <p>{T.previewCount(preview.sourceRowCount)}</p>
          <p role={preview.ready ? 'status' : 'alert'}>{preview.ready ? T.ready : T.invalidPreview}</p>
          {!previewCurrent ? <p role="alert" className={styles.error}>{T.previewExpired}</p> : null}
          {preview.errors.length === 0 ? null : <div className={styles.error} role="alert"><h4>{T.errors}</h4><ul>
            {preview.errors.map((error, index) => <li key={index}>{T.rowError(error.rowNumber, error.message)}</li>)}
          </ul></div>}
          <div className={styles.tableWrap}><table><caption>{T.preview}</caption><thead><tr>
            <th scope="col">{T.row}</th><th scope="col">{T.code}</th><th scope="col">{T.name}</th><th scope="col">{T.membership}</th><th scope="col">{T.operation}</th>
          </tr></thead><tbody>{preview.rows.map((row) => <tr key={row.productId}><td>{row.rowNumber}</td><td>{row.productCode}</td><td>{row.productName}</td>
            <td>{row.alreadyMember ? T.alreadyMember : T.willAdd}</td><td>{row.alreadyMember ? null : <Button type="button" size="sm"
              disabled={!canEdit || !preview.ready || !previewCurrent} onClick={() => setMembership(row, true)}>{T.add}</Button>}</td>
          </tr>)}</tbody></table></div>
        </section>}
      </section>
      <section className={styles.section} aria-labelledby="paint-current-heading"><h2 id="paint-current-heading">{T.current}</h2><p>{T.manualHelp}</p>
        {catalog.outcome !== 1 ? null : <section aria-labelledby="paint-candidates-heading">
          <h3 id="paint-candidates-heading">{T.candidates}</h3><p>{T.candidateHelp}</p>
          <form onSubmit={(event) => { event.preventDefault(); void searchCandidates() }}>
            <Field label={T.candidateQuery}>{(control) => <input {...control} type="search" maxLength={PAINT_CANDIDATE_QUERY_LIMIT}
              value={candidateText} disabled={busy} onChange={(event) => { invalidateCandidates(); setCandidateText(event.currentTarget.value) }} />}</Field>
            <div className={styles.actions}><Button type="submit" disabled={!canEdit}>{T.search}</Button></div>
          </form>
          {candidateOutcome !== null && candidateOutcome.type !== 'ok-single' ? candidateOutcome.type === 'http' && candidateOutcome.status === 400
            ? <p role="alert">{T.candidateSearchRefused}</p>
            : <StateBlock subject={T.candidates} state={classifyOutcome(candidateOutcome)} onRetry={() => void searchCandidates()} /> : null}
          {candidates?.outcome === 3 ? <StateBlock subject={T.candidates} state={{ kind: 'service-unavailable' }} /> : null}
          {candidates?.outcome === 2 ? <p role="status">{T.noSource}</p> : null}
          {candidates?.outcome !== 1 ? null : <>
            {!candidatesCurrent ? <p role="alert">{T.candidateExpired}</p> : null}
            {candidates.searchWindowFull ? <p role="status">{T.candidateWindowFull}</p> : null}
            {candidates.items.length === 0 ? <p role="status">{T.noCandidates}</p> : <div className={styles.tableWrap}>
              <table><caption>{T.candidates}</caption><thead><tr><th scope="col">{T.code}</th><th scope="col">{T.name}</th>
                <th scope="col">{T.membership}</th><th scope="col">{T.operation}</th></tr></thead>
                <tbody>{candidates.items.map((row) => <tr key={row.productId}><td>{row.productCode}</td><td>{row.productName}</td>
                  <td>{row.alreadyMember ? T.alreadyMember : T.willAdd}</td><td>{row.alreadyMember ? null : <Button type="button" size="sm"
                    disabled={!canEdit || !candidatesCurrent} onClick={() => setMembership(row, true, 'candidate')}>{T.add}</Button>}</td></tr>)}</tbody>
              </table></div>}
          </>}
        </section>}
        {catalog.outcome === 2 ? null : catalog.entries.length === 0 ? <p role="status">{T.empty}</p> : <div className={styles.tableWrap}>
          <table><caption>{T.current}</caption><thead><tr><th scope="col">{T.code}</th><th scope="col">{T.name}</th><th scope="col">{T.operation}</th></tr></thead>
            <tbody>{catalog.entries.map((row) => <tr key={row.productId}><td>{row.productCode}</td><td>{row.productName}</td><td>
              <Button type="button" variant="danger" size="sm" disabled={!canEdit} onClick={() => setMembership(row, false)}>{T.remove}</Button>
            </td></tr>)}</tbody></table>
        </div>}
      </section>
    </>}
  </section>
}
