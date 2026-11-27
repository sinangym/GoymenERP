import { AI_PANEL_CAPABILITY } from '../ai/aiCapabilities'
import { WorkSalesSummary } from './WorkSalesSummary'
import { customerReturnText as CUSTOMER_RETURN } from '../logistics/customerReturnText'
import { DISPATCH_NOTE_TEXT as DISPATCH } from '../logistics/logisticsText'
import { BANK_COLUMN, BANK_MASTER_NOTICE, IBAN_CHANGE_NEW, IBAN_CHANGE_REASON, currencyFallback } from '../finance/bankAccountText'
import { SUPPLIER_RETURN_TEXT as SUPPLIER_RETURN, SUPPLIER_RETURN_SOURCE_BLOCK_TEXT } from '../purchasing/supplierReturnText'
import { KIND_LABEL, TAX_IDENTITY_TEXT } from '../cari/taxIdentity'
import { LEGAL_NAME_TEXT } from '../cari/legalName'
import { DISPATCH_KG_TEXT as WEIGHING } from '../logistics/dispatchKilogramsText'
import { PLAN_EXCESS_TEXT as PLAN_EXCESS } from '../logistics/planExcessText'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useInfiniteQuery, useQuery, type InfiniteData } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useScopedCommandAttempt } from '../settings/useCommandAttempt'
import { Button } from '../ui/Button'
import { WORK_INBOX_TEXT as INBOX, inboxKindText, inboxRoleText, inboxRuleText, formatWithUnit } from './workInboxText'
import { fetchWorkDecision, sendWorkDecision, workDecisionLocalUtc, isWorkDecisionRequest, isSalesWorkDecisionKind, isCounterpartyWorkDecisionKind, isWorkTransferOverageReason, isWorkStockCountReason, isWorkSupplierReturnReason, isWorkSupplierReturnVersion, workDecisionReasonLimit, type WorkDecisionKind, type WorkDecisionRequest, type WorkDecisionResult } from './workDecisionApi'
import { fetchDelegations, type DelegationPage } from './workDelegationApi'
import { delegationPersonLabel } from './delegationPersonLabel'
import type { ViewOutcome } from '../shell/uiState'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import styles from './Work.module.css'

class DelegationReadFailure extends Error {
  readonly outcome: ViewOutcome<unknown>
  constructor(outcome: ViewOutcome<unknown>) { super('delegation-read-failed'); this.outcome = outcome }
}

export function WorkDecisionPage({ kind }: { readonly kind: WorkDecisionKind }) {
  const shell = useShellState(); const context = contextKey(shell); const { approvalId = '' } = useParams()
  return <Decision key={`${context === null ? 'none' : contextKeyString(context)}:${shell.routeEntryGeneration}:${kind}:${approvalId}`} kind={kind} id={approvalId} />
}
function Decision({ kind, id }: { readonly kind: WorkDecisionKind; readonly id: string }) {
  const shell = useShellState(); const context = contextKey(shell); const boundary = useAuthBoundary()
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const namespace = context === null ? null : contextKeyString(context)
  const [selectedLabels, setSelectedLabels] = useState<readonly string[]>([])
  const [selectionVersion, setSelectionVersion] = useState<string | null>(null)
  const [decisionTime, setDecisionTime] = useState('')
  const [lossTimeBinding, setLossTimeBinding] = useState<string | null>(null)
  const [supplierOrderVersion, setSupplierOrderVersion] = useState('')
  const [supplierVersionBinding, setSupplierVersionBinding] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [useDelegation, setUseDelegation] = useState(false); const [delegationId, setDelegationId] = useState('')
  const delegations = useInfiniteQuery<DelegationPage,DelegationReadFailure,InfiniteData<DelegationPage>,readonly unknown[],number>({ queryKey: [namespace, shell.routeEntryGeneration, 'work-decision-delegations'],
    initialPageParam: 0, retry: false,
    getNextPageParam: last => last.hasMore ? last.page + 1 : undefined,
    enabled: useDelegation && company !== null && namespace !== null && !capabilityFailClosed(shell), queryFn: async ({ signal, pageParam }) => {
      const captured = boundary.capture(); const answer = await fetchDelegations(company!, false, pageParam, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      if (answer.type !== 'ok-single') throw new DelegationReadFailure(answer)
      return answer.data
    } })
  const pages = delegations.data?.pages ?? []
  const candidates = [...new Map(pages.flatMap(page => page.items).map(row => [row.id,row])).values()].filter(row => row.kind === kind
    && row.delegateUserId === shell.activeContext?.userId && row.revokedAtUtc === null)
  const people = pages.flatMap(page => page.people)
  const attempt = useScopedCommandAttempt<WorkDecisionRequest, WorkDecisionResult>(`work-decision:${kind}:${id}`,
    (payload, key) => sendWorkDecision(company!, kind, id, payload, key), payload => isWorkDecisionRequest(payload, kind))
  // A validated historical command resolves through its native receipt, not a newer maker's preview.
  const ibanChange = kind === 'finance.bank-account-iban-change.v1'
  const receiptRetryKind = ibanChange || kind === 'inventory.transfer-loss.v1' || kind === 'inventory.transfer-overage.v1'
  const historicalRetry = receiptRetryKind && attempt.hasPendingRetry
  const [reconcilingHistorical, setReconcilingHistorical] = useState(historicalRetry)
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'work-decision', kind, id],
    enabled: company !== null && namespace !== null && !capabilityFailClosed(shell) && !historicalRetry && !reconcilingHistorical, queryFn: async ({ signal }) => {
      const captured = boundary.capture(); const result = await fetchWorkDecision(company!, kind, id, signal)
      if (result.type === 'http') boundary.report(result.status, captured)
      return result
    } })
  if (company === null || capabilityFailClosed(shell)) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.title} />
  // Disabling a query does not erase cached data or a refetch already in flight.
  // An IBAN receipt retry must never display a replacement proposal as its human evidence.
  const suppressIbanPreview = ibanChange && (attempt.hasPendingRetry || reconcilingHistorical || attempt.status.kind === 'applied')
  const preview = !suppressIbanPreview && query.data?.type === 'ok-single' ? query.data.data : null
  const frozen = attempt.status.kind === 'busy' || attempt.hasPendingRetry
  const reconciledHistorical = reconcilingHistorical && !attempt.hasPendingRetry && (attempt.status.kind === 'applied' || attempt.status.kind === 'refused')
  const options = preview?.manufacturingPlanReapproval?.parentRollIds
  const selected = frozen ? attempt.pendingPayload?.manufacturingPlanReapproval?.selectedLabelIds ?? [] : selectionVersion === preview?.sourceVersion ? selectedLabels : []
  const selectionInvalid = options !== undefined && (selectionVersion !== preview?.sourceVersion || selectedLabels.length !== options.length
    || selectedLabels.some(id => !options.includes(id)))
  const customerReturn=kind === 'logistics.customer-return.v1'
  const loss = kind === 'inventory.transfer-loss.v1'
  const overage = kind === 'inventory.transfer-overage.v1'
  const timedTransfer = loss || overage
  const transferRequest = overage ? preview?.inventoryTransferOverage : preview?.inventoryTransferLoss
  const frozenTransferRequest = overage ? attempt.pendingPayload?.inventoryTransferOverage : attempt.pendingPayload?.inventoryTransferLoss
  const lossRequest = frozen ? frozenTransferRequest?.requestNumber : transferRequest?.requestNumber
  const lossReason = (customerReturn || timedTransfer || kind === 'inventory.stock-scrap.v1' || kind === 'inventory.stock-count.v1' || kind === 'purchasing.supplier-return-cost-exception.v1') && frozen ? attempt.pendingPayload?.reason ?? '' : reason
  const currentLossBinding = transferRequest === undefined ? null : `${preview?.sourceVersion}:${transferRequest.requestNumber}`
  const selectedUtc = workDecisionLocalUtc(decisionTime)
  const invalidLossTime = timedTransfer && (currentLossBinding === null || lossTimeBinding !== currentLossBinding || selectedUtc === null
    || overage && transferRequest !== undefined && selectedUtc !== null && Date.parse(selectedUtc) < Date.parse(transferRequest.requestedAtUtc))
  const supplierReturn = kind === 'purchasing.supplier-return-cost-exception.v1'
  const supplierProof = preview?.supplierReturnCostException
  const measuredSupplierReturn = supplierProof !== undefined && supplierProof.recordedExpectedPurchaseOrderVersion !== null
  const currentSupplierBinding = supplierProof === undefined ? null : `${preview?.sourceVersion}:${supplierProof.currentPurchaseOrderVersion}`
  const displayedSupplierVersion = frozen ? attempt.pendingPayload?.supplierReturnCostException?.expectedPurchaseOrderVersion ?? ''
    : supplierVersionBinding === currentSupplierBinding ? supplierOrderVersion : ''
  const invalidSupplierVersion = measuredSupplierReturn && (supplierVersionBinding !== currentSupplierBinding || !isWorkSupplierReturnVersion(supplierOrderVersion))
  const reasonLimit = workDecisionReasonLimit(kind) ?? 500
  const batch = kind === 'purchasing.goods-receipt-batch.v1'
  const counterparty = isCounterpartyWorkDecisionKind(kind)
  const reasonRequired = (approve: boolean) => preview?.approvalReasonRequired || !approve && !isSalesWorkDecisionKind(kind) && !counterparty && !customerReturn
  const invalidReason = (approve: boolean) => !customerReturn && ((kind === 'inventory.stock-count.v1' || kind === 'inventory.stock-scrap.v1') && !isWorkStockCountReason(reason.trim()) || supplierReturn && !approve && !isWorkSupplierReturnReason(reason.trim()) || overage && !isWorkTransferOverageReason(reason) || reasonRequired(approve) && reason.trim() === '' || (preview?.approvalReasonRequired || !approve || batch || counterparty) && (batch ? reason.length : reason.trim().length) > reasonLimit)
  const submit = async (approve: boolean) => {
    if (preview === null || approve && preview.approvalSupported === false || !approve && preview.rejectionSupported === false || frozen || approve && (selectionInvalid || invalidSupplierVersion) || invalidLossTime || invalidReason(approve) || useDelegation && !delegationId) return
    const answer = await attempt.run({ expectedVersion: preview.sourceVersion, approve,
      reason: batch ? reason === '' ? null : reason : preview.approvalReasonRequired || !approve || counterparty || customerReturn ? reason.trim() || null : null, delegationId: useDelegation ? delegationId : null,
      ...(customerReturn && approve ? {logisticsCustomerReturn:{inventoryMovementId:crypto.randomUUID()}} : {}),
      ...(supplierReturn && approve && measuredSupplierReturn ? { supplierReturnCostException: { expectedPurchaseOrderVersion: supplierOrderVersion } } : {}),
      ...(kind === 'logistics.plan-excess.v1' ? { logisticsPlanExcess: { expectedInstructionVersion: preview.logisticsPlanExcess!.instructionVersion } } : {}),
      ...(overage ? { inventoryTransferOverage: { requestNumber: preview.inventoryTransferOverage!.requestNumber, decidedAtUtc: workDecisionLocalUtc(decisionTime)! } } : {}),
      ...(loss ? { inventoryTransferLoss: { requestNumber: preview.inventoryTransferLoss!.requestNumber, decidedAtUtc: workDecisionLocalUtc(decisionTime)! } } : {}),
      ...(kind === 'manufacturing.plan.v1' && options !== undefined ? { manufacturingPlanReapproval: { selectedLabelIds: [...selectedLabels] } } : {}) })
    if (answer.type === 'applied') {
      if (ibanChange) setReconcilingHistorical(true)
      else void query.refetch()
    }
  }
  const retry = async () => {
    if (receiptRetryKind && attempt.hasPendingRetry) setReconcilingHistorical(true)
    const answer = await attempt.retry()
    if (answer.type === 'applied' && !receiptRetryKind) void query.refetch()
  }
  const stockProof = preview?.inventoryStockCount ?? preview?.inventoryStockScrap
  const stockScrap = kind === 'inventory.stock-scrap.v1'
  const cost = preview?.rules.find(rule => rule.ruleCode === 'inventory.opening-cost-unit-cost')?.actual
  return <section className={styles.page} aria-labelledby="work-decision-heading"><h1 id="work-decision-heading">{T.title}</h1><p>{T.intro}</p>
    <Link to="/onaylarim/kuyruk">{T.back}</Link>
    {preview === null ? reconciledHistorical ? <p>{T.priorDecisionResolved}</p> : historicalRetry ? <div><p>{T.priorDecisionPending}</p>
      <p>{T.version}: {attempt.pendingPayload?.expectedVersion}</p>
      {ibanChange ? <p>{T.ibanRetained}</p> : <><p>{overage ? T.overageRequestNumber : T.lossRequestNumber}: {frozenTransferRequest?.requestNumber}</p>
      <p>{T.lossFrozenTime}: {frozenTransferRequest?.decidedAtUtc}</p></>}
    </div> : <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>}
      subject={T.title} onRetry={() => void query.refetch()} /> : <>
      <h2>{inboxKindText(kind)} · {preview.documentLabel}</h2><p>{T.version}: {(ibanChange || customerReturn || supplierReturn || kind === 'logistics.dispatch-note.v1' || kind === 'finance.manual-bank-payment.v1' || kind === 'finance.bank-account.v1' || kind === 'inventory.stock-scrap.v1' || kind === 'inventory.stock-count.v1' || kind === 'logistics.plan-excess.v1' || overage) && frozen ? attempt.pendingPayload?.expectedVersion : preview.sourceVersion}</p>
      <section aria-label={INBOX.rulesHeading}><h3>{INBOX.rulesHeading}</h3>
        {preview.rules.length === 0 ? <p>{T.rulesUnavailable}</p> : <ul>{preview.rules.map((rule, index) => <li key={`${rule.ruleCode}:${index}`}>
          <p>{inboxRuleText(rule.ruleCode) ?? <>{INBOX.ruleUnknown} <code>{rule.ruleCode}</code></>}</p>
          {rule.threshold !== null || rule.actual !== null || rule.deviation !== null ? <dl className={styles.facts}>
            {rule.threshold !== null ? <><dt>{INBOX.threshold}</dt><dd>{formatWithUnit(rule.threshold, rule.unit)}</dd></> : null}
            {rule.actual !== null ? <><dt>{INBOX.actual}</dt><dd>{formatWithUnit(rule.actual, rule.unit)}</dd></> : null}
            {rule.deviation !== null ? <><dt>{INBOX.deviation}</dt><dd>{formatWithUnit(rule.deviation, rule.unit)}</dd></> : null}
          </dl> : null}
        </li>)}</ul>}
      </section>
      {kind === 'inventory.opening-cost.v1' ? <p>{cost == null ? T.hiddenCost : `${T.cost}: ${cost}`}</p> : null}
      {preview.logisticsCustomerReturn?<section aria-label={T.customerReturnProposal}><h3>{T.customerReturnProposal}</h3><dl className={styles.facts}>
        <dt>{T.customerReturnInstruction}</dt><dd>{preview.logisticsCustomerReturn.loadingInstructionId}</dd>
        <dt>{T.customerReturnLine}</dt><dd>{preview.logisticsCustomerReturn.loadingInstructionLineId}</dd>
        <dt>{T.customerReturnDelivery}</dt><dd>{preview.logisticsCustomerReturn.sourceDeliveryId}</dd>
        <dt>{T.customerReturnShipment}</dt><dd>{preview.logisticsCustomerReturn.sourceShipmentId}</dd>
        <dt>{T.customerReturnOrder}</dt><dd>{preview.logisticsCustomerReturn.sourceOrderId}</dd>
        <dt>{T.customerReturnOrderLine}</dt><dd>{preview.logisticsCustomerReturn.sourceOrderLineId}</dd>
        <dt>{T.customerReturnUnit}</dt><dd>{preview.logisticsCustomerReturn.unitId}</dd>
        <dt>{T.customerReturnUnitKind}</dt><dd>{T.customerReturnUnits[preview.logisticsCustomerReturn.quantityUnit]}</dd>
        <dt>{T.customerReturnQuantity}</dt><dd>{preview.logisticsCustomerReturn.quantity}</dd>
        <dt>{T.customerReturnDelivered}</dt><dd>{preview.logisticsCustomerReturn.sourceDeliveredQuantity}</dd>
        <dt>{CUSTOMER_RETURN.reason}</dt><dd>{preview.logisticsCustomerReturn.reason}</dd>
        <dt>{T.customerReturnRequestedBy}</dt><dd>{preview.logisticsCustomerReturn.requestedBy}</dd>
        <dt>{T.customerReturnOccurredAt}</dt><dd>{preview.logisticsCustomerReturn.occurredAtUtc}</dd>
        <dt>{T.customerReturnRequestedAt}</dt><dd>{preview.logisticsCustomerReturn.requestedAtUtc}</dd>
        <dt>{T.customerReturnStatus}</dt><dd>{({Pending:CUSTOMER_RETURN.pending,Approved:CUSTOMER_RETURN.approved,Rejected:CUSTOMER_RETURN.rejected})[preview.logisticsCustomerReturn.status]}</dd>
      </dl>{preview.logisticsCustomerReturn.weighing?<section aria-label={T.customerReturnWeighing}><h4>{T.customerReturnWeighing}</h4><dl className={styles.facts}>
        <dt>{T.customerReturnWeighingId}</dt><dd>{preview.logisticsCustomerReturn.weighing.weighingId}</dd>
        <dt>{T.customerReturnWeighingDirection}</dt><dd>{T.customerReturnDirections[preview.logisticsCustomerReturn.weighing.direction]}</dd>
        <dt>{T.customerReturnFirstKg}</dt><dd>{preview.logisticsCustomerReturn.weighing.firstWeighingKg}</dd>
        <dt>{T.customerReturnSecondKg}</dt><dd>{preview.logisticsCustomerReturn.weighing.secondWeighingKg}</dd>
        <dt>{T.customerReturnNetKg}</dt><dd>{preview.logisticsCustomerReturn.weighing.netKg}</dd>
        <dt>{T.customerReturnWeighedBy}</dt><dd>{preview.logisticsCustomerReturn.weighing.recordedBy}</dd>
        <dt>{T.customerReturnWeighedAt}</dt><dd>{preview.logisticsCustomerReturn.weighing.recordedAtUtc}</dd>
      </dl></section>:null}</section>:null}
      {preview.logisticsDispatchNote ? <section aria-label={T.dispatchProposal}><h3>{T.dispatchProposal}</h3><dl className={styles.facts}>
        <dt>{T.dispatchInstruction}</dt><dd>{preview.logisticsDispatchNote.instructionId}</dd>
        <dt>{DISPATCH.reference}</dt><dd>{preview.logisticsDispatchNote.dispatchNoteId}</dd>
        <dt>{T.dispatchCustomer}</dt><dd>{preview.logisticsDispatchNote.customerPartyId}</dd>
        <dt>{T.dispatchAccount}</dt><dd>{preview.logisticsDispatchNote.companyAccountId}</dd>
        <dt>{T.dispatchEffectiveOn}</dt><dd>{preview.logisticsDispatchNote.effectiveOn}</dd>
        <dt>{T.dispatchStatus}</dt><dd>{({Draft:DISPATCH.draft,Approved:DISPATCH.approved,Cancelled:DISPATCH.cancelled})[preview.logisticsDispatchNote.status]}</dd>
        <dt>{DISPATCH.number}</dt><dd>{preview.logisticsDispatchNote.number ?? T.dispatchNoNumber}</dd>
        <dt>{DISPATCH.eDispatchNumber}</dt><dd>{preview.logisticsDispatchNote.eDispatchNumber ?? T.dispatchNoNumber}</dd>
        <dt>{DISPATCH.createdBy}</dt><dd>{preview.logisticsDispatchNote.createdBy}</dd>
        <dt>{DISPATCH.createdAt}</dt><dd>{preview.logisticsDispatchNote.createdAtUtc}</dd>
      </dl><table><caption>{DISPATCH.caption}</caption><thead><tr>
        {[DISPATCH.lineNo,DISPATCH.shipment,DISPATCH.instructionLine,DISPATCH.order,T.dispatchProduct,T.dispatchUnit,DISPATCH.quantity,T.dispatchKg].map(label => <th key={label} scope="col">{label}</th>)}
      </tr></thead><tbody>{preview.logisticsDispatchNote.lines.map(line => <tr key={line.shipmentId}>
        <td>{line.lineNo}</td><td>{line.shipmentId}</td><td>{line.instructionLineId}</td><td>{line.sourceOrderId} / {line.sourceOrderLineId}</td>
        <td>{line.productId}</td><td>{line.unitId}</td><td>{line.quantity}</td><td>{line.quantityKg ?? T.dispatchNoKg}</td>
      </tr>)}</tbody></table></section> : null}
      {preview.bankAccountIbanChange ? <section aria-label={T.ibanProposal}><h3>{T.ibanProposal}</h3><dl className={styles.facts}>
        <dt>{T.ibanHolder}</dt><dd>{preview.bankAccountIbanChange.companyLegalName}</dd>
        <dt>{T.bankAccountId}</dt><dd>{preview.bankAccountIbanChange.accountId}</dd>
        <dt>{T.bankCompany}</dt><dd>{preview.bankAccountIbanChange.companyId}</dd>
        <dt>{T.ibanCurrent}</dt><dd>{preview.bankAccountIbanChange.currentIban === null ? T.ibanAbsent : preview.bankAccountIbanChange.currentIban}</dd>
        <dt>{IBAN_CHANGE_NEW}</dt><dd>{preview.bankAccountIbanChange.proposedIban}</dd>
        <dt>{IBAN_CHANGE_REASON}</dt><dd>{preview.bankAccountIbanChange.reason}</dd>
        <dt>{T.bankProposedBy}</dt><dd>{preview.bankAccountIbanChange.proposedBy}</dd>
        <dt>{T.bankProposedAt}</dt><dd>{preview.bankAccountIbanChange.proposedAtUtc}</dd>
      </dl><p>{T.ibanApprovalOnly}</p></section> : null}
      {preview.bankAccount ? <section aria-label={T.bankProposal}><h3>{T.bankProposal}</h3><dl className={styles.facts}>
        <dt>{BANK_COLUMN.name}</dt><dd>{preview.bankAccount.account.displayName}</dd>
        <dt>{BANK_COLUMN.currency}</dt><dd>{currencyFallback(preview.bankAccount.account.currencyId)}</dd>
        <dt>{T.bankAccountId}</dt><dd>{preview.bankAccount.account.id}</dd>
        <dt>{T.bankCompany}</dt><dd>{preview.bankAccount.account.companyId}</dd>
        <dt>{T.bankActive}</dt><dd>{preview.bankAccount.account.isActive ? T.bankYes : T.bankNo}</dd>
        <dt>{T.bankReceiptUsable}</dt><dd>{preview.bankAccount.account.isUsableForReceipt ? T.bankYes : T.bankNo}</dd>
        <dt>{T.bankCurrentVersion}</dt><dd>{preview.bankAccount.account.version}</dd>
        <dt>{T.bankProposedBy}</dt><dd>{preview.bankAccount.proposedBy}</dd>
        <dt>{T.bankProposedAt}</dt><dd>{preview.bankAccount.proposedAtUtc}</dd>
      </dl><p>{BANK_MASTER_NOTICE}</p></section> : null}
      {stockProof ? <section aria-label={stockScrap ? T.scrapProposal : T.countProposal}><h3>{stockScrap ? T.scrapProposal : T.countProposal}</h3><dl className={styles.facts}>
        <dt>{T.countLabel}</dt><dd>{stockProof.labelId}</dd><dt>{stockScrap ? T.scrapLabelVersion : T.countLabelVersion}</dt><dd>{stockProof.labelVersion}</dd>
        <dt>{T.countOwnerKind}</dt><dd>{T.countPartyKinds[stockProof.ownerKind]}</dd><dt>{T.countOwner}</dt><dd>{stockProof.ownerId}</dd>
        <dt>{T.countCustodianKind}</dt><dd>{T.countPartyKinds[stockProof.custodianKind]}</dd><dt>{T.countCustodian}</dt><dd>{stockProof.custodianId}</dd>
        <dt>{T.countLocation}</dt><dd>{stockProof.locationId}</dd><dt>{T.countProduct}</dt><dd>{stockProof.productId}</dd>
        <dt>{T.countBlock}</dt><dd>{T.countBlocks[stockProof.block]}</dd>
        <dt>{T.countBook}</dt><dd>{stockProof.bookKg}</dd><dt>{stockScrap ? T.scrapQuantity : T.countActual}</dt><dd>{preview.inventoryStockScrap?.scrapKg ?? preview.inventoryStockCount?.countedKg}</dd><dt>{stockScrap ? T.scrapRemaining : T.countDifference}</dt><dd>{preview.inventoryStockScrap?.remainingKg ?? preview.inventoryStockCount?.differenceKg}</dd>
        <dt>{stockScrap ? T.scrapAt : T.countAt}</dt><dd>{preview.inventoryStockScrap?.scrappedAtUtc ?? preview.inventoryStockCount?.countedAtUtc}</dd><dt>{stockScrap ? T.scrapReason : T.countReason}</dt><dd>{stockProof.reason}</dd>
        <dt>{stockScrap ? T.scrapRecordedBy : T.countRecordedBy}</dt><dd>{stockProof.recordedBy}</dd><dt>{T.countRecordedAt}</dt><dd>{stockProof.recordedAtUtc}</dd>
        <dt>{T.countBookPieces}</dt><dd>{stockProof.bookPieceCount ?? T.countNoPieces}</dd><dt>{stockScrap ? T.scrapPieces : T.countActualPieces}</dt><dd>{(stockScrap ? preview.inventoryStockScrap?.scrapPieceCount : preview.inventoryStockCount?.countedPieceCount) ?? T.countNoPieces}</dd>
      </dl><p>{stockScrap ? T.scrapReadOnly : T.countReadOnly}</p></section> : null}
      {supplierProof ? <section aria-label={T.supplierReturnProposal}><h3>{T.supplierReturnProposal}</h3><dl className={styles.facts}>
        <dt>{T.supplierReturnOrder}</dt><dd>{supplierProof.purchaseOrderId}</dd>
        <dt>{T.supplierReturnLine}</dt><dd>{supplierProof.purchaseOrderLineId}</dd>
        <dt>{T.supplierReturnAcceptance}</dt><dd>{supplierProof.goodsAcceptanceId}</dd>
        <dt>{T.supplierReturnMovement}</dt><dd>{supplierProof.inventoryMovementId}</dd>
        <dt>{SUPPLIER_RETURN.sourceBlock}</dt><dd>{SUPPLIER_RETURN_SOURCE_BLOCK_TEXT[supplierProof.sourceBlock]}</dd>
        <dt>{SUPPLIER_RETURN.quantityKg}</dt><dd>{supplierProof.quantityKg}</dd>
        <dt>{SUPPLIER_RETURN.costBasis}</dt><dd>{T.supplierReturnManualCost}</dd>
        <dt>{SUPPLIER_RETURN.unitCost}</dt><dd>{supplierProof.priceDisclosure === 'Visible' ? supplierProof.unitCostTryPerKg : SUPPLIER_RETURN.priceWithheld}</dd>
        <dt>{SUPPLIER_RETURN.reason}</dt><dd>{supplierProof.reason}</dd>
        <dt>{SUPPLIER_RETURN.occurredAtUtc}</dt><dd>{supplierProof.occurredAtUtc}</dd>
        <dt>{SUPPLIER_RETURN.requestedBy}</dt><dd>{supplierProof.requestedBy}</dd>
        <dt>{SUPPLIER_RETURN.requestedAtUtc}</dt><dd>{supplierProof.requestedAtUtc}</dd>
        <dt>{T.supplierReturnReplacement}</dt><dd>{supplierProof.replacementExpected ? SUPPLIER_RETURN.replacementExpected : SUPPLIER_RETURN.definitiveReturn}</dd>
        {measuredSupplierReturn ? <><dt>{SUPPLIER_RETURN.expectedOrderVersion}</dt><dd>{supplierProof.recordedExpectedPurchaseOrderVersion}</dd>
          <dt>{T.supplierReturnCurrentOrderVersion}</dt><dd>{supplierProof.currentPurchaseOrderVersion}</dd>
          <dt>{SUPPLIER_RETURN.undoQuantity}</dt><dd>{supplierProof.undoQuantity}</dd></> : null}
      </dl><p>{T.supplierReturnReadOnly}</p>
      </section> : null}
      {preview.logisticsWeighingDifference ? <section aria-label={T.weighingEvaluation}><h3>{T.weighingEvaluation}</h3><dl className={styles.facts}>
        <dt>{T.weighingNote}</dt><dd>{preview.documentLabel}</dd>
        <dt>{PLAN_EXCESS.instruction}</dt><dd>{preview.logisticsWeighingDifference.instructionId}</dd>
        <dt>{T.weighingTheoretical}</dt><dd>{preview.logisticsWeighingDifference.theoreticalKg}</dd>
        <dt>{WEIGHING.total}</dt><dd>{preview.logisticsWeighingDifference.scaleKg}</dd>
        <dt>{WEIGHING.difference}</dt><dd>{preview.logisticsWeighingDifference.differenceKg}</dd>
        <dt>{T.weighingBand}</dt><dd>{T.weighingBands[preview.logisticsWeighingDifference.bandLevel]}</dd>
        <dt>{T.weighingWarningBand}</dt><dd>{preview.logisticsWeighingDifference.warningBandPercent ?? T.weighingUnavailable}</dd>
        <dt>{T.weighingApprovalBand}</dt><dd>{preview.logisticsWeighingDifference.approvalBandPercent ?? T.weighingUnavailable}</dd>
        <dt>{T.weighingBlockBand}</dt><dd>{preview.logisticsWeighingDifference.blockBandPercent ?? T.weighingUnavailable}</dd>
        <dt>{T.weighingExcess}</dt><dd>{preview.logisticsWeighingDifference.totalExcessKg}</dd>
        <dt>{T.weighingRate}</dt><dd>{preview.logisticsWeighingDifference.approvalRatePercent ?? T.weighingUnavailable}</dd>
        <dt>{T.weighingDecision}</dt><dd>{T.weighingDecisions[preview.logisticsWeighingDifference.decision]}</dd>
        <dt>{T.weighingEnteredBy}</dt><dd>{preview.logisticsWeighingDifference.enteredBy.join(', ')}</dd>
        <dt>{T.weighingRecordedBy}</dt><dd>{preview.logisticsWeighingDifference.recordedBy}</dd>
        <dt>{T.weighingRecordedAt}</dt><dd>{preview.logisticsWeighingDifference.recordedAtUtc}</dd>
      </dl><p>{T.weighingEnteredComplete}</p><p>{T.weighingReadOnly}</p>
      <div className={styles.tableWrap} role="region" aria-label={T.weighingProducts} tabIndex={0}><table className={styles.table}><caption>{T.weighingProducts}</caption><thead><tr>
        <th scope="col">{T.weighingProduct}</th><th scope="col">{T.weighingNoteKg}</th><th scope="col">{WEIGHING.scale}</th><th scope="col">{WEIGHING.difference}</th>
        <th scope="col">{T.weighingRemaining}</th><th scope="col">{T.weighingCovered}</th><th scope="col">{T.weighingExcess}</th><th scope="col">{T.weighingTarget}</th>
      </tr></thead><tbody>{preview.logisticsWeighingDifference.products.map(product => <tr key={product.productId}>
        <th scope="row">{product.productId}</th><td>{product.noteKg}</td><td>{product.scaleKg}</td><td>{product.differenceKg}</td>
        <td>{product.remainingPhysicalKg}</td><td>{product.physicallyCoveredKg}</td><td>{product.excessKg}</td><td>{product.targetLineNo ?? T.weighingUnavailable}</td>
      </tr>)}</tbody></table></div></section> : null}
      {preview.inventoryTransferOverage ? <section aria-label={T.overageProposal}><h3>{T.overageProposal}</h3><dl>
        <dt>{T.overageTransfer}</dt><dd>{preview.inventoryTransferOverage.transferId}</dd>
        <dt>{T.overageLine}</dt><dd>{preview.inventoryTransferOverage.lineId}</dd>
        <dt>{T.overageKilograms}</dt><dd>{preview.inventoryTransferOverage.kilograms}</dd>
        <dt>{T.overagePieces}</dt><dd>{preview.inventoryTransferOverage.pieceCount === null ? T.overageNoPieces : preview.inventoryTransferOverage.pieceCount}</dd>
        <dt>{T.overageProposalReason}</dt><dd>{preview.inventoryTransferOverage.reason}</dd>
        <dt>{T.overageRequestedBy}</dt><dd>{preview.inventoryTransferOverage.requestedBy}</dd>
        <dt>{T.overageReceivedBy}</dt><dd>{preview.inventoryTransferOverage.receivedBy}</dd>
      </dl></section> : null}
      {preview.logisticsPlanExcess ? <section aria-label={T.planExcessProposal}><h3>{T.planExcessProposal}</h3><dl>
        <dt>{PLAN_EXCESS.instruction}</dt><dd>{preview.logisticsPlanExcess.instructionId}</dd>
        <dt>{PLAN_EXCESS.line}</dt><dd>{preview.logisticsPlanExcess.lineId}</dd>
        <dt>{T.instructionVersion}</dt><dd>{frozen ? attempt.pendingPayload?.logisticsPlanExcess?.expectedInstructionVersion : preview.logisticsPlanExcess.instructionVersion}</dd>
        <dt>{PLAN_EXCESS.quantity}</dt><dd>{preview.logisticsPlanExcess.quantity}</dd>
        <dt>{PLAN_EXCESS.reason}</dt><dd>{preview.logisticsPlanExcess.reason}</dd>
        <dt>{PLAN_EXCESS.proposer}</dt><dd>{preview.logisticsPlanExcess.proposedBy}</dd>
        <dt>{PLAN_EXCESS.proposedAt}</dt><dd>{preview.logisticsPlanExcess.proposedAtUtc}</dd>
      </dl><p>{PLAN_EXCESS.warning}</p></section> : null}
      {preview.counterpartyTaxIdentityChange ? <section aria-label={T.identityProof}><h3>{T.identityProof}</h3><dl>
        <dt>{TAX_IDENTITY_TEXT.pendingCurrent}</dt><dd>{preview.counterpartyTaxIdentityChange.currentTaxIdentityNumber}</dd>
        <dt>{TAX_IDENTITY_TEXT.pendingProposed}</dt><dd>{preview.counterpartyTaxIdentityChange.proposedTaxIdentityNumber} ({KIND_LABEL[preview.counterpartyTaxIdentityChange.proposedKind]})</dd>
        <dt>{TAX_IDENTITY_TEXT.reasonLabel}</dt><dd>{preview.counterpartyTaxIdentityChange.reason}</dd>
      </dl></section> : null}
      {preview.counterpartyLegalNameChange ? <section aria-label={T.identityProof}><h3>{T.identityProof}</h3><dl>
        <dt>{LEGAL_NAME_TEXT.pendingCurrent}</dt><dd>{preview.counterpartyLegalNameChange.currentLegalName}</dd>
        <dt>{LEGAL_NAME_TEXT.proposedLabel}</dt><dd>{preview.counterpartyLegalNameChange.proposedLegalName}</dd>
        <dt>{LEGAL_NAME_TEXT.reasonLabel}</dt><dd>{preview.counterpartyLegalNameChange.reason}</dd>
      </dl></section> : null}
      <section aria-label={T.reference}><h3>{T.reference}</h3>{preview.references.length === 0 ? <p>{INBOX.noReferences}</p>
        : <ul>{preview.references.map((ref, i) => <li key={i}>{inboxRoleText(ref.role) ?? T.reference}: {ref.label}</li>)}</ul>}</section>
      {measuredSupplierReturn && (preview.pending || frozen) ? <label>{T.supplierReturnSelectedOrderVersion}<input type="text" inputMode="numeric" autoComplete="off" disabled={frozen}
        value={displayedSupplierVersion} onChange={event => { setSupplierOrderVersion(event.target.value); setSupplierVersionBinding(currentSupplierBinding) }} /></label> : null}
      {measuredSupplierReturn && (preview.pending || frozen) ? <p>{T.supplierReturnVersionHelp}</p> : null}
      {(options !== undefined || frozen && selected.length > 0) ? <fieldset disabled={frozen}><legend>{T.reapprovalLabels}</legend><p>{T.reapprovalHelp}</p>
        {(frozen ? selected : options ?? []).map(labelId => <label key={labelId}><input type="checkbox" disabled={frozen} checked={selected.includes(labelId)}
          onChange={event => {
            setSelectionVersion(preview.sourceVersion)
            setSelectedLabels(event.target.checked ? [...selected, labelId] : selected.filter(id => id !== labelId))
          }} />{labelId}</label>)}
        <Button type="button" onClick={() => { setSelectedLabels([]); setSelectionVersion(null) }}>{T.clearLabels}</Button>
      </fieldset> : null}
      {timedTransfer ? <div><p>{overage ? T.overageRequestNumber : T.lossRequestNumber}: {lossRequest}</p>
        {frozen && frozenTransferRequest ? <p>{T.lossFrozenTime}: {frozenTransferRequest.decidedAtUtc}</p>
          : <p>{T.lossRequestedAt}: {transferRequest?.requestedAtUtc}</p>}
      </div> : null}
      {kind === 'sales.order-draft.v1' && hasCapability(shell, AI_PANEL_CAPABILITY) && hasCapability(shell, 'sales.drafts.detail')
        ? <WorkSalesSummary companyId={preview.companyId} draftId={preview.itemId} expectedVersion={preview.sourceVersion} /> : null}
      {preview.pending && !reconciledHistorical ? <fieldset disabled={frozen}>{preview.rejectionSupported === false ? <p>{kind === 'logistics.dispatch-note.v1' ? T.dispatchApprovalOnly : kind === 'finance.bank-account.v1' ? T.bankApprovalOnly : kind === 'logistics.weighing-difference.v1' ? T.weighingApprovalOnly : kind === 'logistics.plan-excess.v1' ? T.planExcessApprovalOnly : kind === 'manufacturing.plan.v1' ? T.manufacturingReapprovalOnly : kind === 'purchasing.incoming-invoice-variance.v1' ? T.varianceApprovalOnly : T.approvalOnly}</p> : null}
        {preview.rejectionSupported !== false || preview.approvalReasonRequired ? <label>{customerReturn ? CUSTOMER_RETURN.note : counterparty ? T.optionalNote : kind === 'manufacturing.plan.v1' ? T.manufacturingRejectReason : T.reason}<textarea value={lossReason} maxLength={customerReturn?undefined:reasonLimit} onChange={event => setReason(event.target.value)} /></label> : null}
        {kind === 'inventory.stock-count.v1' || stockScrap ? <p>{T.countReasonHelp}</p> : null}
        {kind === 'purchasing.service-order.v1' || kind === 'purchasing.incoming-invoice-variance.v1' ? <p>{T.approvalReasonRequired(reasonLimit)}</p> : null}
        {kind === 'manufacturing.plan.v1' && preview.rejectionSupported ? <p>{T.manufacturingRejectHelp}</p> : null}
        {kind === 'purchasing.request.v1' || supplierReturn ? <p>{T.rejectionReasonRequired(reasonLimit)}</p> : null}
        {counterparty ? <p>{T.optionalNoteHelp}</p> : null}
        {customerReturn?<><p>{T.customerReturnNoteHelp}</p>{preview.approvalSupported===false?<p role="status">{T.customerReturnPieceUnavailable}</p>:null}</>:null}
        {timedTransfer ? <><label>{T.lossDecisionTime}<input type="datetime-local" step="1" disabled={frozen} value={decisionTime} onChange={event => { setDecisionTime(event.target.value); setLossTimeBinding(currentLossBinding) }} /></label><p>{overage ? T.overageReasonHelp : T.lossReason}</p></> : null}
        {batch ? <p>{T.batchReason(reasonLimit)}</p> : null}
        <label><input type="checkbox" checked={useDelegation} onChange={event => { setUseDelegation(event.target.checked); setDelegationId('') }} />{T.delegation}</label>
        {useDelegation ? <><label>{T.delegationChoice}<select value={delegationId} onChange={event => setDelegationId(event.target.value)}><option value="">{T.choose}</option>
          {candidates.map(row => <option key={row.id} value={row.id}>{delegationPersonLabel(people,row.delegatorUserId)} · {new Date(row.startsAtUtc).toLocaleString('tr-TR')} – {new Date(row.endsAtUtc).toLocaleString('tr-TR')} · {row.reason}</option>)}</select></label>
          {delegations.isPending ? <StateBlock state={{kind:'initial-loading'}} subject={T.delegationChoice} />
            : delegations.error instanceof DelegationReadFailure ? <StateBlock state={classifyOutcome(delegations.error.outcome) as RemoteViewState<unknown>} subject={T.delegationChoice}
              onRetry={() => void (pages.length === 0 ? delegations.refetch() : delegations.fetchNextPage())} />
            : candidates.length === 0 ? <p>{delegations.hasNextPage ? T.delegationNoMatchesYet : T.delegationEmpty}</p> : null}
          {delegations.hasNextPage ? <><p>{T.delegationMoreAvailable}</p><Button type="button" disabled={delegations.isFetchingNextPage}
            onClick={() => void delegations.fetchNextPage()}>{T.delegationMore}</Button></> : null}</> : null}
        <div className={styles.actions}><Button type="button" disabled={preview.approvalSupported===false || invalidLossTime || selectionInvalid || invalidSupplierVersion || useDelegation && !delegationId || invalidReason(true)} onClick={() => void submit(true)}>{T.approve}</Button>
          {preview.rejectionSupported !== false ? <Button type="button" variant="secondary" disabled={invalidLossTime || useDelegation && !delegationId || invalidReason(false)} onClick={() => void submit(false)}>{T.reject}</Button> : null}</div></fieldset> : <p>{reconciledHistorical ? T.priorDecisionResolved : T.closed}</p>}
    </>}
    {attempt.status.kind === 'unknown' ? <div role="alert"><p>{T.unknown}</p><Button type="button" onClick={() => void retry()}>{T.retry}</Button></div> : null}
    {attempt.status.kind === 'refused' ? <p role="alert">{T.refused} ({attempt.status.status})</p> : null}
    {attempt.status.kind === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
