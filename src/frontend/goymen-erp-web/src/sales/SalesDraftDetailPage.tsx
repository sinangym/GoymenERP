/**
 * S-04 Sipariş 360 — the draft as `GET /api/v1/sales/drafts/{id}` describes it (#257).
 *
 * Only what the detail carries:
 *  - the commercial revision (`Rev.0`) and the concurrency version are shown as two
 *    separate facts, never merged into "v1" or similar;
 *  - a null is shown as unknown, never as zero, cash, today or "none": an unknown
 *    price is "not priced", an unknown plan is "no plan entered", an empty plan is
 *    "no planned slices" — three different statements;
 *  - "unplanned" is the quantity not yet placed on a dated slice. It is not
 *    delivered, reserved or open; nothing has been delivered on a draft;
 *  - party, account, currency and unit have no display name in this contract, so
 *    their ids are shown as ids rather than resolved by a client lookup.
 *
 * No approval, allocation, shipment, invoice or money tab: none exists for a draft.
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchDraftDetail } from './api'
import { OWNER_REFERENCE_FIELDS, type DraftDetail, type DraftLine } from './contract'
import { unplannedQuantity } from './deliveryPlan'
import { formatBusinessDate, formatInstant } from './format'
import { SALES_CAPABILITY, SALES_ROUTE } from './capabilities'
import { ORDER_DETAIL_TEXT as T } from './salesText'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
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

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft', draftId ?? ''],
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
  return <DraftView detail={outcome.data} backLink={backLink} />
}

export function DraftView({ detail, backLink }: { readonly detail: DraftDetail; readonly backLink: React.ReactNode }) {
  const { draft } = detail
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
          {T.stateDraft}
        </span>
      </header>

      <dl className={styles.facts}>
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
