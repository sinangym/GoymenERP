/**
 * UI-71 — S-11 "Satış İletişimi ve Sözleşmeler", in the S-04 "Sözleşme / Belgeler" tab (the S-11 visual is that tab of
 * Sipariş 360; the catalog files it under Satış → Satış Siparişleri).
 *
 * What the backend supports today, and nothing more:
 *   - the order's own documents (Document Vault owner `sales.order`, authorised by Sales's own detail read —
 *     `SalesDocumentOwnerLinkPort`);
 *   - the customer's Cari-card documents (owner `counterparty.party`, authorised by the Cari card's own read —
 *     `CounterpartyDocumentOwnerLinkPort`), where a contract valid across orders is kept;
 *   - one timeline over both, newest first, filtered by the record a document is bound to.
 * Upload, download, preview and versions stay in each owner's `DocumentsPanel`; the timeline only reads, and shares the
 * panels' cache key, so it adds no request and refreshes when a panel stores a document.
 *
 * Not drawn, because no backend source exists (follow-ups in the PR): a contract record (number, version, validity,
 * clauses), document categories (Sözleşme, Teklif, Yazışma …) and a communication log. The customer read is asked only
 * where the shell publishes the Cari card's detail capability (the vault measures the same Cari read on every request);
 * a 403/404 from either owner is a hidden owner, never an empty list.
 */

import { useId, useState, type ReactNode } from 'react'
import { useQueries } from '@tanstack/react-query'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { documentsQueryKey, fetchDocuments } from '../documents/api'
import { OWNER_KIND, type DocumentRow, type OwnerKind } from '../documents/contract'
import { DOCUMENTS_TEXT, formatInstant, typeLabel } from '../documents/documentsText'
import type { PartyDisplay } from './contract'
import { SALES_DOCUMENTS_TEXT as TEXT, partyDisplayText } from './salesText'
import { Button } from '../ui/Button'
import { DOCUMENT_RELATION, mergeTimeline, type DocumentRelation, type RelationFilter } from './documentTimeline'
import styles from './SalesOrderDocumentsWorkspace.module.css'

type OwnerRead =
  | { readonly state: 'loading' }
  | { readonly state: 'ok'; readonly items: readonly DocumentRow[] }
  /** 403/404: this owner's documents do not exist for this actor here. */
  | { readonly state: 'hidden' }
  | { readonly state: 'failed' }

export function SalesOrderDocumentsWorkspace({
  namespace,
  entryGeneration,
  companyId,
  orderId,
  orderDocumentNo,
  party,
  canReadCustomer,
}: {
  readonly namespace: string
  readonly entryGeneration: number
  readonly companyId: string
  readonly orderId: string
  readonly orderDocumentNo: string
  readonly party: PartyDisplay
  /** The shell publishes the Cari card's detail capability (the same read the vault measures for the customer). */
  readonly canReadCustomer: boolean
}) {
  const owners: readonly { readonly relation: DocumentRelation; readonly kind: OwnerKind; readonly id: string }[] = [
    { relation: DOCUMENT_RELATION.order, kind: OWNER_KIND.salesOrder, id: orderId },
    ...(canReadCustomer ? [{ relation: DOCUMENT_RELATION.customer, kind: OWNER_KIND.counterpartyParty, id: party.partyId }] : []),
  ]
  const queries = useQueries({
    queries: owners.map((owner) => ({
      queryKey: documentsQueryKey(namespace, entryGeneration, owner.kind, owner.id),
      queryFn: ({ signal }: { signal: AbortSignal }) => fetchDocuments(owner.kind, owner.id, companyId, signal),
    })),
  })
  const reads: OwnerRead[] = queries.map((query) => {
    const outcome = query.data
    if (query.isPending || outcome === undefined || outcome.type === 'fence-stale') return { state: 'loading' }
    if (outcome.type === 'ok-single') return { state: 'ok', items: outcome.data.items }
    if (outcome.type === 'http' && (outcome.status === 403 || outcome.status === 404)) return { state: 'hidden' }
    return { state: 'failed' }
  })
  const orderRead = reads[0]
  const customerRead: OwnerRead = canReadCustomer ? reads[1] : { state: 'hidden' }

  return (
    <div className={styles.workspace} data-testid="sales-documents-workspace">
      <p className={styles.note}>{TEXT.note}</p>
      <Timeline
        orderRead={orderRead}
        customerRead={customerRead}
        orderDocumentNo={orderDocumentNo}
        party={party}
        onRetry={() => {
          queries.forEach((query, index) => {
            if (reads[index].state === 'failed') void query.refetch()
          })
        }}
      />
      <DocumentsPanel
        ownerKind={OWNER_KIND.salesOrder}
        ownerId={orderId}
        companyId={companyId}
        namespace={namespace}
        entryGeneration={entryGeneration}
        heading={TEXT.orderPanelHeading}
      />
      {canReadCustomer ? (
        <DocumentsPanel
          ownerKind={OWNER_KIND.counterpartyParty}
          ownerId={party.partyId}
          companyId={companyId}
          namespace={namespace}
          entryGeneration={entryGeneration}
          heading={TEXT.customerPanelHeading}
        />
      ) : null}
    </div>
  )
}

function Timeline({
  orderRead,
  customerRead,
  orderDocumentNo,
  party,
  onRetry,
}: {
  readonly orderRead: OwnerRead
  readonly customerRead: OwnerRead
  readonly orderDocumentNo: string
  readonly party: PartyDisplay
  readonly onRetry: () => void
}) {
  const headingId = useId()
  const [chosenFilter, setFilter] = useState<RelationFilter>('all')
  const orderItems = orderRead.state === 'ok' ? orderRead.items : []
  const customerItems = customerRead.state === 'ok' ? customerRead.items : []
  const rows = mergeTimeline(orderItems, customerItems)
  // A filter whose owner is no longer readable falls back to all rows; it never leaves an unexplained empty table.
  const filter: RelationFilter =
    (chosenFilter === 'order' && orderRead.state !== 'ok') || (chosenFilter === 'customer' && customerRead.state !== 'ok')
      ? 'all'
      : chosenFilter

  const loading = orderRead.state === 'loading' || customerRead.state === 'loading'
  const failed = orderRead.state === 'failed' || customerRead.state === 'failed'
  const forbidden = orderRead.state === 'hidden' && customerRead.state === 'hidden'

  let body: ReactNode
  if (loading) {
    body = (
      <p role="status" className={styles.note} data-documents-timeline="loading">
        {TEXT.loading}
      </p>
    )
  } else if (forbidden) {
    body = (
      <p className={styles.note} data-documents-timeline="forbidden">
        {TEXT.forbidden}
      </p>
    )
  } else {
    const visible = filter === 'all' ? rows : rows.filter((row) => row.relation === filter)
    const relationText = (relation: DocumentRelation) =>
      relation === DOCUMENT_RELATION.order ? TEXT.relationOrder(orderDocumentNo) : TEXT.relationCustomer(partyDisplayText(party))
    body = (
      <>
        {failed ? (
          <div role="alert" className={styles.problem} data-documents-timeline="read-failed">
            <p>{TEXT.readFailed}</p>
            <Button type="button" size="sm" onClick={onRetry}>
              {TEXT.readAgain}
            </Button>
          </div>
        ) : null}
        {orderRead.state === 'hidden' ? (
          <p className={styles.note} data-documents-timeline="order-hidden">
            {TEXT.orderHidden}
          </p>
        ) : null}
        {customerRead.state === 'hidden' ? (
          <p className={styles.note} data-documents-timeline="customer-hidden">
            {TEXT.customerHidden}
          </p>
        ) : null}
        {rows.length === 0 ? (
          failed ? null : (
            <p className={styles.note} data-documents-timeline="empty">
              {TEXT.empty}
            </p>
          )
        ) : (
          <>
            <div role="group" aria-label={TEXT.filterLabel} className={styles.filters}>
              <FilterButton active={filter === 'all'} onClick={() => setFilter('all')} value="all">
                {TEXT.filter.all(rows.length)}
              </FilterButton>
              {orderRead.state === 'ok' ? (
                <FilterButton active={filter === 'order'} onClick={() => setFilter('order')} value="order">
                  {TEXT.filter.order(orderItems.length)}
                </FilterButton>
              ) : null}
              {customerRead.state === 'ok' ? (
                <FilterButton active={filter === 'customer'} onClick={() => setFilter('customer')} value="customer">
                  {TEXT.filter.customer(customerItems.length)}
                </FilterButton>
              ) : null}
            </div>
            {visible.length === 0 ? (
              <p className={styles.note} data-documents-timeline="filter-empty">
                {TEXT.filterEmpty}
              </p>
            ) : (
              <div className={styles.tableWrap}>
                <table className={styles.table} data-documents-timeline="rows">
                  <caption className={styles.srOnly}>{TEXT.timelineCaption}</caption>
                  <thead>
                    <tr>
                      <th scope="col">{TEXT.columnDate}</th>
                      <th scope="col">{TEXT.columnFile}</th>
                      <th scope="col">{TEXT.columnRelation}</th>
                      <th scope="col">{TEXT.columnVersion}</th>
                      <th scope="col">{TEXT.columnType}</th>
                      <th scope="col">{TEXT.columnScan}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map((row) => (
                      <tr key={`${row.relation}:${row.document.id}`} data-timeline-relation={row.relation} data-document-id={row.document.id}>
                        <td>
                          <time dateTime={row.document.uploadedAtUtc}>{formatInstant(row.document.uploadedAtUtc)}</time>
                        </td>
                        <th scope="row">{row.document.fileName}</th>
                        <td>{relationText(row.relation)}</td>
                        <td>{DOCUMENTS_TEXT.versionNo(row.document.versionNo)}</td>
                        <td>{typeLabel(row.document.contentType)}</td>
                        <td>{row.document.scanState === 'Clean' ? DOCUMENTS_TEXT.scanClean : DOCUMENTS_TEXT.scanPending}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </>
    )
  }

  return (
    <section aria-labelledby={headingId} className={styles.timeline} data-section="documents-timeline">
      <h2 id={headingId} className={styles.title}>
        {TEXT.timelineHeading}
      </h2>
      {body}
    </section>
  )
}

function FilterButton({
  active,
  onClick,
  value,
  children,
}: {
  readonly active: boolean
  readonly onClick: () => void
  readonly value: RelationFilter
  readonly children: ReactNode
}) {
  return (
    <button
      type="button"
      className={styles.filter}
      aria-pressed={active}
      onClick={onClick}
      data-timeline-filter={value}
    >
      {children}
    </button>
  )
}
