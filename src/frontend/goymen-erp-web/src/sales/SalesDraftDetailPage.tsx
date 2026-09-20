/**
 * S-04 Sipariş 360 — the draft as `GET /api/v1/sales/drafts/{id}` describes it (#257), with the
 * SALES-A1 internal approval axis (DEC-SAL-001/002; Business 5737989917).
 *
 * Only what the detail carries:
 *  - the lifecycle state (Draft), the internal approval state, the commercial revision
 *    (`Rev.0`) and the concurrency version are FOUR separate facts, never merged;
 *  - the approval facts (who submitted / decided, when, the reason) are shown as stored,
 *    "not yet" when null — never derived, never defaulted;
 *  - a null is shown as unknown, never as zero, cash, today or "none";
 *  - "unplanned" is the quantity not yet placed on a dated slice; nothing has been
 *    delivered, reserved or allocated on a draft — the linked effects line says so honestly
 *    (D4: allocation 0 / loading 0, no consumer in this slice).
 *
 * Commands: "Onaya gönder" follows the create capability (whoever may create may submit);
 * "Onayla" / "Reddet" follow the `sales.orders.approve` control the shell publishes only to an
 * actor holding the approve permission beside the read. The shell decides the buttons; the API
 * measures the grant afresh on every send. Each command is one idempotent attempt (the same rules
 * as the create: one key per snapshot, an unknown outcome keeps its key, a retry resends the same
 * snapshot). After a recorded transition the detail is re-read — the answer is the server's.
 *
 * SALES-A2a — the "Teslimat" block: the stored delivery address / contact references (ids + the owner's
 * versions) are shown with the Cari owner's CURRENT texts, read afresh through the contact-information
 * reference; the currency id with its code from the currencies reference for the business date. A
 * reference the owner no longer lists says so beside the id; a read that fails names its reason; nothing
 * is guessed and no read hides the draft. The reference reads are bound to `sales.drafts.create` on the
 * server, so an actor without that grant is told the names cannot be read, and nothing is asked.
 *
 * No allocation, shipment, invoice or money tab: none exists for a draft.
 */

import { useContext, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchDraftDetail, sendDraftCommand } from './api'
import {
  APPROVAL_STATE,
  OWNER_REFERENCE_FIELDS,
  type ContactInformationOptions,
  type DraftDetail,
  type DraftLine,
  type DraftMutation,
  type DraftStateCommand,
  type DraftStateCommandRequest,
  type OwnerReference,
} from './contract'
import { unplannedQuantity } from './deliveryPlan'
import { contactLabel, formatBusinessDate, formatInstant } from './format'
import {
  SalesContactInformationSourceContext,
  SalesCurrencySourceContext,
  type ContactInformationReadState,
} from './referenceSource'
import {
  abandon,
  applyOutcome,
  beginSend,
  decideSubmit,
  generateOrderAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  orderAttemptBinding,
  type OrderAttemptSnapshot,
  type OrderAttemptState,
} from './orderAttempt'
import { SALES_CAPABILITY, SALES_ROUTE } from './capabilities'
import { APPROVAL_STATE_TEXT, ORDER_DETAIL_TEXT as T, ORDER_DRAFT_TEXT, problemCodeText } from './salesText'
import { SALES_PROBLEM_CODE } from './contract'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'

const orUnknown = (value: string | null) => value ?? T.unknown

export function SalesDraftDetailPage() {
  const shell = useShellState()
  const { draftId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()

  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft', draftId ?? '']
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && (draftId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftDetail(draftId as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = hasCapability(shell, SALES_CAPABILITY.draftsList) ? (
    <Link className={styles.backLink} to={SALES_ROUTE.list}>
      ← {T.backToList}
    </Link>
  ) : null

  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label="Sipariş taslağı">
        {backLink}
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject="sipariş taslağı"
          onRetry={() => void query.refetch()}
        />
      </section>
    )
  }
  return (
    <DraftView
      detail={outcome.data}
      backLink={backLink}
      delivery={companyId === null ? null : <DeliveryBlock shell={shell} detail={outcome.data} companyId={companyId} />}
      commands={
        companyId === null ? null : (
          <ApprovalCommands
            key={`${namespace ?? ''}:${outcome.data.draft.id}`}
            shell={shell}
            detail={outcome.data}
            companyId={companyId}
            onRecorded={() => void queryClient.invalidateQueries({ queryKey })}
          />
        )
      }
    />
  )
}

export function DraftView({
  detail,
  backLink,
  commands = null,
  delivery = null,
}: {
  readonly detail: DraftDetail
  readonly backLink: React.ReactNode
  readonly commands?: React.ReactNode
  /** SALES-A2a: the "Teslimat" block (the owner's texts for the stored references); null when no company is bound. */
  readonly delivery?: React.ReactNode
}) {
  const { draft, approval } = detail
  return (
    <article className={styles.page} aria-labelledby="sales-detail-heading">
      {backLink}
      <header className={styles.header}>
        <div>
          <h1 id="sales-detail-heading" className={styles.title}>
            {draft.documentNo}
          </h1>
          <p className={styles.muted}>{T.referenceNote}</p>
        </div>
        <span className={styles.badge} data-testid="sales-draft-state">
          {T.stateDraft} · {APPROVAL_STATE_TEXT[draft.approvalState]}
        </span>
      </header>

      <dl className={styles.facts}>
        <div>
          <dt>{T.approval.state}</dt>
          <dd data-testid="sales-draft-approval-state">{APPROVAL_STATE_TEXT[draft.approvalState]}</dd>
        </div>
        <div>
          <dt>{T.revision}</dt>
          <dd data-testid="sales-draft-revision">{draft.revision}</dd>
        </div>
        <div>
          <dt>{T.version}</dt>
          <dd data-testid="sales-draft-version">{draft.version}</dd>
        </div>
        <div>
          <dt>{T.businessDate}</dt>
          <dd>{formatBusinessDate(draft.businessDate)}</dd>
        </div>
        <div>
          <dt>{T.createdAt}</dt>
          <dd>{formatInstant(draft.createdAtUtc)}</dd>
        </div>
        <div>
          <dt>{T.party}</dt>
          <dd className={styles.identifier}>{draft.partyId}</dd>
        </div>
        <div>
          <dt>{T.account}</dt>
          <dd className={styles.identifier}>{draft.companyAccountId}</dd>
        </div>
        <div>
          <dt>{T.currency}</dt>
          <dd className={styles.identifier}>{draft.currencyId}</dd>
        </div>
        <div>
          <dt>{T.requestedDeliveryDate}</dt>
          <dd data-testid="sales-draft-header-due">
            {detail.requestedDeliveryDate === null ? T.unknown : formatBusinessDate(detail.requestedDeliveryDate)}
          </dd>
        </div>
        <div>
          <dt>{T.note}</dt>
          <dd>{orUnknown(detail.note)}</dd>
        </div>
      </dl>

      {delivery}

      <section className={styles.section} aria-labelledby="sales-approval-heading" data-testid="sales-approval">
        <h2 id="sales-approval-heading">{T.approval.heading}</h2>
        <dl className={styles.facts}>
          <div>
            <dt>{T.approval.submittedBy}</dt>
            <dd data-testid="sales-approval-submitted-by">{approval.submittedBy ?? T.approval.notYet}</dd>
          </div>
          <div>
            <dt>{T.approval.submittedAt}</dt>
            <dd>{approval.submittedAtUtc === null ? T.approval.notYet : formatInstant(approval.submittedAtUtc)}</dd>
          </div>
          <div>
            <dt>{T.approval.decidedBy}</dt>
            <dd data-testid="sales-approval-decided-by">{approval.decidedBy ?? T.approval.notYet}</dd>
          </div>
          <div>
            <dt>{T.approval.decidedAt}</dt>
            <dd>{approval.decidedAtUtc === null ? T.approval.notYet : formatInstant(approval.decidedAtUtc)}</dd>
          </div>
          {draft.approvalState === APPROVAL_STATE.rejected ? (
            <div>
              <dt>{T.approval.rejectionReason}</dt>
              <dd data-testid="sales-approval-reason">{approval.rejectionReason ?? T.approval.noReason}</dd>
            </div>
          ) : null}
        </dl>
        <p className={styles.muted} data-testid="sales-approval-linked-effects">
          {T.approval.linkedEffects}
        </p>
        {draft.approvalState === APPROVAL_STATE.approved ? <p className={styles.muted}>{T.approval.approvedNote}</p> : null}
        {draft.approvalState === APPROVAL_STATE.rejected ? <p className={styles.muted}>{T.approval.rejectedNote}</p> : null}
        {commands}
      </section>

      <section className={styles.section} aria-labelledby="sales-commercial-heading">
        <h2 id="sales-commercial-heading">{T.commercialHeading}</h2>
        <dl className={styles.facts}>
          {OWNER_REFERENCE_FIELDS.map((field) => {
            const reference = detail[field]
            return (
              <div key={field}>
                <dt>{T.ownerReference[field]}</dt>
                <dd data-testid={`sales-draft-${field}`} className={reference === null ? undefined : styles.identifier}>
                  {reference === null ? T.unknown : `${reference.id} (${reference.expectedVersion})`}
                </dd>
              </div>
            )
          })}
        </dl>
      </section>

      <section className={styles.section} aria-labelledby="sales-lines-heading">
        <h2 id="sales-lines-heading">{T.linesHeading}</h2>
        {detail.lines.map((line) => (
          <LineView key={line.lineId} line={line} />
        ))}
      </section>
    </article>
  )
}

/* ── SALES-A2a: the delivery block ─────────────────────────────────────────── */

function DeliveryBlock({
  shell,
  detail,
  companyId,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
}) {
  const { draft } = detail
  const D = T.delivery
  // The reference reads are the create form's (bound to sales.drafts.create): without that grant the
  // ids are shown with the reason, and no read is attempted.
  const canRead = hasCapability(shell, SALES_CAPABILITY.draftsCreate)
  const wanted = detail.deliveryAddress !== null || detail.contact !== null
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const useContactInformation = useContext(SalesContactInformationSourceContext)
  const currencies = useCurrencies(canRead ? companyId : '', draft.businessDate)
  const contactInformation = useContactInformation(canRead && wanted ? companyId : '', draft.partyId, draft.companyAccountId)
  const currency = currencies.status === 'ready' ? (currencies.data.find((c) => c.currencyId === draft.currencyId) ?? null) : null

  return (
    <section className={styles.section} aria-labelledby="sales-delivery-heading" data-testid="sales-delivery-block">
      <h2 id="sales-delivery-heading">{D.heading}</h2>
      <dl className={styles.facts}>
        <div>
          <dt>{D.currency}</dt>
          <dd data-testid="sales-currency-code">
            {!canRead ? (
              <IdWithNote id={draft.currencyId} note={D.noGrant} />
            ) : currency !== null ? (
              currency.code
            ) : currencies.status === 'unavailable' ? (
              <IdWithNote id={draft.currencyId} note={D.unreadable(currencies.reason)} retry={currencies.retry} />
            ) : currencies.status === 'ready' ? (
              <IdWithNote id={draft.currencyId} note={D.currencyNotListed} />
            ) : (
              D.reading
            )}
          </dd>
        </div>
        <ReferenceCell
          testId="sales-delivery-address-text"
          label={D.address}
          reference={detail.deliveryAddress}
          notChosen={D.addressNotChosen}
          canRead={canRead}
          read={contactInformation}
          pick={(options, id) => {
            const address = options.addresses.find((a) => a.id === id)
            return address === undefined ? null : { version: address.version, text: address.text }
          }}
        />
        <ReferenceCell
          testId="sales-delivery-contact-text"
          label={D.contact}
          reference={detail.contact}
          notChosen={D.contactNotChosen}
          canRead={canRead}
          read={contactInformation}
          pick={(options, id) => {
            const contact = options.contacts.find((c) => c.id === id)
            return contact === undefined ? null : { version: contact.version, text: contactLabel(contact) }
          }}
        />
      </dl>
      {wanted ? null : (
        <p className={styles.muted} data-testid="sales-delivery-none">
          {D.notChosen}
        </p>
      )}
    </section>
  )
}

/** The stored id in mono beside the reason it cannot be shown as a name — never a guessed name. */
function IdWithNote({ id, note, retry }: { readonly id: string; readonly note: string; readonly retry?: () => void }) {
  return (
    <>
      <span className={styles.identifier}>{id}</span> — {note}
      {retry === undefined ? null : (
        <>
          {' '}
          <button type="button" className={styles.linkButton} onClick={retry}>
            {ORDER_DRAFT_TEXT.references.retry}
          </button>
        </>
      )}
    </>
  )
}

function ReferenceCell({
  testId,
  label,
  reference,
  notChosen,
  canRead,
  read,
  pick,
}: {
  readonly testId: string
  readonly label: string
  readonly reference: OwnerReference | null
  readonly notChosen: string
  readonly canRead: boolean
  readonly read: ContactInformationReadState
  readonly pick: (options: ContactInformationOptions, id: string) => { readonly version: string; readonly text: string } | null
}) {
  const D = T.delivery
  let content: React.ReactNode
  if (reference === null) content = notChosen
  else if (!canRead) content = <IdWithNote id={reference.id} note={D.noGrant} />
  else if (read.status === 'unavailable') content = <IdWithNote id={reference.id} note={D.unreadable(read.reason)} retry={read.retry} />
  else if (read.status !== 'ready') content = D.reading
  else {
    const found = pick(read.data, reference.id)
    content =
      found === null ? (
        <IdWithNote id={reference.id} note={D.notListed} />
      ) : (
        <>
          {found.text}
          {found.version === reference.expectedVersion ? null : <span className={styles.muted}> — {D.versionChanged}</span>}
        </>
      )
  }
  return (
    <div>
      <dt>{label}</dt>
      <dd data-testid={testId}>{content}</dd>
    </div>
  )
}

/* ── SALES-A1: the commands ────────────────────────────────────────────────── */

type CommandAttempt = OrderAttemptState<DraftStateCommandRequest, DraftMutation>

/** The attempt binding scope of the detail's commands: one per draft, so a key never crosses drafts. */
const commandScope = (draftId: string, command: DraftStateCommand) => `sales.draft.${command}:${draftId}`

function ApprovalCommands({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const { draft } = detail
  const canSubmit = hasCapability(shell, SALES_CAPABILITY.draftsCreate)
  const canDecide = hasCapability(shell, SALES_CAPABILITY.ordersApprove)
  const [attempt, setAttempt] = useState<CommandAttempt>({ phase: 'idle' })
  const [command, setCommand] = useState<DraftStateCommand | null>(null)
  const [reason, setReason] = useState('')
  const attemptRef = useRef(attempt)
  attemptRef.current = attempt

  const offerSubmit =
    canSubmit && (draft.approvalState === APPROVAL_STATE.preparing || draft.approvalState === APPROVAL_STATE.rejected)
  const offerDecision = canDecide && draft.approvalState === APPROVAL_STATE.pendingApproval
  if (!offerSubmit && !offerDecision && attempt.phase === 'idle') return null

  async function send(kind: DraftStateCommand, snapshot: OrderAttemptSnapshot<DraftStateCommandRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftStateCommandRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    setCommand(kind)
    const outcome = await sendDraftCommand(kind, snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, commandScope(draft.id, kind)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }

  function start(kind: DraftStateCommand) {
    const binding = orderAttemptBinding(shell, commandScope(draft.id, kind))
    if (binding === null) return
    const payload: DraftStateCommandRequest =
      kind === 'rejection' && reason.trim() !== ''
        ? { expectedVersion: draft.version, reason: reason.trim() }
        : { expectedVersion: draft.version }
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${draft.id}/${kind}`, binding, companyId, payload },
      generateOrderAttemptKey,
    )
    if (decision.type !== 'send') return
    void send(kind, decision.snapshot)
  }

  function retry() {
    const live = liveSnapshot(attemptRef.current)
    if (live === null || command === null || attemptRef.current.phase === 'submitting') return
    void send(command, live)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = commandNotice(attempt)

  return (
    <div className={styles.actions} data-testid="sales-approval-commands">
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-approval-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <>
          <button type="button" className={styles.secondaryButton} onClick={retry}>
            {T.approval.retry}
          </button>
          <button type="button" className={styles.linkButton} onClick={leave}>
            {T.approval.abandon}
          </button>
        </>
      ) : null}
      {offerSubmit ? (
        <button type="button" className={styles.primaryButton} disabled={frozen} onClick={() => start('submission')}>
          {T.approval.submit}
        </button>
      ) : null}
      {offerDecision ? (
        <>
          <button type="button" className={styles.primaryButton} disabled={frozen} onClick={() => start('approval')}>
            {T.approval.approve}
          </button>
          <label htmlFor="sales-rejection-reason">{T.approval.reasonLabel}</label>
          <textarea
            id="sales-rejection-reason"
            maxLength={4000}
            value={reason}
            disabled={frozen}
            onChange={(event) => setReason(event.target.value)}
          />
          <button type="button" className={styles.secondaryButton} disabled={frozen} onClick={() => start('rejection')}>
            {T.approval.reject}
          </button>
        </>
      ) : null}
    </div>
  )
}

/** The notice of one command attempt, in the approval's own words. */
function commandNotice(attempt: CommandAttempt): string | null {
  switch (attempt.phase) {
    case 'idle':
      return null
    case 'submitting':
      return T.approval.submitting
    case 'unknown':
      return T.approval.unknown
    case 'abandoned':
      return T.approval.unknown
    case 'completed':
      return attempt.result.isReplay ? T.approval.replayed : T.approval.completed
    case 'failed': {
      // `version_conflict` on a state command means the draft moved after this screen read it (the
      // approval's own sentence); the shared code text is the S-03 product-version sentence and would
      // mislead here (browser run 35447380891, S04-03). Every other code keeps its shared text.
      const code = attempt.problem?.code ?? null
      const codeText = code === SALES_PROBLEM_CODE.versionConflict ? T.approval.versionConflict : problemCodeText(code)
      const base =
        attempt.failure === 'unauthenticated'
          ? T.approval.unauthenticated
          : attempt.failure === 'forbidden'
            ? T.approval.forbidden
            : attempt.failure === 'not-found'
              ? T.approval.notFound
              : attempt.failure === 'conflict'
                ? T.approval.conflict
                : attempt.failure === 'validation'
                  ? T.approval.validation
                  : T.approval.unexpected
      const text = codeText === null ? base : `${base} ${codeText}`
      return attempt.unresolvedSince === undefined ? text : `${text} ${T.approval.refusedAfterUnknown}`
    }
    default:
      return null
  }
}

function LineView({ line }: { readonly line: DraftLine }) {
  const plan = unplannedQuantity(line, line.requestedDeliverySlices)
  const headingId = `sales-line-${line.lineId}`
  return (
    <section className={styles.line} aria-labelledby={headingId} data-testid={`sales-line-${line.lineNo}`}>
      <h3 id={headingId} className={styles.lineTitle}>
        {line.lineNo}. {line.productCode} — {line.productName}
      </h3>
      <dl className={styles.facts}>
        <div>
          <dt>{T.productVersion}</dt>
          <dd>{line.productVersion}</dd>
        </div>
        <div>
          <dt>{T.quantity}</dt>
          <dd className={styles.numericValue}>{line.quantity}</dd>
        </div>
        <div>
          <dt>{T.orderUnit}</dt>
          <dd className={styles.identifier}>{line.orderUnitId}</dd>
        </div>
        <div>
          <dt>{T.lineDeliveryDate}</dt>
          <dd>{line.requestedDeliveryDate === null ? T.unknown : formatBusinessDate(line.requestedDeliveryDate)}</dd>
        </div>
        <div>
          <dt>{T.price}</dt>
          <dd data-testid={`sales-line-${line.lineNo}-price`}>
            {line.unitPrice === null ? T.notPriced : `${line.unitPrice} / ${line.priceUnitId}`}
          </dd>
        </div>
        <div>
          <dt>{T.discount}</dt>
          <dd>{orUnknown(line.discountPercent)}</dd>
        </div>
        <div>
          <dt>{T.note}</dt>
          <dd>{orUnknown(line.note)}</dd>
        </div>
      </dl>

      {line.requestedDeliverySlices === null ? (
        <p className={styles.muted} data-testid={`sales-line-${line.lineNo}-plan`}>
          {T.planUnknown}
        </p>
      ) : line.requestedDeliverySlices.length === 0 ? (
        <p className={styles.muted} data-testid={`sales-line-${line.lineNo}-plan`}>
          {T.planEmpty}
        </p>
      ) : (
        <table className={styles.table}>
          <caption className={styles.caption}>
            {T.slicesHeading} — {line.lineNo}. satır
          </caption>
          <thead>
            <tr>
              <th scope="col">{T.sliceDate}</th>
              <th scope="col" className={styles.numeric}>
                {T.sliceQuantity}
              </th>
            </tr>
          </thead>
          <tbody>
            {line.requestedDeliverySlices.map((slice) => (
              <tr key={slice.sliceId}>
                <td>{formatBusinessDate(slice.requestedDeliveryDate)}</td>
                <td className={styles.numeric}>{slice.quantity}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {plan.ok ? (
        <p data-testid={`sales-line-${line.lineNo}-unplanned`}>
          {T.unplanned}: <span className={styles.numericValue}>{plan.unplanned}</span>
        </p>
      ) : null}
    </section>
  )
}
