import { useState } from 'react'
import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { ACCOUNTING_CAPABILITY, ACCOUNTING_ROUTE } from '../accounting/capabilities'
import { SALES_CAPABILITY, SALES_ROUTE } from '../sales/capabilities'
import { PURCHASING_CAPABILITY, purchaseOrderDetailPath } from '../purchasing/capabilities'
import { fetchWorkNotifications } from './notificationApi'
import { NOTIFICATIONS_ROUTE } from './notificationContract'
import { NOTIFICATION_TEXT as T } from './notificationText'
import { CollaborationInbox } from './CollaborationInbox'
import styles from './Work.module.css'

export function NotificationsHomeEntry() {
  const shell = useShellState()
  if (capabilityFailClosed(shell) || shell.activeContext?.scopeMode !== 'single') return null
  return <section><h2>{T.heading}</h2><Link to={NOTIFICATIONS_ROUTE}>{T.home}</Link></section>
}
export function NotificationsPage() {
  const shell = useShellState()
  const context = contextKey(shell)
  return <NotificationsBody key={context === null ? 'no-context' : contextKeyString(context)} />
}
function NotificationsBody() {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const [includePassive, setIncludePassive] = useState(false)
  const [cursor, setCursor] = useState<string | null>(null)
  const boundary = useAuthBoundary()
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'my-notifications', includePassive, cursor],
    enabled: namespace !== null && company !== null && !capabilityFailClosed(shell),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchWorkNotifications(company!, includePassive, signal, cursor)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    } })
  const page = query.data?.type === 'ok-single' ? query.data.data : null
  if (capabilityFailClosed(shell) || company === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
  return <section className={styles.page} aria-labelledby="my-notifications-heading">
    <h1 id="my-notifications-heading">{T.heading}</h1><p>{T.scope}</p>
    <label><input type="checkbox" checked={includePassive} onChange={(event) => { setCursor(null); setIncludePassive(event.target.checked) }} />{T.includePassive}</label>
    {cursor === null ? null : <button type="button" onClick={() => setCursor(null)}>{T.first}</button>}
    {page === null ? <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.subject} onRetry={() => void query.refetch()} />
      : page.items.length === 0 ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />
        : <><div className={styles.tableWrap} role="region" aria-label={T.heading} tabIndex={0}><table className={styles.table}><caption>{T.heading}</caption>
          <thead><tr><th scope="col">{T.heading}</th><th scope="col">{T.source}</th><th scope="col">{T.reason}</th><th scope="col">{T.time}</th><th scope="col">{T.status}</th></tr></thead>
          <tbody>{page.items.map((item) => <tr key={item.id}><td>{item.kind === 'allocation-released' ? T.released : item.kind === 'purchase-supply-revived' ? T.revived : T.staleCost}</td><td>{item.kind === 'accounting-cost-proposal-stale' ? <>{T.costProposal}: {item.sourceEntityId}<p>{T.staleCostNextStep}</p>{hasCapability(shell, item.costKind === 1 ? ACCOUNTING_CAPABILITY.invoicedShipmentPrepare : ACCOUNTING_CAPABILITY.purchaseCostPrepare) ? <Link to={`${item.costKind === 1 ? ACCOUNTING_ROUTE.invoicedShipmentPrepare : ACCOUNTING_ROUTE.purchaseCostPrepare}?${new URLSearchParams({ proposalId: item.sourceEntityId }).toString()}`}>{T.openCost}</Link> : null}</> : item.salesOrderLineId ?? item.purchaseOrderLineId}
            {item.salesOrderId !== null && hasCapability(shell, SALES_CAPABILITY.draftsDetail) ? <> <Link to={SALES_ROUTE.detail.replace(':draftId', item.salesOrderId)}>{T.order}</Link></> : null}
            {item.purchaseOrderId !== null && hasCapability(shell, PURCHASING_CAPABILITY.orderDetail) ? <> <Link to={purchaseOrderDetailPath(item.purchaseOrderId)}>{T.order}</Link></> : null}</td>
            <td>{item.reasonText}</td><td><time dateTime={item.recordedAtUtc}>{new Date(item.recordedAtUtc).toLocaleString('tr-TR')}</time></td><td>{item.passivatedAtUtc === null ? T.active : T.passive}</td></tr>)}</tbody>
        </table></div>{page.hasMore ? <button type="button" onClick={() => setCursor(page.nextCursor)}>{T.more}</button> : null}</>}
    <CollaborationInbox />
  </section>
}
