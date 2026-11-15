/**
 * B-08 / BS-10 (STK-S12): the selected company's package standard (pieces per package) — first imported from an Excel file,
 * then edited in the ERP one product at a time; each import or edit is a new version. Without
 * `product.package-standards.manage` (granted to no role here) the server answers 403 and the panel shows nothing; an ended
 * session (401), an unwired provider (503) and a failed read each say so instead of "kaynak yok". While no table or no entry
 * exists the panel says "kaynak yok" (NoSource) and guesses nothing. The forms check the input only to help the person; the
 * server holds every rule.
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  PACKAGE_STANDARD_LIMITS,
  PACKAGE_STANDARD_PROBLEM,
  bytesToBase64,
  checkPackageFile,
  fetchPackageStandardTable,
  importPackageStandardTable,
  parsePieces,
  setPackageStandardEntry,
  type PackageStandardCommand,
  type PackageStandardTableView,
} from './packageImportApi'
import { PACKAGE_SOURCE_KIND_LABEL, PACKAGE_STANDARD_TEXT } from './packageImportText'
import styles from './ProductDetailPage.module.css'

const HEADING_ID = 'product-package-standard-heading'

function Heading() {
  return <h2 id={HEADING_ID}>{PACKAGE_STANDARD_TEXT.heading}</h2>
}

type Opened = 'none' | 'import' | 'edit'

export function PackageImportPanel({
  companyId,
  productId,
  queryNamespace,
}: {
  readonly companyId: string
  readonly productId: string
  readonly queryNamespace: string
}) {
  const query = useQuery({
    queryKey: [queryNamespace, 'product-package-standard-table'],
    queryFn: ({ signal }) => fetchPackageStandardTable(companyId, signal),
  })
  const [opened, setOpened] = useState<Opened>('none')
  const [notice, setNotice] = useState<string | null>(null)

  if (query.isPending) {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} aria-busy="true">
        <Heading />
        <p className={styles.note}>{PACKAGE_STANDARD_TEXT.loading}</p>
      </section>
    )
  }

  const outcome = query.data
  if (outcome === undefined || outcome.type === 'forbidden') return null
  if (outcome.type === 'unauthenticated') {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid="package-standard-unauthenticated">
        <Heading />
        <p role="alert">{PACKAGE_STANDARD_TEXT.unauthenticated}</p>
      </section>
    )
  }

  if (outcome.type !== 'ok') {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid={`package-standard-${outcome.type}`}>
        <Heading />
        <p role="alert">{outcome.type === 'unavailable' ? PACKAGE_STANDARD_TEXT.unavailable : PACKAGE_STANDARD_TEXT.failed}</p>
        <button type="button" onClick={() => void query.refetch()}>
          {PACKAGE_STANDARD_TEXT.retryRead}
        </button>
      </section>
    )
  }

  const view = outcome.data
  const entry = view.entries.find((candidate) => candidate.productId.toLowerCase() === productId.toLowerCase()) ?? null
  const done = (message: string | null) => {
    setOpened('none')
    setNotice(message)
    void query.refetch()
  }

  return (
    <section className={styles.section} aria-labelledby={HEADING_ID}>
      <Heading />
      {notice !== null ? <p role="status">{notice}</p> : null}
      <p className={styles.note}>{PACKAGE_STANDARD_TEXT.note}</p>

      <h3>{PACKAGE_STANDARD_TEXT.productHeading}</h3>
      <p data-testid="package-standard-product">
        {entry === null
          ? PACKAGE_STANDARD_TEXT.productNoSource
          : `${PACKAGE_STANDARD_TEXT.productValue} ${String(entry.piecesPerPackage)}`}
      </p>
      {opened === 'edit' ? (
        <EditForm
          companyId={companyId}
          productId={productId}
          view={view}
          current={entry?.piecesPerPackage ?? null}
          onDone={done}
        />
      ) : opened === 'none' ? (
        <button type="button" onClick={() => setOpened('edit')}>
          {PACKAGE_STANDARD_TEXT.openEdit}
        </button>
      ) : null}

      {view.source === null ? (
        <p data-testid="package-standard-empty">{PACKAGE_STANDARD_TEXT.empty}</p>
      ) : (
        <TableView view={view} />
      )}
      {opened === 'import' ? (
        <ImportForm companyId={companyId} view={view} onDone={done} />
      ) : opened === 'none' ? (
        <button type="button" onClick={() => setOpened('import')}>
          {PACKAGE_STANDARD_TEXT.openImport}
        </button>
      ) : null}
    </section>
  )
}

function TableView({ view }: { readonly view: PackageStandardTableView }) {
  const source = view.source
  if (source === null) return null
  return (
    <>
      <dl data-testid="package-standard-header">
        <div>
          <dt>{PACKAGE_STANDARD_TEXT.version}</dt>
          <dd>{view.version}</dd>
        </div>
        <div>
          <dt>{PACKAGE_STANDARD_TEXT.sourceKind}</dt>
          <dd>{PACKAGE_SOURCE_KIND_LABEL[source.kind]}</dd>
        </div>
        <div>
          <dt>{PACKAGE_STANDARD_TEXT.sourceLabel}</dt>
          <dd>{source.sourceLabel ?? PACKAGE_STANDARD_TEXT.noValue}</dd>
        </div>
        <div>
          <dt>{PACKAGE_STANDARD_TEXT.fileName}</dt>
          <dd>{source.fileName ?? PACKAGE_STANDARD_TEXT.noValue}</dd>
        </div>
        <div>
          <dt>{PACKAGE_STANDARD_TEXT.createdAt}</dt>
          <dd>{source.createdAtUtc}</dd>
        </div>
        <div>
          <dt>{PACKAGE_STANDARD_TEXT.createdBy}</dt>
          <dd>{source.createdBy}</dd>
        </div>
        <div>
          <dt>{PACKAGE_STANDARD_TEXT.entryCount}</dt>
          <dd>{view.entries.length}</dd>
        </div>
      </dl>
      {view.entries.length === 0 ? (
        <p data-testid="package-standard-no-entries">{PACKAGE_STANDARD_TEXT.empty}</p>
      ) : (
        <table data-testid="package-standard-rows">
          <caption>{PACKAGE_STANDARD_TEXT.tableCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{PACKAGE_STANDARD_TEXT.columnCode}</th>
              <th scope="col">{PACKAGE_STANDARD_TEXT.columnName}</th>
              <th scope="col">{PACKAGE_STANDARD_TEXT.columnPieces}</th>
            </tr>
          </thead>
          <tbody>
            {view.entries.map((row) => (
              <tr key={row.productId}>
                <td>{row.productCode}</td>
                <td>{row.productName}</td>
                <td>{String(row.piecesPerPackage)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}

const FILE_CHECK_TEXT = {
  'not-xlsx': PACKAGE_STANDARD_TEXT.fileNotXlsx,
  'name-too-long': PACKAGE_STANDARD_TEXT.fileNameTooLong,
  empty: PACKAGE_STANDARD_TEXT.fileEmpty,
  'too-large': PACKAGE_STANDARD_TEXT.fileTooLarge,
} as const

function refusalText(status: number, code: string | null, detail: string | null): string {
  switch (code) {
    case PACKAGE_STANDARD_PROBLEM.fileInvalid:
      return detail === null ? PACKAGE_STANDARD_TEXT.fileInvalid : `${PACKAGE_STANDARD_TEXT.fileInvalid} ${detail}`
    case PACKAGE_STANDARD_PROBLEM.sourceLabelRequired:
      return PACKAGE_STANDARD_TEXT.sourceLabelRequired
    case PACKAGE_STANDARD_PROBLEM.sourceLabelTooLong:
      return PACKAGE_STANDARD_TEXT.sourceLabelTooLong
    case PACKAGE_STANDARD_PROBLEM.reasonRequired:
      return PACKAGE_STANDARD_TEXT.reasonRequired
    case PACKAGE_STANDARD_PROBLEM.reasonTooLong:
      return PACKAGE_STANDARD_TEXT.reasonTooLong
    case PACKAGE_STANDARD_PROBLEM.piecesInvalid:
      return PACKAGE_STANDARD_TEXT.piecesInvalid
    case PACKAGE_STANDARD_PROBLEM.idempotencyConflict:
      return PACKAGE_STANDARD_TEXT.idempotencyConflict
    default:
      return status === 404 && code === null ? PACKAGE_STANDARD_TEXT.productMissing : PACKAGE_STANDARD_TEXT.refused
  }
}

type Pending = { readonly key: string; readonly fingerprint: string } | null

/**
 * Sends one command under a caller-owned key: the same key is reused only for the same body after an unknown outcome; a definite
 * answer drops it.
 */
function useCommand(
  onDone: (notice: string | null) => void,
  texts: { readonly applied: string; readonly replayed: string },
) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending>(null)

  async function run(fingerprint: string, call: (key: string) => Promise<PackageStandardCommand>) {
    const key = pending !== null && pending.fingerprint === fingerprint ? pending.key : crypto.randomUUID()
    setBusy(true)
    setError(null)
    const outcome = await call(key)
    setBusy(false)
    if (outcome.type === 'unknown') {
      setPending({ key, fingerprint })
      setError(PACKAGE_STANDARD_TEXT.unknownOutcome)
      return
    }
    setPending(null)
    if (outcome.type === 'applied') {
      onDone(outcome.replayed ? texts.replayed : texts.applied)
    } else if (outcome.code === PACKAGE_STANDARD_PROBLEM.versionConflict) {
      onDone(PACKAGE_STANDARD_TEXT.conflict)
    } else {
      setError(refusalText(outcome.status, outcome.code, outcome.detail))
    }
  }

  return { busy, error, setError, pending, run }
}

function SubmitButtons({
  busy,
  retrying,
  submitText,
  onCancel,
}: {
  readonly busy: boolean
  readonly retrying: boolean
  readonly submitText: string
  readonly onCancel: () => void
}) {
  return (
    <>
      <button type="submit" disabled={busy} aria-busy={busy}>
        {busy ? PACKAGE_STANDARD_TEXT.submitting : retrying ? PACKAGE_STANDARD_TEXT.retrySame : submitText}
      </button>
      <button type="button" disabled={busy} onClick={onCancel}>
        {PACKAGE_STANDARD_TEXT.cancel}
      </button>
    </>
  )
}

function ReasonField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <label>
      {PACKAGE_STANDARD_TEXT.reasonLabel}
      <textarea
        required
        rows={2}
        maxLength={PACKAGE_STANDARD_LIMITS.reasonMaximumLength}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  )
}

function EditForm({
  companyId,
  productId,
  view,
  current,
  onDone,
}: {
  readonly companyId: string
  readonly productId: string
  readonly view: PackageStandardTableView
  readonly current: number | null
  readonly onDone: (notice: string | null) => void
}) {
  const [pieces, setPieces] = useState(current === null ? '' : String(current))
  const [reason, setReason] = useState('')
  const command = useCommand(onDone, { applied: PACKAGE_STANDARD_TEXT.saved, replayed: PACKAGE_STANDARD_TEXT.savedReplayed })

  async function submit(event: FormEvent) {
    event.preventDefault()
    let piecesPerPackage: number | null = null
    if (pieces.trim() !== '') {
      piecesPerPackage = parsePieces(pieces)
      if (piecesPerPackage === null) {
        command.setError(PACKAGE_STANDARD_TEXT.piecesInvalid)
        return
      }
    }
    const why = reason.trim()
    if (why === '') {
      command.setError(PACKAGE_STANDARD_TEXT.reasonRequired)
      return
    }
    const body = { expectedVersion: view.version, piecesPerPackage, reason: why }
    await command.run(JSON.stringify(body), (key) => setPackageStandardEntry(companyId, productId, key, body))
  }

  return (
    <form onSubmit={(event) => void submit(event)} aria-label={PACKAGE_STANDARD_TEXT.editFormLabel} noValidate>
      <label>
        {PACKAGE_STANDARD_TEXT.piecesLabel}
        <input type="text" inputMode="numeric" value={pieces} onChange={(event) => setPieces(event.target.value)} />
      </label>
      <ReasonField value={reason} onChange={setReason} />
      {command.error !== null ? <p role="alert">{command.error}</p> : null}
      <SubmitButtons
        busy={command.busy}
        retrying={command.pending !== null}
        submitText={PACKAGE_STANDARD_TEXT.saveEntry}
        onCancel={() => onDone(null)}
      />
    </form>
  )
}

function ImportForm({
  companyId,
  view,
  onDone,
}: {
  readonly companyId: string
  readonly view: PackageStandardTableView
  readonly onDone: (notice: string | null) => void
}) {
  const [file, setFile] = useState<File | null>(null)
  const [sourceLabel, setSourceLabel] = useState('')
  const [reason, setReason] = useState('')
  const command = useCommand(onDone, { applied: PACKAGE_STANDARD_TEXT.imported, replayed: PACKAGE_STANDARD_TEXT.replayed })

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (file === null) {
      command.setError(PACKAGE_STANDARD_TEXT.fileRequired)
      return
    }
    const fileCheck = checkPackageFile(file)
    if (fileCheck !== 'ok') {
      command.setError(FILE_CHECK_TEXT[fileCheck])
      return
    }
    const label = sourceLabel.trim()
    const why = reason.trim()
    if (label === '') {
      command.setError(PACKAGE_STANDARD_TEXT.sourceLabelRequired)
      return
    }
    if (why === '') {
      command.setError(PACKAGE_STANDARD_TEXT.reasonRequired)
      return
    }
    let contentBase64: string
    try {
      contentBase64 = bytesToBase64(new Uint8Array(await file.arrayBuffer()))
    } catch {
      command.setError(PACKAGE_STANDARD_TEXT.fileUnreadable)
      return
    }
    const body = { expectedVersion: view.version, sourceLabel: label, reason: why, fileName: file.name, contentBase64 }
    await command.run(JSON.stringify(body), (key) => importPackageStandardTable(companyId, key, body))
  }

  return (
    <form onSubmit={(event) => void submit(event)} aria-label={PACKAGE_STANDARD_TEXT.formLabel} noValidate>
      <label>
        {PACKAGE_STANDARD_TEXT.fileLabel}
        <input
          type="file"
          accept={`${PACKAGE_STANDARD_LIMITS.fileExtension},${PACKAGE_STANDARD_LIMITS.contentType}`}
          required
          onChange={(event) => {
            setFile(event.target.files?.[0] ?? null)
            command.setError(null)
          }}
        />
      </label>
      <p className={styles.note}>
        {PACKAGE_STANDARD_TEXT.fileFormat} <code>{PACKAGE_STANDARD_LIMITS.productCodeHeader}</code>,{' '}
        <code>{PACKAGE_STANDARD_LIMITS.piecesPerPackageHeader}</code>. {PACKAGE_STANDARD_TEXT.fileReplaces}
      </p>
      <label>
        {PACKAGE_STANDARD_TEXT.sourceLabelLabel}
        <input
          type="text"
          required
          maxLength={PACKAGE_STANDARD_LIMITS.sourceLabelMaximumLength}
          value={sourceLabel}
          onChange={(event) => setSourceLabel(event.target.value)}
        />
      </label>
      <ReasonField value={reason} onChange={setReason} />
      {command.error !== null ? <p role="alert">{command.error}</p> : null}
      <SubmitButtons
        busy={command.busy}
        retrying={command.pending !== null}
        submitText={PACKAGE_STANDARD_TEXT.submit}
        onCancel={() => onDone(null)}
      />
    </form>
  )
}
