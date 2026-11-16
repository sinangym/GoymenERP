/**
 * URN-D13…D16 (STK-S37R): the selected company's kg/m table of profiles and round tubes — the only source of a card's kg/m.
 * Without `product.profile-weights.import` (granted to no role here) the server answers 403 and the panel shows nothing; an
 * ended session (401), an unwired provider (503) and a failed read each say so instead of "tablo yok". The import sends the
 * whole file with a mandatory source label and reason; the server reads the rows and keeps the file in the document vault.
 * While no vault or scanner can take the file the import is refused (503) and nothing changes. The form checks the file's name
 * and size only to help the person; the server holds every rule.
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  PROFILE_WEIGHT_LIMITS,
  PROFILE_WEIGHT_PROBLEM,
  bytesToBase64,
  checkFile,
  fetchProfileWeightTable,
  importProfileWeightTable,
  type ProfileWeightTableView,
} from './profileWeightApi'
import { PROFILE_WEIGHT_TEXT, TABLE_KIND_LABEL } from './profileWeightText'
import styles from './ProductDetailPage.module.css'

const HEADING_ID = 'product-profile-weight-heading'

function Heading() {
  return <h2 id={HEADING_ID}>{PROFILE_WEIGHT_TEXT.heading}</h2>
}

function cell(value: number | null): string {
  return value === null ? PROFILE_WEIGHT_TEXT.noValue : String(value)
}

export function ProfileWeightTablePanel({
  companyId,
  queryNamespace,
}: {
  readonly companyId: string
  readonly queryNamespace: string
}) {
  const query = useQuery({
    queryKey: [queryNamespace, 'product-profile-weight-table'],
    queryFn: ({ signal }) => fetchProfileWeightTable(companyId, signal),
  })
  const [importing, setImporting] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

  if (query.isPending) {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} aria-busy="true">
        <Heading />
        <p className={styles.note}>{PROFILE_WEIGHT_TEXT.loading}</p>
      </section>
    )
  }

  const outcome = query.data
  if (outcome === undefined || outcome.type === 'forbidden') return null
  if (outcome.type === 'unauthenticated') {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid="profile-weight-unauthenticated">
        <Heading />
        <p role="alert">{PROFILE_WEIGHT_TEXT.unauthenticated}</p>
      </section>
    )
  }

  if (outcome.type !== 'ok') {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid={`profile-weight-${outcome.type}`}>
        <Heading />
        <p role="alert">{outcome.type === 'unavailable' ? PROFILE_WEIGHT_TEXT.unavailable : PROFILE_WEIGHT_TEXT.failed}</p>
        <button type="button" onClick={() => void query.refetch()}>
          {PROFILE_WEIGHT_TEXT.retryRead}
        </button>
      </section>
    )
  }

  const view = outcome.data
  return (
    <section className={styles.section} aria-labelledby={HEADING_ID}>
      <Heading />
      {notice !== null ? <p role="status">{notice}</p> : null}
      <p className={styles.note}>{PROFILE_WEIGHT_TEXT.note}</p>
      {view.imported === null ? (
        <p data-testid="profile-weight-empty">{PROFILE_WEIGHT_TEXT.empty}</p>
      ) : (
        <TableView view={view} />
      )}
      {importing ? (
        <ImportForm
          companyId={companyId}
          view={view}
          onDone={(message) => {
            setImporting(false)
            setNotice(message)
            void query.refetch()
          }}
        />
      ) : (
        <button type="button" onClick={() => setImporting(true)}>
          {PROFILE_WEIGHT_TEXT.openImport}
        </button>
      )}
    </section>
  )
}

function TableView({ view }: { readonly view: ProfileWeightTableView }) {
  const imported = view.imported
  if (imported === null) return null
  return (
    <>
      <dl data-testid="profile-weight-header">
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.version}</dt>
          <dd>{view.version}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.sourceLabel}</dt>
          <dd>{imported.sourceLabel}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.fileName}</dt>
          <dd>{imported.fileName}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.importedAt}</dt>
          <dd>{imported.importedAtUtc}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.importedBy}</dt>
          <dd>{imported.importedBy}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.rowCount}</dt>
          <dd>{view.rows.length}</dd>
        </div>
      </dl>
      <table data-testid="profile-weight-rows">
        <caption>{PROFILE_WEIGHT_TEXT.tableCaption}</caption>
        <thead>
          <tr>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnKind}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnWidth}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnHeight}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnDiameter}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnWall}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnKg}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnSeries}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.columnSize}</th>
          </tr>
        </thead>
        <tbody>
          {view.rows.map((row, index) => (
            <tr key={index}>
              <td>{TABLE_KIND_LABEL[row.sectionKind]}</td>
              <td>{cell(row.widthMm)}</td>
              <td>{cell(row.heightMm)}</td>
              <td>{cell(row.outerDiameterMm)}</td>
              <td>{cell(row.wallThicknessMm)}</td>
              <td>{String(row.kgPerMetre)}</td>
              <td>{row.profileSeries ?? PROFILE_WEIGHT_TEXT.noValue}</td>
              <td>{row.profileSize ?? PROFILE_WEIGHT_TEXT.noValue}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}

const FILE_CHECK_TEXT = {
  'not-csv': PROFILE_WEIGHT_TEXT.fileNotCsv,
  'name-too-long': PROFILE_WEIGHT_TEXT.fileNameTooLong,
  empty: PROFILE_WEIGHT_TEXT.fileEmpty,
  'too-large': PROFILE_WEIGHT_TEXT.fileTooLarge,
} as const

function refusalText(code: string | null, detail: string | null): string {
  switch (code) {
    case PROFILE_WEIGHT_PROBLEM.fileInvalid:
      return detail === null ? PROFILE_WEIGHT_TEXT.fileInvalid : `${PROFILE_WEIGHT_TEXT.fileInvalid} ${detail}`
    case PROFILE_WEIGHT_PROBLEM.fileRejected:
      return PROFILE_WEIGHT_TEXT.fileRejected
    case PROFILE_WEIGHT_PROBLEM.fileVaultUnavailable:
      return PROFILE_WEIGHT_TEXT.vaultUnavailable
    case PROFILE_WEIGHT_PROBLEM.sourceLabelRequired:
      return PROFILE_WEIGHT_TEXT.sourceLabelRequired
    case PROFILE_WEIGHT_PROBLEM.sourceLabelTooLong:
      return PROFILE_WEIGHT_TEXT.sourceLabelTooLong
    case PROFILE_WEIGHT_PROBLEM.reasonRequired:
      return PROFILE_WEIGHT_TEXT.reasonRequired
    case PROFILE_WEIGHT_PROBLEM.reasonTooLong:
      return PROFILE_WEIGHT_TEXT.reasonTooLong
    case PROFILE_WEIGHT_PROBLEM.idempotencyConflict:
      return PROFILE_WEIGHT_TEXT.idempotencyConflict
    default:
      return PROFILE_WEIGHT_TEXT.refused
  }
}

function ImportForm({
  companyId,
  view,
  onDone,
}: {
  readonly companyId: string
  readonly view: ProfileWeightTableView
  readonly onDone: (notice: string | null) => void
}) {
  const [file, setFile] = useState<File | null>(null)
  const [sourceLabel, setSourceLabel] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<{ readonly key: string; readonly fingerprint: string } | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (file === null) {
      setError(PROFILE_WEIGHT_TEXT.fileRequired)
      return
    }
    const fileCheck = checkFile(file)
    if (fileCheck !== 'ok') {
      setError(FILE_CHECK_TEXT[fileCheck])
      return
    }
    const label = sourceLabel.trim()
    const why = reason.trim()
    if (label === '') {
      setError(PROFILE_WEIGHT_TEXT.sourceLabelRequired)
      return
    }
    if (why === '') {
      setError(PROFILE_WEIGHT_TEXT.reasonRequired)
      return
    }
    let contentBase64: string
    try {
      contentBase64 = bytesToBase64(new Uint8Array(await file.arrayBuffer()))
    } catch {
      setError(PROFILE_WEIGHT_TEXT.fileUnreadable)
      return
    }
    const body = { expectedVersion: view.version, sourceLabel: label, reason: why, fileName: file.name, contentBase64 }
    const fingerprint = JSON.stringify(body)
    const key = pending !== null && pending.fingerprint === fingerprint ? pending.key : crypto.randomUUID()
    setBusy(true)
    setError(null)
    const outcome = await importProfileWeightTable(companyId, key, body)
    setBusy(false)
    if (outcome.type === 'unknown') {
      setPending({ key, fingerprint })
      setError(PROFILE_WEIGHT_TEXT.unknownOutcome)
      return
    }

    setPending(null)
    if (outcome.type === 'applied') {
      onDone(outcome.replayed ? PROFILE_WEIGHT_TEXT.replayed : PROFILE_WEIGHT_TEXT.imported)
    } else if (outcome.code === PROFILE_WEIGHT_PROBLEM.versionConflict) {
      onDone(PROFILE_WEIGHT_TEXT.conflict)
    } else {
      setError(refusalText(outcome.code, outcome.detail))
    }
  }

  return (
    <form onSubmit={(event) => void submit(event)} aria-label={PROFILE_WEIGHT_TEXT.formLabel} noValidate>
      <label>
        {PROFILE_WEIGHT_TEXT.fileLabel}
        <input
          type="file"
          accept={`${PROFILE_WEIGHT_LIMITS.fileExtension},text/csv`}
          required
          onChange={(event) => {
            setFile(event.target.files?.[0] ?? null)
            setError(null)
          }}
        />
      </label>
      <p className={styles.note}>
        {PROFILE_WEIGHT_TEXT.fileFormat} <code>{PROFILE_WEIGHT_LIMITS.header}</code>
      </p>
      <p className={styles.note}>
        {PROFILE_WEIGHT_TEXT.extendedFileFormat} <code>{PROFILE_WEIGHT_LIMITS.extendedHeader}</code>
      </p>
      <label>
        {PROFILE_WEIGHT_TEXT.sourceLabelLabel}
        <input
          type="text"
          required
          maxLength={PROFILE_WEIGHT_LIMITS.sourceLabelMaximumLength}
          value={sourceLabel}
          onChange={(event) => setSourceLabel(event.target.value)}
        />
      </label>
      <label>
        {PROFILE_WEIGHT_TEXT.reasonLabel}
        <textarea
          required
          rows={2}
          maxLength={PROFILE_WEIGHT_LIMITS.reasonMaximumLength}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
      </label>
      {error !== null ? <p role="alert">{error}</p> : null}
      <button type="submit" disabled={busy} aria-busy={busy}>
        {busy ? PROFILE_WEIGHT_TEXT.submitting : pending !== null ? PROFILE_WEIGHT_TEXT.retrySame : PROFILE_WEIGHT_TEXT.submit}
      </button>
      <button type="button" disabled={busy} onClick={() => onDone(null)}>
        {PROFILE_WEIGHT_TEXT.cancel}
      </button>
    </form>
  )
}
