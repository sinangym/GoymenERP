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
 *
 * Versions (#677): every row can open its own history — newest first, paged by the server's cursor — and download a
 * chosen clean version's own bytes. Under `canUpload` the same place adds a NEW version to that document; it is not the
 * upload form, creates no document and changes no earlier version. An append is one frozen attempt (company, document,
 * expected current version, file, key) repeated unchanged until its outcome is known. The history is bound to the
 * company, owner, entry generation and document it was opened under: a change of any of them discards it, and an answer
 * that arrives afterwards is dropped.
 */

import { useCallback, useEffect, useId, useMemo, useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  appendDocumentVersion,
  downloadDocument,
  downloadDocumentVersion,
  fetchDocumentVersions,
  fetchDocuments,
  uploadDocument,
  type UploadAttempt,
  type VersionAttempt,
  type VersionHistoryOutcome,
} from './api'
import {
  DOCUMENT_PROBLEM,
  type DocumentBinding,
  type DocumentLimits,
  type DocumentList,
  type DocumentRow,
  type DocumentUpload,
  type DocumentVersionWrite,
  type OwnerKind,
} from './contract'
import { DOCUMENTS_TEXT as TEXT, formatInstant, formatSize, refusalText, typeLabel, versionRefusalText } from './documentsText'
import { DocumentPreview } from './DocumentPreview'
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

/** Why the chosen file cannot be sent under the list's limits; null when it can. The server re-checks regardless. */
function localRefusal(file: File, limits: DocumentLimits): string | null {
  const dot = file.name.lastIndexOf('.')
  const extension = dot < 0 ? '' : file.name.slice(dot).toLowerCase()
  if (!limits.extensions.includes(extension)) return TEXT.typeLocal
  if (file.size > limits.maximumBytes) return TEXT.tooLargeLocal(limits.maximumBytes)
  return null
}

/** Saved as a file under its own name; never opened as a page of this application. */
function saveAttachment(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.rel = 'noopener'
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
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
          scope={`${namespace}|${entryGeneration}|${companyId}|${ownerKind}|${ownerId}`}
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
  scope,
  onStored,
}: {
  readonly list: DocumentList
  readonly ownerKind: OwnerKind
  readonly ownerId: string
  readonly companyId: string
  readonly scope: string
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
        <DocumentTable list={list} companyId={companyId} scope={scope} onChanged={onStored} />
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
    const refusal = localRefusal(file, list.limits)
    if (refusal !== null) {
      setLocalError(refusal)
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

function DocumentTable({
  list,
  companyId,
  scope,
  onChanged,
}: {
  readonly list: DocumentList
  readonly companyId: string
  readonly scope: string
  readonly onChanged: () => void
}) {
  const items = list.items
  const regionId = useId()
  const toggles = useRef(new Map<string, HTMLButtonElement>())
  const [downloading, setDownloading] = useState<string | null>(null)
  const [problem, setProblem] = useState<{ readonly id: string; readonly message: string } | null>(null)
  // The opened history belongs to the scope it was opened under; under another scope nothing is open.
  const [opened, setOpened] = useState<{ readonly scope: string; readonly id: string } | null>(null)
  const open = opened !== null && opened.scope === scope ? (items.find((row) => row.id === opened.id) ?? null) : null

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
    saveAttachment(outcome.blob, row.fileName)
  }

  return (
    <>
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
                  <div className={styles.rowActions}>
                    {row.downloadable ? (
                      <>
                        <button
                          type="button"
                          className={styles.action}
                          aria-label={TEXT.download(row.fileName)}
                          disabled={downloading !== null}
                          onClick={() => void download(row)}
                        >
                          {downloading === row.id ? TEXT.downloading : TEXT.downloadLabel}
                        </button>
                        <DocumentPreview row={row} companyId={companyId} />
                      </>
                    ) : null}
                    {/* The latest version may be unscanned while an earlier one is clean: the history is always offered. */}
                    <button
                      type="button"
                      className={styles.action}
                      ref={(element) => {
                        if (element === null) toggles.current.delete(row.id)
                        else toggles.current.set(row.id, element)
                      }}
                      aria-label={open?.id === row.id ? TEXT.versionsHide(row.fileName) : TEXT.versionsShow(row.fileName)}
                      aria-expanded={open?.id === row.id}
                      aria-controls={open?.id === row.id ? regionId : undefined}
                      data-document-versions-toggle={row.id}
                      onClick={() => setOpened(open?.id === row.id ? null : { scope, id: row.id })}
                    >
                      {TEXT.versionsLabel}
                    </button>
                  </div>
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
      {open !== null ? (
        <DocumentVersions
          // A new company, owner, entry generation or document is a new history; nothing of the old one survives.
          key={`${scope}|${open.id}`}
          regionId={regionId}
          companyId={companyId}
          ownerKind={open.ownerKind}
          ownerId={open.ownerId}
          documentId={open.id}
          fileName={open.fileName}
          canUpload={list.canUpload}
          limits={list.limits}
          onChanged={onChanged}
          onClose={() => {
            const toggle = toggles.current.get(open.id)
            setOpened(null)
            toggle?.focus()
          }}
        />
      ) : null}
    </>
  )
}

/* ── Immutable versions of one document ───────────────────────────────────── */

type History =
  | { readonly phase: 'loading' }
  | { readonly phase: 'failed' }
  /** 403/404: there is no history for this actor here, and nothing to retry. */
  | { readonly phase: 'gone' }
  | {
      readonly phase: 'ready'
      readonly versions: readonly DocumentRow[]
      readonly nextCursor: string | null
      readonly more: 'idle' | 'loading' | 'failed'
    }

function firstPage(outcome: VersionHistoryOutcome): History | null {
  switch (outcome.type) {
    case 'ok':
      return { phase: 'ready', versions: outcome.page.versions, nextCursor: outcome.page.nextCursor, more: 'idle' }
    case 'gone':
      return { phase: 'gone' }
    case 'failed':
      return { phase: 'failed' }
    case 'aborted':
      return null
  }
}

function DocumentVersions({
  regionId,
  companyId,
  ownerKind,
  ownerId,
  documentId,
  fileName,
  canUpload,
  limits,
  onChanged,
  onClose,
}: {
  readonly regionId: string
  readonly companyId: string
  readonly ownerKind: string
  readonly ownerId: string
  readonly documentId: string
  readonly fileName: string
  readonly canUpload: boolean
  readonly limits: DocumentLimits
  readonly onChanged: () => void
  readonly onClose: () => void
}) {
  const headingId = useId()
  const headingRef = useRef<HTMLHeadingElement | null>(null)
  const binding = useMemo<DocumentBinding>(
    () => ({ companyId, ownerKind, ownerId, documentId }),
    [companyId, ownerKind, ownerId, documentId],
  )
  // `alive` closes with the binding; `sequence` lets only the latest history request answer.
  const alive = useRef(false)
  const sequence = useRef(0)
  const abort = useRef<AbortController | null>(null)
  const [history, setHistory] = useState<History>({ phase: 'loading' })
  const [downloading, setDownloading] = useState<number | null>(null)
  const [problem, setProblem] = useState<{ readonly versionNo: number; readonly message: string } | null>(null)

  const readFirst = useCallback(() => {
    const run = ++sequence.current
    void fetchDocumentVersions(binding, null, abort.current?.signal).then((outcome) => {
      if (!alive.current || run !== sequence.current) return
      const next = firstPage(outcome)
      if (next !== null) setHistory(next)
    })
  }, [binding])

  useEffect(() => {
    const controller = new AbortController()
    alive.current = true
    abort.current = controller
    headingRef.current?.focus()
    readFirst()
    return () => {
      alive.current = false
      controller.abort()
    }
  }, [readFirst])

  const reload = () => {
    setProblem(null)
    setHistory({ phase: 'loading' })
    readFirst()
  }

  const readMore = () => {
    if (history.phase !== 'ready' || history.nextCursor === null || history.more === 'loading') return
    // A failed continuation is repeated with the very cursor it failed on; the rows already shown stay.
    const cursor = history.nextCursor
    const run = ++sequence.current
    setHistory({ ...history, more: 'loading' })
    void fetchDocumentVersions(binding, cursor, abort.current?.signal).then((outcome) => {
      if (!alive.current || run !== sequence.current || outcome.type === 'aborted') return
      setHistory((held) => {
        if (held.phase !== 'ready' || held.nextCursor !== cursor) return held
        if (outcome.type === 'gone') return { phase: 'gone' }
        if (outcome.type === 'failed') return { ...held, more: 'failed' }
        return { phase: 'ready', versions: [...held.versions, ...outcome.page.versions], nextCursor: outcome.page.nextCursor, more: 'idle' }
      })
    })
  }

  const download = async (version: DocumentRow) => {
    setProblem(null)
    setDownloading(version.versionNo)
    const outcome = await downloadDocumentVersion(binding, version.versionNo, version.sizeBytes, abort.current?.signal)
    // Bytes that arrive after the binding closed are not saved.
    if (!alive.current) return
    setDownloading(null)
    if (outcome.type !== 'ok') {
      setProblem({
        versionNo: version.versionNo,
        message:
          outcome.type === 'refused' && outcome.code === DOCUMENT_PROBLEM.notScanned
            ? TEXT.versionNotScanned
            : TEXT.versionDownloadRefused,
      })
      return
    }
    saveAttachment(outcome.blob, version.fileName)
  }

  const refresh = () => {
    reload()
    onChanged()
  }

  return (
    <section id={regionId} aria-labelledby={headingId} className={styles.versions} data-document-versions={documentId}>
      <h3 id={headingId} ref={headingRef} tabIndex={-1} className={styles.versionsTitle}>
        {TEXT.versionsHeading(fileName)}
      </h3>
      <p className={styles.note}>{TEXT.versionsNote}</p>

      {canUpload && history.phase !== 'gone' ? (
        <VersionForm
          binding={binding}
          // The newest version this history has read; null until it has, and then nothing can be chosen.
          currentVersion={history.phase === 'ready' ? history.versions[0].versionNo : null}
          limits={limits}
          onSettled={refresh}
        />
      ) : null}

      {history.phase === 'loading' ? (
        <p role="status" className={styles.note} data-version-state="loading">
          {TEXT.versionsLoading}
        </p>
      ) : null}

      {history.phase === 'failed' ? (
        <div role="alert" className={styles.problem} data-version-state="read-failed">
          <p>{TEXT.versionsFailed}</p>
          <button type="button" className={styles.action} onClick={reload}>
            {TEXT.versionsAgain}
          </button>
        </div>
      ) : null}

      {history.phase === 'gone' ? (
        <p role="alert" className={styles.problem} data-version-state="gone">
          {TEXT.versionsGone}
        </p>
      ) : null}

      {history.phase === 'ready' ? (
        <>
          <div className={styles.tableWrap}>
            <table className={`${styles.table} ${styles.versionTable}`}>
              <caption className={styles.srOnly}>{TEXT.versionsCaption(fileName)}</caption>
              <thead>
                <tr>
                  <th scope="col">{TEXT.columnVersion}</th>
                  <th scope="col">{TEXT.columnFile}</th>
                  <th scope="col">{TEXT.columnType}</th>
                  <th scope="col">{TEXT.columnSize}</th>
                  <th scope="col">{TEXT.columnUploaded}</th>
                  <th scope="col">{TEXT.columnState}</th>
                  <th scope="col">{TEXT.columnAction}</th>
                </tr>
              </thead>
              <tbody>
                {history.versions.map((version) => (
                  <tr key={version.versionNo} data-version-no={version.versionNo}>
                    <th scope="row">{TEXT.versionNo(version.versionNo)}</th>
                    <td data-label={TEXT.columnFile}>{version.fileName}</td>
                    <td data-label={TEXT.columnType}>{typeLabel(version.contentType)}</td>
                    <td data-label={TEXT.columnSize}>{formatSize(version.sizeBytes)}</td>
                    <td data-label={TEXT.columnUploaded}>{formatInstant(version.uploadedAtUtc)}</td>
                    <td data-label={TEXT.columnState} data-scan-state={version.scanState}>
                      {version.downloadable ? TEXT.scanClean : TEXT.scanPending}
                    </td>
                    <td data-label={TEXT.columnAction}>
                      {version.downloadable ? (
                        <button
                          type="button"
                          className={styles.action}
                          aria-label={TEXT.versionDownload(version.fileName, version.versionNo)}
                          disabled={downloading !== null}
                          onClick={() => void download(version)}
                        >
                          {downloading === version.versionNo ? TEXT.downloading : TEXT.downloadLabel}
                        </button>
                      ) : null}
                      {problem?.versionNo === version.versionNo ? (
                        <p role="alert" className={styles.problem} data-version-state="download-refused">
                          {problem.message}
                        </p>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {history.more === 'failed' ? (
            <p role="alert" className={styles.problem} data-version-state="more-failed">
              {TEXT.versionsMoreFailed}
            </p>
          ) : null}
          {history.nextCursor !== null ? (
            <button type="button" className={styles.action} disabled={history.more === 'loading'} onClick={readMore} data-version-more={history.nextCursor}>
              {history.more === 'loading' ? TEXT.versionsMoreLoading : history.more === 'failed' ? TEXT.versionsMoreAgain : TEXT.versionsMore}
            </button>
          ) : (
            <p className={styles.note} data-version-state="end">
              {TEXT.versionsEnd}
            </p>
          )}
        </>
      ) : null}

      <button type="button" className={styles.action} onClick={onClose}>
        {TEXT.versionsClose}
      </button>
    </section>
  )
}

type AppendState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'selected'; readonly attempt: VersionAttempt }
  | { readonly phase: 'sending'; readonly attempt: VersionAttempt }
  | { readonly phase: 'unknown'; readonly attempt: VersionAttempt }
  | { readonly phase: 'stored'; readonly write: DocumentVersionWrite; readonly replayed: boolean }
  | { readonly phase: 'refused'; readonly message: string }

function VersionForm({
  binding,
  currentVersion,
  limits,
  onSettled,
}: {
  readonly binding: DocumentBinding
  readonly currentVersion: number | null
  readonly limits: DocumentLimits
  readonly onSettled: () => void
}) {
  const inputId = useId()
  const hintId = useId()
  const inputRef = useRef<HTMLInputElement | null>(null)
  const alive = useRef(false)
  const [state, setState] = useState<AppendState>({ phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)

  useEffect(() => {
    alive.current = true
    return () => {
      alive.current = false
    }
  }, [])

  const clearInput = () => {
    if (inputRef.current !== null) inputRef.current.value = ''
  }

  const choose = (file: File | undefined) => {
    setLocalError(null)
    if (file === undefined || currentVersion === null) {
      setState({ phase: 'idle' })
      return
    }
    const refusal = localRefusal(file, limits)
    if (refusal !== null) {
      setLocalError(refusal)
      setState({ phase: 'idle' })
      return
    }
    // A new file is a new attempt: a new key, frozen with the company, the document and the version it was chosen on.
    setState({ phase: 'selected', attempt: { ...binding, idempotencyKey: newKey(), expectedCurrentVersion: currentVersion, file } })
  }

  const send = async (attempt: VersionAttempt) => {
    setState({ phase: 'sending', attempt })
    const outcome = await appendDocumentVersion(attempt)
    // The binding closed while the request was out: its answer belongs to nobody here.
    if (!alive.current) return
    switch (outcome.type) {
      case 'stored':
        setState({ phase: 'stored', write: outcome.write, replayed: outcome.replayed })
        clearInput()
        onSettled()
        return
      case 'refused':
        setState({ phase: 'refused', message: versionRefusalText(outcome.status, outcome.code) })
        clearInput()
        // A stale expected version means the history on screen is behind: read it and the list again.
        if (outcome.code === DOCUMENT_PROBLEM.concurrentChange) onSettled()
        return
      case 'unknown':
        setState({ phase: 'unknown', attempt })
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
    <form className={styles.upload} onSubmit={onSubmit} aria-label={TEXT.appendLegend} data-document-version-append="form">
      <label htmlFor={inputId} className={styles.label}>
        {TEXT.appendFileLabel}
      </label>
      <input
        id={inputId}
        ref={inputRef}
        type="file"
        className={styles.file}
        accept={[...limits.extensions, ...limits.contentTypes].join(',')}
        aria-describedby={hintId}
        // While an outcome is unknown the file is pinned: the retry must send exactly this attempt.
        disabled={busy || pending || currentVersion === null}
        onChange={(event) => choose(event.target.files?.[0])}
      />
      <p id={hintId} className={styles.note}>
        {currentVersion !== null ? `${TEXT.appendNote(currentVersion + 1)} ` : ''}
        {TEXT.allowed(limits.extensions, limits.maximumBytes)}
      </p>
      <button type="submit" className={styles.primary} disabled={state.phase !== 'selected'}>
        {busy ? TEXT.appending : TEXT.append}
      </button>

      {localError !== null ? (
        <p role="alert" className={styles.problem} data-version-state="local-refusal">
          {localError}
        </p>
      ) : null}

      {state.phase === 'refused' ? (
        <p role="alert" className={styles.problem} data-version-state="refused">
          {state.message}
        </p>
      ) : null}

      {state.phase === 'unknown' ? (
        <div role="alert" className={styles.problem} data-version-state="unknown">
          <p>
            <strong>{TEXT.appendUnknownTitle}</strong>
          </p>
          <p>{TEXT.appendUnknownDetail}</p>
          <p className={styles.actions}>
            <button type="button" className={styles.action} onClick={() => void send(state.attempt)}>
              {TEXT.appendRetry}
            </button>
            <button
              type="button"
              className={styles.action}
              onClick={() => {
                setState({ phase: 'idle' })
                clearInput()
              }}
            >
              {TEXT.abandon}
            </button>
          </p>
        </div>
      ) : null}

      {state.phase === 'stored' ? (
        <div role="status" className={styles.saved} data-version-state="stored">
          <p>{TEXT.appended(state.write.document.fileName, state.write.document.versionNo)}</p>
          {state.replayed ? <p data-version-state="replayed">{TEXT.appendReplayed}</p> : null}
        </div>
      ) : null}
    </form>
  )
}
