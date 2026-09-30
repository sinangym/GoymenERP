/**
 * AT-065 document panel of an owner record (first owner: the Cari card, C-03).
 *
 * The user's job: on a record, add a document (PDF, PNG or JPEG), see the record's documents after closing and reopening
 * the card, download one, and be told when the same file was already uploaded in this company (AT-057). Nothing here
 * deletes, renames or overwrites a document — retention/deletion is undecided (OPEN-018/021) and a document is never
 * overwritten (DEC-DOC-003).
 *
 * Authority is the server's, measured on every request: the panel appears only when the server answers the list (the
 * vault's read name AND the owner record's read); a 403 or 404 leaves no trace of the panel, not even a heading. The
 * upload form appears only when the list says `canUpload`. A version the security scan has not cleared is listed but
 * offers no download (DEC-DOC-009, fail-closed).
 *
 * An upload keeps ONE Idempotency-Key for the chosen file until its outcome is known, and the company it was chosen
 * under: a lost answer is retried with the same key and the same company, so it can never store the document twice.
 */

import { useId, useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { downloadDocument, fetchDocuments, uploadDocument, type UploadAttempt } from './api'
import { DOCUMENT_PROBLEM, type DocumentList, type DocumentRow, type DocumentUpload, type OwnerKind } from './contract'
import { DOCUMENTS_TEXT as TEXT, formatInstant, formatSize, refusalText, typeLabel } from './documentsText'
import styles from './DocumentsPanel.module.css'

type UploadState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'selected'; readonly attempt: UploadAttempt }
  | { readonly phase: 'sending'; readonly attempt: UploadAttempt }
  | { readonly phase: 'unknown'; readonly attempt: UploadAttempt }
  | { readonly phase: 'stored'; readonly upload: DocumentUpload; readonly replayed: boolean }
  | { readonly phase: 'refused'; readonly message: string }

function newKey(): string {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `doc-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

export function DocumentsPanel({
  ownerKind,
  ownerId,
  companyId,
  namespace,
  entryGeneration,
}: {
  readonly ownerKind: OwnerKind
  readonly ownerId: string
  readonly companyId: string
  readonly namespace: string
  readonly entryGeneration: number
}) {
  const queryClient = useQueryClient()
  const queryKey = [namespace, entryGeneration, 'documents', ownerKind, ownerId] as const
  const query = useQuery({
    queryKey,
    queryFn: ({ signal }) => fetchDocuments(ownerKind, ownerId, companyId, signal),
  })

  const outcome = query.data
  if (query.isPending || outcome === undefined) return null
  // No read of the vault or of this record: the panel does not exist for this actor.
  if (outcome.type === 'http' && (outcome.status === 403 || outcome.status === 404)) return null
  if (outcome.type === 'fence-stale') return null

  return (
    <section aria-labelledby="documents-heading" className={styles.block} data-section="documents">
      <h2 id="documents-heading" className={styles.title}>
        {TEXT.heading}
      </h2>
      {outcome.type === 'ok-single' ? (
        <DocumentsBody
          list={outcome.data}
          ownerKind={ownerKind}
          ownerId={ownerId}
          companyId={companyId}
          onStored={() => void queryClient.invalidateQueries({ queryKey })}
        />
      ) : (
        <div role="alert" className={styles.problem} data-documents-state="read-failed">
          <p>{TEXT.readFailed}</p>
          <button type="button" className={styles.action} onClick={() => void query.refetch()}>
            {TEXT.readAgain}
          </button>
        </div>
      )}
    </section>
  )
}

function DocumentsBody({
  list,
  ownerKind,
  ownerId,
  companyId,
  onStored,
}: {
  readonly list: DocumentList
  readonly ownerKind: OwnerKind
  readonly ownerId: string
  readonly companyId: string
  readonly onStored: () => void
}) {
  return (
    <div className={styles.body}>
      <p className={styles.note}>{TEXT.note}</p>
      {list.canUpload ? (
        <UploadForm list={list} ownerKind={ownerKind} ownerId={ownerId} companyId={companyId} onStored={onStored} />
      ) : null}
      {list.items.length === 0 ? (
        <p className={styles.note} data-documents-state="none">
          {TEXT.none}
        </p>
      ) : (
        <DocumentTable items={list.items} companyId={companyId} />
      )}
    </div>
  )
}

function UploadForm({
  list,
  ownerKind,
  ownerId,
  companyId,
  onStored,
}: {
  readonly list: DocumentList
  readonly ownerKind: OwnerKind
  readonly ownerId: string
  readonly companyId: string
  readonly onStored: () => void
}) {
  const inputId = useId()
  const hintId = useId()
  const inputRef = useRef<HTMLInputElement | null>(null)
  const [state, setState] = useState<UploadState>({ phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)

  const choose = (file: File | undefined) => {
    setLocalError(null)
    if (file === undefined) {
      setState({ phase: 'idle' })
      return
    }
    const dot = file.name.lastIndexOf('.')
    const extension = dot < 0 ? '' : file.name.slice(dot).toLowerCase()
    if (!list.limits.extensions.includes(extension)) {
      setLocalError(TEXT.typeLocal)
      setState({ phase: 'idle' })
      return
    }
    if (file.size > list.limits.maximumBytes) {
      setLocalError(TEXT.tooLargeLocal(list.limits.maximumBytes))
      setState({ phase: 'idle' })
      return
    }
    // A new file is a new attempt: a new key, frozen with the company it was chosen under.
    setState({ phase: 'selected', attempt: { idempotencyKey: newKey(), companyId, ownerKind, ownerId, file } })
  }

  const send = async (attempt: UploadAttempt) => {
    setState({ phase: 'sending', attempt })
    const outcome = await uploadDocument(attempt)
    switch (outcome.type) {
      case 'stored':
        setState({ phase: 'stored', upload: outcome.upload, replayed: outcome.replayed })
        if (inputRef.current !== null) inputRef.current.value = ''
        onStored()
        return
      case 'refused':
        setState({ phase: 'refused', message: refusalText(outcome.status, outcome.code) })
        if (inputRef.current !== null) inputRef.current.value = ''
        return
      case 'unknown':
        setState({ phase: 'unknown', attempt })
        return
      case 'aborted':
        setState({ phase: 'selected', attempt })
        return
    }
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    if (state.phase === 'selected') void send(state.attempt)
  }

  const busy = state.phase === 'sending'
  const pending = state.phase === 'unknown'

  return (
    <form className={styles.upload} onSubmit={onSubmit} aria-label={TEXT.uploadLegend} data-documents-upload="form">
      <label htmlFor={inputId} className={styles.label}>
        {TEXT.fileLabel}
      </label>
      <input
        id={inputId}
        ref={inputRef}
        type="file"
        className={styles.file}
        accept={[...list.limits.extensions, ...list.limits.contentTypes].join(',')}
        aria-describedby={hintId}
        // While an outcome is unknown the file is pinned: the retry must send exactly this attempt.
        disabled={busy || pending}
        onChange={(event) => choose(event.target.files?.[0])}
      />
      <p id={hintId} className={styles.note}>
        {TEXT.allowed(list.limits.extensions, list.limits.maximumBytes)}
      </p>
      <button type="submit" className={styles.primary} disabled={state.phase !== 'selected'}>
        {busy ? TEXT.uploading : TEXT.upload}
      </button>

      {localError !== null ? (
        <p role="alert" className={styles.problem} data-documents-state="local-refusal">
          {localError}
        </p>
      ) : null}

      {state.phase === 'refused' ? (
        <p role="alert" className={styles.problem} data-documents-state="refused">
          {state.message}
        </p>
      ) : null}

      {state.phase === 'unknown' ? (
        <div role="alert" className={styles.problem} data-documents-state="unknown">
          <p>
            <strong>{TEXT.unknownTitle}</strong>
          </p>
          <p>{TEXT.unknownDetail}</p>
          <p className={styles.actions}>
            <button type="button" className={styles.action} onClick={() => void send(state.attempt)}>
              {TEXT.retry}
            </button>
            <button
              type="button"
              className={styles.action}
              onClick={() => {
                setState({ phase: 'idle' })
                if (inputRef.current !== null) inputRef.current.value = ''
              }}
            >
              {TEXT.abandon}
            </button>
          </p>
        </div>
      ) : null}

      {state.phase === 'stored' ? (
        <div role="status" className={styles.saved} data-documents-state="stored">
          <p>{TEXT.uploaded(state.upload.document.fileName)}</p>
          {state.replayed ? <p data-documents-state="replayed">{TEXT.replayed}</p> : null}
          {state.upload.duplicates.length > 0 ? (
            <div className={styles.warning} data-documents-state="duplicate">
              <p>
                <strong>{TEXT.duplicateTitle}</strong>
              </p>
              <p>{TEXT.duplicateDetail}</p>
              <ul>
                {state.upload.duplicates.map((duplicate) => (
                  <li key={duplicate.documentId} data-duplicate-id={duplicate.documentId}>
                    {TEXT.duplicateRow(duplicate.fileName, formatInstant(duplicate.uploadedAtUtc))}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}
    </form>
  )
}

function DocumentTable({ items, companyId }: { readonly items: readonly DocumentRow[]; readonly companyId: string }) {
  const [downloading, setDownloading] = useState<string | null>(null)
  const [problem, setProblem] = useState<{ readonly id: string; readonly message: string } | null>(null)

  const download = async (row: DocumentRow) => {
    setProblem(null)
    setDownloading(row.id)
    const outcome = await downloadDocument(row.id, companyId)
    setDownloading(null)
    if (outcome.type !== 'ok') {
      setProblem({
        id: row.id,
        message:
          outcome.type === 'refused' && outcome.code === DOCUMENT_PROBLEM.notScanned ? TEXT.notScanned : TEXT.downloadRefused,
      })
      return
    }
    // Saved as a file under its own name; never opened as a page of this application.
    const url = URL.createObjectURL(outcome.blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = row.fileName
    anchor.rel = 'noopener'
    document.body.append(anchor)
    anchor.click()
    anchor.remove()
    setTimeout(() => URL.revokeObjectURL(url), 0)
  }

  return (
    <div className={styles.tableWrap}>
      <table className={styles.table}>
        <caption className={styles.srOnly}>{TEXT.caption}</caption>
        <thead>
          <tr>
            <th scope="col">{TEXT.columnFile}</th>
            <th scope="col">{TEXT.columnType}</th>
            <th scope="col">{TEXT.columnSize}</th>
            <th scope="col">{TEXT.columnUploaded}</th>
            <th scope="col">{TEXT.columnState}</th>
            <th scope="col">{TEXT.columnAction}</th>
          </tr>
        </thead>
        <tbody>
          {items.map((row) => (
            <tr key={row.id} data-document-id={row.id}>
              <th scope="row">{row.fileName}</th>
              <td>{typeLabel(row.contentType)}</td>
              <td>{formatSize(row.sizeBytes)}</td>
              <td>{formatInstant(row.uploadedAtUtc)}</td>
              <td data-scan-state={row.scanState}>{row.downloadable ? TEXT.scanClean : TEXT.scanPending}</td>
              <td>
                {row.downloadable ? (
                  <button
                    type="button"
                    className={styles.action}
                    aria-label={TEXT.download(row.fileName)}
                    disabled={downloading !== null}
                    onClick={() => void download(row)}
                  >
                    {downloading === row.id ? TEXT.downloading : TEXT.downloadLabel}
                  </button>
                ) : null}
                {problem?.id === row.id ? (
                  <p role="alert" className={styles.problem} data-documents-state="download-refused">
                    {problem.message}
                  </p>
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
