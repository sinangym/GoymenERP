import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { fetchTransfer, fetchTransfers } from './transferApi'
import type { TransferDetail, TransferList } from './transferContract'
import { TRANSFER_TEXT as T } from './transferText'
import { INVENTORY_STOCK_POSITIONS_ROUTE, INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_TRANSFER_CANCEL_CAPABILITY } from './capabilities'
import { TransferCancelPanel } from './TransferCancelPanel'
import { TransferDispatchPanel } from './TransferDispatchPanel'
import { transferCancelBinding } from './transferCancelAttempt'
import { TransferPlanPanel } from './TransferPlanPanel'
import { TRANSFER_PLAN_CAPABILITY } from './transferPlanApi'
import { TransferReceiptPanel } from './TransferReceiptPanel'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import styles from './inventory.module.css'

export function InventoryTransferPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <TransferView key={`${namespace}:${shell.routeEntryGeneration}`} namespace={namespace} />
}

function TransferView({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = hasCapability(shell, INVENTORY_STOCK_POSITIONS_CAPABILITY)
  const [selected, setSelected] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['inventory-transfers', namespace, shell.routeEntryGeneration], retry: false,
    enabled: namespace !== null && companyId !== null && canRead, queryFn: async ({ signal }) => {
      const capture = boundary.capture()
      const result = await fetchTransfers(companyId!, signal)
      if (result.type === 'http' && [401, 403].includes(result.status)) boundary.report(result.status as 401 | 403, capture)
      return result
    } })
  const detail = useQuery({ queryKey: ['inventory-transfer', namespace, selected, shell.routeEntryGeneration], retry: false,
    enabled: namespace !== null && companyId !== null && selected !== null && canRead, queryFn: async ({ signal }) => {
      const capture = boundary.capture()
      const result = await fetchTransfer(companyId!, selected!, signal)
      if (result.type === 'http' && [401, 403].includes(result.status)) boundary.report(result.status as 401 | 403, capture)
      return result
    } })
  if (namespace === null || companyId === null) return <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
  const state = (!canRead ? { kind: 'forbidden' } : list.isPending ? { kind: 'initial-loading' } : classifyOutcome(list.data ?? { type: 'malformed' })) as RemoteViewState<TransferList>
  const detailState = (!canRead ? { kind: 'forbidden' } : detail.isPending ? { kind: 'initial-loading' } : classifyOutcome(detail.data ?? { type: 'malformed' })) as RemoteViewState<TransferDetail>
  return <main className={styles.page}>
    <nav><Link to={INVENTORY_STOCK_POSITIONS_ROUTE}>{T.back}</Link></nav><h1>{T.title}</h1>
    {hasCapability(shell, TRANSFER_PLAN_CAPABILITY) ? <TransferPlanPanel onPlanned={(transferId) => { if (canRead) void list.refetch(); setSelected(transferId) }} /> : null}
    <TransferReceiptPanel transferId={selected} onChanged={(transferId) => { if (canRead) { void list.refetch(); if (selected === transferId) void detail.refetch() } setSelected(transferId) }} />
    {!canRead ? null : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void list.refetch()} /> : <>
    {state.data.windowFull ? <p role="status">{T.window}</p> : null}
    {state.data.items.length === 0 ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} /> : <table>
      <caption>{T.title}</caption><thead><tr><th>{T.plannedAt}</th><th>{T.from}</th><th>{T.to}</th><th>{T.status}</th><th>{T.detail}</th></tr></thead>
      <tbody>{state.data.items.map(item => <tr key={item.transfer.transferId}>
        <td>{new Date(item.transfer.plannedAtUtc).toLocaleString('tr-TR')}</td>
        <td>{item.sourceLocationCode} — {item.sourceLocationName}</td><td>{item.destinationLocationCode} — {item.destinationLocationName}</td>
        <td>{T.statuses[item.transfer.status]}</td><td><Button type="button" onClick={() => setSelected(item.transfer.transferId)}>{T.inspect}</Button></td>
      </tr>)}</tbody></table>}</>}
    {selected === null || !canRead ? null : <section aria-labelledby="transfer-detail"><h2 id="transfer-detail">{T.detail}</h2>
      {detailState.kind !== 'ready' ? <StateBlock state={detailState} subject={T.detail} onRetry={canRead ? () => void detail.refetch() : undefined} /> : <><table>
        <caption>{T.detail}</caption><thead><tr><th>{T.line}</th><th>{T.plannedKg}</th><th>{T.shippedKg}</th><th>{T.differenceKg}</th><th>{T.status}</th><th>{T.label}</th></tr></thead>
        <tbody>{detailState.data.lines.map((line, index) => <tr key={line.lineId}><td>{index + 1}</td><td>{line.plannedKg}</td>
          <td>{line.actualShippedKg ?? T.unknown}</td><td>{line.differenceKg ?? T.unknown}</td><td>{T.statuses[line.status]}</td>
          <td><Link to={inventoryLabelDetailPath(line.labelId)}>{T.openLabel}</Link></td></tr>)}</tbody>
      </table>{hasCapability(shell, TRANSFER_PLAN_CAPABILITY) ? null : <>
        <TransferDispatchPanel transfer={detailState.data} onChanged={() => { void list.refetch(); void detail.refetch() }} />
        <TransferCancelPanel transfer={detailState.data} binding={transferCancelBinding(shell, selected)} canCancel={hasCapability(shell, INVENTORY_TRANSFER_CANCEL_CAPABILITY)}
          onChanged={() => { void list.refetch(); void detail.refetch() }} /></>}</>}
    </section>}
  </main>
}
