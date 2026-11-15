/**
 * KLN-D54 (STK-S37) — the "Yapısal profil ağırlık tablosu" screen of Ayarlar: shows the selected company's current table with its
 * version, source label, reason and who imported it, and imports a new version from a `series;size;kgPerMetre` text file (the
 * rows only; the file is not uploaded, B-26). Every state is shown: loading, an ended session, no permission (nothing is
 * shown), an unwired provider, an off-contract answer, no table yet ("kaynak yok"), a version conflict, a refusal with its
 * reason and an unknown outcome whose key is kept for the same retry. The mount under Ayarlar belongs to the shell/settings lane.
 */

import { useState, type ChangeEvent, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  PROFILE_WEIGHT_PROBLEM,
  PROFILE_WEIGHT_REASON_MAXIMUM_LENGTH,
  PROFILE_WEIGHT_SOURCE_LABEL_MAXIMUM_LENGTH,
  fetchProfileWeightTable,
  importProfileWeightTable,
  parseProfileWeightFile,
  type ProfileWeightRow,
  type ProfileWeightTableView,
} from './profileWeightApi'
import { PROFILE_WEIGHT_TEXT, parseRefusalMessage } from './profileWeightText'
import styles from './ProductDetailPage.module.css'

const HEADING_ID = 'profile-weight-heading'
const SOURCE_HINT_ID = 'profile-weight-source-hint'
const FORMAT_NOTE_ID = 'profile-weight-format-note'

function Heading() {
  return <h2 id={HEADING_ID}>{PROFILE_WEIGHT_TEXT.heading}</h2>
}

export function ProfileWeightTablePanel({
  companyId,
  queryNamespace,
}: {
  readonly companyId: string
  readonly queryNamespace: string
}) {
  const query = useQuery({
    queryKey: [queryNamespace, 'product-profile-weight-table', companyId],
    queryFn: ({ signal }) => fetchProfileWeightTable(companyId, signal),
  })
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
      <p className={styles.note}>{PROFILE_WEIGHT_TEXT.intro}</p>
      {notice !== null ? <p role="status">{notice}</p> : null}
      {view.version === '0' ? <p data-testid="profile-weight-none">{PROFILE_WEIGHT_TEXT.none}</p> : <CurrentTable view={view} />}
      <ImportForm
        key={view.version}
        companyId={companyId}
        view={view}
        onDone={(message) => {
          setNotice(message)
          void query.refetch()
        }}
      />
    </section>
  )
}

function CurrentTable({ view }: { readonly view: ProfileWeightTableView }) {
  return (
    <>
      <dl data-testid="profile-weight-header">
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.versionLabel}</dt>
          <dd>{view.version}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.sourceLabel}</dt>
          <dd>{view.sourceLabel}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.reasonShownLabel}</dt>
          <dd>{view.reason}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.importedAtLabel}</dt>
          <dd>{view.importedAtUtc}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.importedByLabel}</dt>
          <dd>{view.importedBy}</dd>
        </div>
        <div>
          <dt>{PROFILE_WEIGHT_TEXT.rowCountLabel}</dt>
          <dd>{view.rows.length}</dd>
        </div>
      </dl>
      <table data-testid="profile-weight-rows">
        <caption>{PROFILE_WEIGHT_TEXT.tableCaption}</caption>
        <thead>
          <tr>
            <th scope="col">{PROFILE_WEIGHT_TEXT.seriesColumn}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.sizeColumn}</th>
            <th scope="col">{PROFILE_WEIGHT_TEXT.weightColumn}</th>
          </tr>
        </thead>
        <tbody>
          {view.rows.map((row) => (
            <tr key={`${row.series} ${row.size}`}>
              <td>{row.series}</td>
              <td>{row.size}</td>
              <td>{String(row.kgPerMetre)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}

function ImportForm({
  companyId,
  view,
  onDone,
}: {
  readonly companyId: string
  readonly view: ProfileWeightTableView
  readonly onDone: (notice: string) => void
}) {
  const [rows, setRows] = useState<readonly ProfileWeightRow[] | null>(null)
  const [fileMessage, setFileMessage] = useState<string | null>(null)
  const [source, setSource] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<{ readonly key: string; readonly fingerprint: string } | null>(null)

  async function chooseFile(event: ChangeEvent<HTMLInputElement>) {
    setRows(null)
    setError(null)
    const file = event.target.files?.[0]
    if (file === undefined) {
      setFileMessage(null)
      return
    }
    let text: string
    try {
      text = await file.text()
    } catch {
      setFileMessage(PROFILE_WEIGHT_TEXT.fileUnreadable)
      return
    }
    const parsed = parseProfileWeightFile(text)
    if (!parsed.ok) {
      setFileMessage(parseRefusalMessage(parsed.reason, parsed.line))
      return
    }
    setRows(parsed.rows)
    setFileMessage(PROFILE_WEIGHT_TEXT.parsedRows(parsed.rows.length))
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    const sourceLabel = source.trim()
    const why = reason.trim()
    const refusal = rows === null ? PROFILE_WEIGHT_TEXT.fileRequired
      : sourceLabel === '' ? PROFILE_WEIGHT_TEXT.sourceRequired
        : sourceLabel.length > PROFILE_WEIGHT_SOURCE_LABEL_MAXIMUM_LENGTH ? PROFILE_WEIGHT_TEXT.sourceTooLong
          : why === '' ? PROFILE_WEIGHT_TEXT.reasonRequired
            : why.length > PROFILE_WEIGHT_REASON_MAXIMUM_LENGTH ? PROFILE_WEIGHT_TEXT.reasonTooLong
              : null
    if (refusal !== null || rows === null) {
      setError(refusal ?? PROFILE_WEIGHT_TEXT.fileRequired)
      return
    }
    const body = { expectedVersion: view.version, sourceLabel, reason: why, rows }
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
      return
    }
    if (outcome.code === PROFILE_WEIGHT_PROBLEM.versionConflict) {
      onDone(PROFILE_WEIGHT_TEXT.conflict)
      return
    }
    setError(
      outcome.code === PROFILE_WEIGHT_PROBLEM.rowsInvalid
        ? outcome.detail === null ? PROFILE_WEIGHT_TEXT.rowsRefused : `${PROFILE_WEIGHT_TEXT.rowsRefused} ${outcome.detail}`
        : outcome.code === PROFILE_WEIGHT_PROBLEM.sourceRequired ? PROFILE_WEIGHT_TEXT.sourceRequired
          : outcome.code === PROFILE_WEIGHT_PROBLEM.sourceTooLong ? PROFILE_WEIGHT_TEXT.sourceTooLong
            : outcome.code === PROFILE_WEIGHT_PROBLEM.reasonRequired ? PROFILE_WEIGHT_TEXT.reasonRequired
              : outcome.code === PROFILE_WEIGHT_PROBLEM.reasonTooLong ? PROFILE_WEIGHT_TEXT.reasonTooLong
                : PROFILE_WEIGHT_TEXT.refused,
    )
  }

  return (
    <form onSubmit={(event) => void submit(event)} aria-label={PROFILE_WEIGHT_TEXT.importHeading} noValidate>
      <h3>{PROFILE_WEIGHT_TEXT.importHeading}</h3>
      <p id={FORMAT_NOTE_ID} className={styles.note}>{PROFILE_WEIGHT_TEXT.formatNote}</p>
      <label>
        {PROFILE_WEIGHT_TEXT.fileLabel}
        <input
          type="file"
          accept=".txt,.csv,text/plain,text/csv"
          aria-describedby={FORMAT_NOTE_ID}
          disabled={busy}
          onChange={(event) => void chooseFile(event)}
        />
      </label>
      {fileMessage !== null ? <p role={rows === null ? 'alert' : 'status'}>{fileMessage}</p> : null}
      <label>
        {PROFILE_WEIGHT_TEXT.sourceInputLabel}
        <input
          type="text"
          required
          maxLength={PROFILE_WEIGHT_SOURCE_LABEL_MAXIMUM_LENGTH}
          aria-describedby={SOURCE_HINT_ID}
          value={source}
          onChange={(event) => setSource(event.target.value)}
        />
      </label>
      <p id={SOURCE_HINT_ID} className={styles.note}>{PROFILE_WEIGHT_TEXT.sourceHint}</p>
      <label>
        {PROFILE_WEIGHT_TEXT.reasonLabel}
        <textarea
          required
          rows={2}
          maxLength={PROFILE_WEIGHT_REASON_MAXIMUM_LENGTH}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
      </label>
      {error !== null ? <p role="alert">{error}</p> : null}
      <button type="submit" disabled={busy} aria-busy={busy}>
        {busy ? PROFILE_WEIGHT_TEXT.submitting : pending !== null ? PROFILE_WEIGHT_TEXT.retrySame : PROFILE_WEIGHT_TEXT.submit}
      </button>
    </form>
  )
}
