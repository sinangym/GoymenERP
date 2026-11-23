/**
 * SAT-D382 / SAT-D383 / SAT-D397 / SAT-D401 (STK-SC36-c): the package standard's approval picture for one product — the version
 * in effect today, this product's customer overrides, the version waiting for approval with its changes, the newest versions and
 * the kept import files with their result reports. A person with `product.package-standards.manage` proposes a customer override;
 * a person with `product.package-standards.approve` (registered, granted to no role) approves the pending version from an
 * effective date or rejects it. Without a permission the server answers 403 and that part shows nothing; an ended session (401),
 * an unwired provider (503) and a failed or off-contract read each say so. The forms check the input only to help the person: the
 * server holds every rule (the date floor, who may decide, the customer's validity).
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { PACKAGE_STANDARD_LIMITS, parsePieces } from './packageImportApi'
import {
  PACKAGE_APPROVAL_PROBLEM,
  approvePackageVersion,
  fetchPackageApprovalForApprover,
  fetchPackageApprovalState,
  fetchPackageImportFiles,
  fetchPackageImportReport,
  isCustomerId,
  isEffectiveDate,
  rejectPackageVersion,
  setPackageCustomerEntry,
  type PackageApprovalCommand,
  type PackageApprovalView,
  type PackageCustomerEntry,
  type PackageImportFile,
  type PackageVersionSummary,
} from './packageApprovalApi'
import { PACKAGE_SOURCE_KIND_LABEL } from './packageImportText'
import { PACKAGE_APPROVAL_TEXT, PACKAGE_IMPORT_OUTCOME_LABEL, PACKAGE_VERSION_STATE_LABEL } from './packageApprovalText'
import styles from './ProductDetailPage.module.css'

const HEADING_ID = 'product-package-approval-heading'
const TEXT = PACKAGE_APPROVAL_TEXT

function Heading() {
  return <h2 id={HEADING_ID}>{TEXT.heading}</h2>
}

function sameId(left: string | null, right: string | null): boolean {
  return left !== null && right !== null && left.toLowerCase() === right.toLowerCase()
}

export function PackageApprovalPanel({
  companyId,
  productId,
  queryNamespace,
}: {
  readonly companyId: string
  readonly productId: string
  readonly queryNamespace: string
}) {
  const state = useQuery({
    queryKey: [queryNamespace, 'product-package-standard-approval-state'],
    queryFn: ({ signal }) => fetchPackageApprovalState(companyId, signal),
  })
  const approver = useQuery({
    queryKey: [queryNamespace, 'product-package-standard-approval-pending'],
    queryFn: ({ signal }) => fetchPackageApprovalForApprover(companyId, signal),
  })
  const [notice, setNotice] = useState<string | null>(null)
  const [customerOpen, setCustomerOpen] = useState(false)

  if (state.isPending) {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} aria-busy="true">
        <Heading />
        <p className={styles.note}>{TEXT.loading}</p>
      </section>
    )
  }

  const outcome = state.data
  if (outcome === undefined || outcome.type === 'forbidden') return null
  if (outcome.type === 'unauthenticated') {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid="package-approval-unauthenticated">
        <Heading />
        <p role="alert">{TEXT.unauthenticated}</p>
      </section>
    )
  }
  if (outcome.type !== 'ok') {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid={`package-approval-${outcome.type}`}>
        <Heading />
        <p role="alert">{outcome.type === 'unavailable' ? TEXT.unavailable : TEXT.failed}</p>
        <button type="button" onClick={() => void state.refetch()}>
          {TEXT.retryRead}
        </button>
      </section>
    )
  }

  const view = outcome.data
  const done = (message: string | null) => {
    setCustomerOpen(false)
    setNotice(message)
    void state.refetch()
    void approver.refetch()
  }
  const overrides = view.customerEntries.filter((entry) => sameId(entry.productId, productId))
  const approverView = approver.data?.type === 'ok' ? approver.data.data : null

  return (
    <section className={styles.section} aria-labelledby={HEADING_ID}>
      <Heading />
      {notice !== null ? <p role="status">{notice}</p> : null}
      <p className={styles.note}>{TEXT.note}</p>
      <dl data-testid="package-approval-effective">
        <div>
          <dt>{TEXT.today}</dt>
          <dd>{view.today}</dd>
        </div>
        <div>
          <dt>{TEXT.headVersion}</dt>
          <dd>{view.headVersion}</dd>
        </div>
        <div>
          <dt>{TEXT.effectiveVersion}</dt>
          <dd>{view.effectiveVersion ?? TEXT.noEffective}</dd>
        </div>
        {view.effectiveVersion !== null ? (
          <div>
            <dt>{TEXT.effectiveFrom}</dt>
            <dd>{view.effectiveFrom ?? TEXT.legacyEffective}</dd>
          </div>
        ) : null}
      </dl>

      <h3>{TEXT.customerHeading}</h3>
      {!view.customerNamesAvailable ? <p className={styles.note}>{TEXT.customerNamesUnavailable}</p> : null}
      <CustomerOverrides entries={overrides} />
      {customerOpen ? (
        <CustomerForm companyId={companyId} productId={productId} view={view} onDone={done} />
      ) : (
        <button type="button" onClick={() => setCustomerOpen(true)}>
          {TEXT.openCustomer}
        </button>
      )}

      <h3>{TEXT.pendingHeading}</h3>
      <PendingVersion view={view} />
      {approverView !== null && approverView.pending !== null ? (
        <ApproverForms companyId={companyId} pending={approverView.pending} today={approverView.today} onDone={done} />
      ) : null}

      <RecentVersions versions={view.recentVersions} />
      <KeptFiles companyId={companyId} queryNamespace={queryNamespace} />
    </section>
  )
}

function customerLabel(customerId: string | null, code: string | null, name: string | null): string {
  if (customerId === null) return TEXT.productDefault
  if (code !== null && name !== null) return `${code} — ${name}`
  return name ?? customerId
}

function CustomerOverrides({ entries }: { readonly entries: readonly PackageCustomerEntry[] }) {
  if (entries.length === 0) return <p data-testid="package-approval-no-overrides">{TEXT.customerEmpty}</p>
  return (
    <table data-testid="package-approval-overrides">
      <caption>{TEXT.customerCaption}</caption>
      <thead>
        <tr>
          <th scope="col">{TEXT.columnCustomer}</th>
          <th scope="col">{TEXT.columnPieces}</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <tr key={entry.customerId}>
            <td>{customerLabel(entry.customerId, entry.customerCode, entry.customerName)}</td>
            <td>{String(entry.piecesPerPackage)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function piecesText(value: number | null): string {
  return value === null ? TEXT.noEntry : String(value)
}

function PendingVersion({ view }: { readonly view: PackageApprovalView }) {
  const pending = view.pending
  if (pending === null) return <p data-testid="package-approval-no-pending">{TEXT.pendingNone}</p>
  return (
    <>
      <dl data-testid="package-approval-pending">
        <div>
          <dt>{TEXT.pendingVersion}</dt>
          <dd>{pending.version}</dd>
        </div>
        <div>
          <dt>{TEXT.pendingSource}</dt>
          <dd>
            {PACKAGE_SOURCE_KIND_LABEL[pending.sourceKind]}
            {pending.fileName !== null ? ` (${pending.fileName})` : ''}
          </dd>
        </div>
        <div>
          <dt>{TEXT.pendingPreparedBy}</dt>
          <dd>{pending.createdBy}</dd>
        </div>
        <div>
          <dt>{TEXT.pendingPreparedAt}</dt>
          <dd>{pending.createdAtUtc}</dd>
        </div>
        <div>
          <dt>{TEXT.pendingReason}</dt>
          <dd>{pending.reason}</dd>
        </div>
      </dl>
      {view.pendingChanges.length === 0 ? (
        <p data-testid="package-approval-no-changes">{TEXT.changesEmpty}</p>
      ) : (
        <table data-testid="package-approval-changes">
          <caption>{TEXT.changesCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{TEXT.columnProduct}</th>
              <th scope="col">{TEXT.columnCustomer}</th>
              <th scope="col">{TEXT.columnBefore}</th>
              <th scope="col">{TEXT.columnAfter}</th>
            </tr>
          </thead>
          <tbody>
            {view.pendingChanges.map((change) => (
              <tr key={`${change.productId}|${change.customerId ?? ''}`}>
                <td>{`${change.productCode} — ${change.productName}`}</td>
                <td>{customerLabel(change.customerId, null, change.customerName)}</td>
                <td>{piecesText(change.piecesBefore)}</td>
                <td>{piecesText(change.piecesAfter)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}

function RecentVersions({ versions }: { readonly versions: readonly PackageVersionSummary[] }) {
  if (versions.length === 0) return null
  return (
    <>
      <h3>{TEXT.recentHeading}</h3>
      <table data-testid="package-approval-versions">
        <caption>{TEXT.recentCaption}</caption>
        <thead>
          <tr>
            <th scope="col">{TEXT.columnVersion}</th>
            <th scope="col">{TEXT.columnState}</th>
            <th scope="col">{TEXT.columnSource}</th>
            <th scope="col">{TEXT.columnEffectiveFrom}</th>
            <th scope="col">{TEXT.columnDecidedBy}</th>
            <th scope="col">{TEXT.columnDecisionReason}</th>
          </tr>
        </thead>
        <tbody>
          {versions.map((version) => (
            <tr key={version.version}>
              <td>{version.version}</td>
              <td>{PACKAGE_VERSION_STATE_LABEL[version.state]}</td>
              <td>{PACKAGE_SOURCE_KIND_LABEL[version.sourceKind]}</td>
              <td>{version.effectiveFrom ?? TEXT.noValue}</td>
              <td>{version.decidedBy ?? TEXT.noValue}</td>
              <td>{version.decisionReason ?? TEXT.noValue}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}

function refusalText(status: number, code: string | null): string {
  switch (code) {
    case PACKAGE_APPROVAL_PROBLEM.piecesInvalid:
      return TEXT.piecesInvalid
    case PACKAGE_APPROVAL_PROBLEM.reasonRequired:
      return TEXT.reasonRequired
    case PACKAGE_APPROVAL_PROBLEM.reasonTooLong:
      return TEXT.reasonTooLong
    case PACKAGE_APPROVAL_PROBLEM.idempotencyConflict:
      return TEXT.idempotencyConflict
    case PACKAGE_APPROVAL_PROBLEM.approvalPending:
      return TEXT.approvalPending
    case PACKAGE_APPROVAL_PROBLEM.preparerCannotDecide:
      return TEXT.preparerCannotDecide
    case PACKAGE_APPROVAL_PROBLEM.effectiveDateInvalid:
      return TEXT.effectiveDateInvalid
    case PACKAGE_APPROVAL_PROBLEM.productInactive:
      return TEXT.productInactive
    case PACKAGE_APPROVAL_PROBLEM.customerNotFound:
      return TEXT.customerNotFound
    case PACKAGE_APPROVAL_PROBLEM.customerProviderUnavailable:
      return TEXT.customerProviderUnavailable
    default:
      return status === 404 && code === null ? TEXT.productMissing : TEXT.refused
  }
}

type PendingKey = { readonly key: string; readonly fingerprint: string } | null

/** One command under a caller-owned key: reused only for the same body after an unknown outcome; a definite answer drops it. */
function useCommand(
  onDone: (notice: string | null) => void,
  texts: { readonly applied: string; readonly replayed: string },
) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<PendingKey>(null)

  async function run(fingerprint: string, call: (key: string) => Promise<PackageApprovalCommand>) {
    const key = pending !== null && pending.fingerprint === fingerprint ? pending.key : crypto.randomUUID()
    setBusy(true)
    setError(null)
    const outcome = await call(key)
    setBusy(false)
    if (outcome.type === 'unknown') {
      setPending({ key, fingerprint })
      setError(TEXT.unknownOutcome)
      return
    }
    setPending(null)
    if (outcome.type === 'applied') {
      onDone(outcome.replayed ? texts.replayed : texts.applied)
    } else if (outcome.code === PACKAGE_APPROVAL_PROBLEM.versionConflict) {
      onDone(TEXT.conflict)
    } else if (outcome.code === PACKAGE_APPROVAL_PROBLEM.versionNotPending) {
      onDone(TEXT.versionNotPending)
    } else {
      setError(refusalText(outcome.status, outcome.code))
    }
  }

  return { busy, error, setError, pending, run }
}

function SubmitButton({
  busy,
  retrying,
  text,
}: {
  readonly busy: boolean
  readonly retrying: boolean
  readonly text: string
}) {
  return (
    <button type="submit" disabled={busy} aria-busy={busy}>
      {busy ? TEXT.submitting : retrying ? TEXT.retrySame : text}
    </button>
  )
}

function ReasonField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <label>
      {TEXT.reasonLabel}
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

function CustomerForm({
  companyId,
  productId,
  view,
  onDone,
}: {
  readonly companyId: string
  readonly productId: string
  readonly view: PackageApprovalView
  readonly onDone: (notice: string | null) => void
}) {
  const [customerId, setCustomerId] = useState('')
  const [pieces, setPieces] = useState('')
  const [reason, setReason] = useState('')
  const command = useCommand(onDone, { applied: TEXT.customerProposed, replayed: TEXT.customerReplayed })

  async function submit(event: FormEvent) {
    event.preventDefault()
    const customer = customerId.trim()
    if (!isCustomerId(customer)) {
      command.setError(TEXT.customerIdInvalid)
      return
    }
    let piecesPerPackage: number | null = null
    if (pieces.trim() !== '') {
      piecesPerPackage = parsePieces(pieces)
      if (piecesPerPackage === null) {
        command.setError(TEXT.piecesInvalid)
        return
      }
    }
    const why = reason.trim()
    if (why === '') {
      command.setError(TEXT.reasonRequired)
      return
    }
    const body = { expectedVersion: view.headVersion, piecesPerPackage, reason: why }
    await command.run(JSON.stringify({ customer, ...body }), (key) =>
      setPackageCustomerEntry(companyId, customer, productId, key, body))
  }

  return (
    <form onSubmit={(event) => void submit(event)} aria-label={TEXT.customerFormLabel} noValidate>
      <label>
        {TEXT.customerIdLabel}
        <input type="text" value={customerId} onChange={(event) => setCustomerId(event.target.value)} />
      </label>
      <label>
        {TEXT.piecesLabel}
        <input type="text" inputMode="numeric" value={pieces} onChange={(event) => setPieces(event.target.value)} />
      </label>
      <ReasonField value={reason} onChange={setReason} />
      {command.error !== null ? <p role="alert">{command.error}</p> : null}
      <SubmitButton busy={command.busy} retrying={command.pending !== null} text={TEXT.submitCustomer} />
      <button type="button" disabled={command.busy} onClick={() => onDone(null)}>
        {TEXT.cancel}
      </button>
    </form>
  )
}

function ApproverForms({
  companyId,
  pending,
  today,
  onDone,
}: {
  readonly companyId: string
  readonly pending: PackageVersionSummary
  readonly today: string
  readonly onDone: (notice: string | null) => void
}) {
  const [effectiveFrom, setEffectiveFrom] = useState(today)
  const [approveReason, setApproveReason] = useState('')
  const [rejectReason, setRejectReason] = useState('')
  const approve = useCommand(onDone, { applied: TEXT.approved, replayed: TEXT.approvedReplayed })
  const reject = useCommand(onDone, { applied: TEXT.rejected, replayed: TEXT.rejectedReplayed })

  async function submitApprove(event: FormEvent) {
    event.preventDefault()
    // ISO dates compare as text; the server checks the floor again against its own business day.
    if (!isEffectiveDate(effectiveFrom) || effectiveFrom < today) {
      approve.setError(TEXT.effectiveFromInvalid)
      return
    }
    const why = approveReason.trim()
    if (why === '') {
      approve.setError(TEXT.reasonRequired)
      return
    }
    const body = { effectiveFrom, reason: why }
    await approve.run(JSON.stringify({ version: pending.version, ...body }), (key) =>
      approvePackageVersion(companyId, pending.version, key, body))
  }

  async function submitReject(event: FormEvent) {
    event.preventDefault()
    const why = rejectReason.trim()
    if (why === '') {
      reject.setError(TEXT.reasonRequired)
      return
    }
    const body = { reason: why }
    await reject.run(JSON.stringify({ version: pending.version, ...body }), (key) =>
      rejectPackageVersion(companyId, pending.version, key, body))
  }

  const busy = approve.busy || reject.busy
  return (
    <div data-testid="package-approval-decide">
      <h4>{TEXT.approverHeading}</h4>
      <form onSubmit={(event) => void submitApprove(event)} aria-label={TEXT.approveFormLabel} noValidate>
        <label>
          {TEXT.effectiveFromLabel}
          <input type="date" min={today} value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} />
        </label>
        <ReasonField value={approveReason} onChange={setApproveReason} />
        {approve.error !== null ? <p role="alert">{approve.error}</p> : null}
        <button type="submit" disabled={busy} aria-busy={approve.busy}>
          {approve.busy ? TEXT.submitting : approve.pending !== null ? TEXT.retrySame : TEXT.approve}
        </button>
      </form>
      <form onSubmit={(event) => void submitReject(event)} aria-label={TEXT.rejectFormLabel} noValidate>
        <ReasonField value={rejectReason} onChange={setRejectReason} />
        {reject.error !== null ? <p role="alert">{reject.error}</p> : null}
        <button type="submit" disabled={busy} aria-busy={reject.busy}>
          {reject.busy ? TEXT.submitting : reject.pending !== null ? TEXT.retrySame : TEXT.reject}
        </button>
      </form>
    </div>
  )
}

function KeptFiles({ companyId, queryNamespace }: { readonly companyId: string; readonly queryNamespace: string }) {
  const files = useQuery({
    queryKey: [queryNamespace, 'product-package-standard-import-files'],
    queryFn: ({ signal }) => fetchPackageImportFiles(companyId, signal),
  })
  const [opened, setOpened] = useState<string | null>(null)

  if (files.isPending) {
    return (
      <div aria-busy="true">
        <h3>{TEXT.filesHeading}</h3>
        <p className={styles.note}>{TEXT.filesLoading}</p>
      </div>
    )
  }
  const outcome = files.data
  if (outcome === undefined || outcome.type === 'forbidden') return null
  if (outcome.type !== 'ok') {
    return (
      <div data-testid={`package-files-${outcome.type}`}>
        <h3>{TEXT.filesHeading}</h3>
        <p role="alert">
          {outcome.type === 'unauthenticated'
            ? TEXT.unauthenticated
            : outcome.type === 'unavailable'
              ? TEXT.unavailable
              : TEXT.filesFailed}
        </p>
        <button type="button" onClick={() => void files.refetch()}>
          {TEXT.retryRead}
        </button>
      </div>
    )
  }

  return (
    <div>
      <h3>{TEXT.filesHeading}</h3>
      <p className={styles.note}>{TEXT.filesNote}</p>
      {outcome.data.length === 0 ? (
        <p data-testid="package-files-empty">{TEXT.filesEmpty}</p>
      ) : (
        <table data-testid="package-files">
          <caption>{TEXT.filesCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{TEXT.columnFile}</th>
              <th scope="col">{TEXT.columnOutcome}</th>
              <th scope="col">{TEXT.columnRows}</th>
              <th scope="col">{TEXT.columnErrors}</th>
              <th scope="col">{TEXT.columnCreatedAt}</th>
              <th scope="col">{TEXT.columnCreatedBy}</th>
              <th scope="col">
                <span className={styles.note}>{TEXT.showReport}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {outcome.data.map((file) => (
              <FileRow
                key={file.importId}
                file={file}
                opened={opened === file.importId}
                onToggle={() => setOpened(opened === file.importId ? null : file.importId)}
              />
            ))}
          </tbody>
        </table>
      )}
      {opened !== null ? <ImportReport companyId={companyId} importId={opened} queryNamespace={queryNamespace} /> : null}
    </div>
  )
}

function FileRow({
  file,
  opened,
  onToggle,
}: {
  readonly file: PackageImportFile
  readonly opened: boolean
  readonly onToggle: () => void
}) {
  return (
    <tr>
      <td>{file.fileName}</td>
      <td>{PACKAGE_IMPORT_OUTCOME_LABEL[file.outcome]}</td>
      <td>{String(file.rowCount)}</td>
      <td>{String(file.errorCount)}</td>
      <td>{file.createdAtUtc}</td>
      <td>{file.createdBy}</td>
      <td>
        <button type="button" aria-expanded={opened} aria-label={`${opened ? TEXT.hideReport : TEXT.showReport}: ${file.fileName}`} onClick={onToggle}>
          {opened ? TEXT.hideReport : TEXT.showReport}
        </button>
      </td>
    </tr>
  )
}

function ImportReport({
  companyId,
  importId,
  queryNamespace,
}: {
  readonly companyId: string
  readonly importId: string
  readonly queryNamespace: string
}) {
  const report = useQuery({
    queryKey: [queryNamespace, 'product-package-standard-import-report', importId],
    queryFn: ({ signal }) => fetchPackageImportReport(companyId, importId, signal),
  })
  if (report.isPending) return <p aria-busy="true">{TEXT.reportLoading}</p>
  const outcome = report.data
  if (outcome === undefined || outcome.type === 'forbidden') return null
  if (outcome.type !== 'ok') {
    return (
      <p role="alert" data-testid={`package-report-${outcome.type}`}>
        {outcome.type === 'missing'
          ? TEXT.reportMissing
          : outcome.type === 'unauthenticated'
            ? TEXT.unauthenticated
            : outcome.type === 'unavailable'
              ? TEXT.unavailable
              : TEXT.reportFailed}
      </p>
    )
  }
  const data = outcome.data
  return (
    <div data-testid="package-report">
      <h4>{`${TEXT.reportHeading}: ${data.fileName}`}</h4>
      <p>{data.detail}</p>
      <dl>
        <div>
          <dt>{TEXT.columnOutcome}</dt>
          <dd>{PACKAGE_IMPORT_OUTCOME_LABEL[data.outcome]}</dd>
        </div>
        <div>
          <dt>{TEXT.reportRows}</dt>
          <dd>{String(data.rowCount)}</dd>
        </div>
        <div>
          <dt>{TEXT.reportAccepted}</dt>
          <dd>{String(data.acceptedCount)}</dd>
        </div>
        <div>
          <dt>{TEXT.reportSha}</dt>
          <dd>
            <code>{data.fileSha256}</code>
          </dd>
        </div>
      </dl>
      {data.errors.length === 0 ? (
        <p>{TEXT.reportNoErrors}</p>
      ) : (
        <ul aria-label={TEXT.reportErrorsCaption}>
          {data.errors.map((error, index) => (
            <li key={`${String(index)}-${error}`}>{error}</li>
          ))}
        </ul>
      )}
    </div>
  )
}
