import type { WorkInvoiceLineCogsPreview } from './workInvoiceLineCogsDecision'
import type { CostProofRatio, CostProofReturn, ManufacturingCostShipment } from '../accounting/postedManufacturingCostProof'
import type { PurchaseCostShipment } from '../accounting/postedPurchaseCostProof'
import { INVOICE_LINE_COGS_TEXT as C } from '../accounting/invoiceLineCogsText'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import styles from './Work.module.css'
const ratio=(value:CostProofRatio)=>`${value.numerator} / ${value.denominator}`
type Fields=Readonly<Partial<Record<keyof typeof T.invoiceLineCogsFields,string|number|boolean|null>>>
function Facts({fields}:{readonly fields:Fields}) {
  return <dl className={styles.facts}>{Object.entries(fields).map(([key,value])=><div key={key}>
    <dt>{T.invoiceLineCogsFields[key as keyof typeof T.invoiceLineCogsFields]}</dt>
    <dd>{value===null?T.reversalAbsent:typeof value==='boolean'?value?T.invoiceLineCogsYes:T.invoiceLineCogsNo:value}</dd>
  </div>)}</dl>
}
function Returns({rows}:{readonly rows:readonly CostProofReturn[]}) {
  return <section aria-label={T.invoiceLineCogsReturns}><h5>{T.invoiceLineCogsReturns}</h5>{rows.length===0?<p>{T.reversalAbsent}</p>:
    rows.map(r=><Facts key={r.returnId} fields={{returnId:r.returnId,movementId:r.movementId,movementLineNumber:r.movementLineNumber,
      sourceIssueMovementId:r.sourceIssueMovementId,quantityKg:r.quantityKg,recordedAtUtc:r.recordedAtUtc}}/>)}</section>
}
function Shipment({value:s}:{readonly value:ManufacturingCostShipment<string>|PurchaseCostShipment<string>}) {
  return <section aria-label={T.invoiceLineCogsShipment}><h5>{T.invoiceLineCogsShipment}</h5>
    <Facts fields={{shipmentId:s.shipmentId,movementId:s.movementId,movementLineNumber:s.movementLineNumber,loadingInstructionLineId:s.loadingInstructionLineId,
      productId:s.productId,labelId:s.labelId,physicalQuantityKg:s.physicalQuantityKg,rootShare:ratio(s.rootShare),netRootQuantityKg:ratio(s.netRootQuantityKg),
      recordedAtUtc:s.recordedAtUtc,provenOriginalSequence:s.provenOriginalSequence,...('hasReturns'in s?{hasReturns:s.hasReturns??false}:{})}}/>
    {s.returns===undefined?null:<Returns rows={s.returns}/>}</section>
}
export function WorkInvoiceLineCogsFacts({human}:{readonly human:WorkInvoiceLineCogsPreview}) {
  const p=human.proposal,a=p.accountPlan,s=human.source,e=p.postedEvidence
  return <section aria-label={T.invoiceLineCogsTitle}><h3>{T.invoiceLineCogsTitle}</h3><p>{T.invoiceLineCogsReadOnly}</p>
    <Facts fields={{proposalId:p.proposalId,companyId:p.companyId,invoiceId:p.invoiceId,invoiceLineId:p.invoiceLineId,finalizedVersion:p.finalizedVersion,
      version:p.version,expectedLineVersion:p.expectedLineVersion,currentLineVersion:human.currentLineVersion,status:C.statuses[p.status],classification:C.classes[p.classification],
      periodId:p.periodId,transactionDate:p.transactionDate,accountingDate:p.accountingDate,valueDate:p.valueDate,preparedBy:p.preparedBy,preparedAtUtc:p.preparedAtUtc,
      approvedBy:p.approvedBy,approvedAtUtc:p.approvedAtUtc,reason:p.reason,reversingCorrectionJournalId:p.reversingCorrectionJournalId,
      replacementCorrectionJournalId:p.replacementCorrectionJournalId}}/>
    <h4>{T.invoiceLineCogsPlan}</h4><Facts fields={{packageId:a.packageId,packageVersion:a.packageVersion,catalogVersion:a.catalogVersion,
      sourceVersion:a.sourceVersion,accountId:a.account.accountId,code:a.account.code,name:a.account.name}}/>
    <h4>{T.invoiceLineCogsSource}</h4><Facts fields={{companyId:s.companyId,invoiceId:s.invoiceId,invoiceLineId:s.invoiceLineId,finalizationAdmissionId:s.finalizationAdmissionId,
      finalizedVersion:s.finalizedVersion,productId:s.productId,shipmentId:s.shipmentId,dispatchNoteId:s.dispatchNoteId,dispatchNoteLineNo:s.dispatchNoteLineNo,
      invoiceLineNo:s.invoiceLineNo,invoiceDate:s.invoiceDate,officialInvoiceNumber:s.officialInvoiceNumber,ettn:s.ettn}}/>
    <h4>{T.invoiceLineCogsPosted}</h4>{e===null?<p>{T.reversalAbsent}</p>:<Facts fields={{companyId:e.companyId,invoiceId:e.invoiceId,invoiceLineId:e.invoiceLineId,
      finalizedVersion:e.finalizedVersion,journalId:e.journalId,cogsJournalLineNumber:e.cogsJournalLineNumber,counterJournalLineNumber:e.counterJournalLineNumber,
      cogsAccountId:e.cogsAccountId,counterAccountId:e.counterAccountId,currencyId:e.currencyId,amount:e.amount}}/>}
    {p.attributedEvidence===undefined?null:<section aria-label={T.invoiceLineCogsManufacturing}><h4>{T.invoiceLineCogsManufacturing}</h4>{p.attributedEvidence.map(e=>{
      const m=e.source
      return <section key={e.source.fingerprint}><Facts fields={{currentJournalId:e.currentJournalId,currentCogsAccountId:e.currentCogsAccountId,counterAccountId:e.counterAccountId,
        currentJournalTotal:e.currentJournalTotal,companyId:m.companyId,sourceEventId:m.sourceEventId,outputId:m.outputId,generationId:m.generationId,generationVersion:m.generationVersion,
        receiptMovementId:m.receiptMovementId,journalId:m.journalId,currencyId:m.currencyId,cogsAccountId:m.cogsAccountId,journalTotal:m.journalTotal,signedAmount:m.signedAmount,
        exactSignedAmount:ratio(m.exactSignedAmount),signedRoundingResidue:m.signedRoundingResidue,moneyProfileId:m.moneyProfileId,moneyProfileVersion:m.moneyProfileVersion,
        moneyScale:m.moneyScale,profileAccountingDate:m.profileAccountingDate}}/><Shipment value={m.shipment}/></section>
    })}</section>}
    {p.purchaseAttributedEvidence===undefined?null:<section aria-label={T.invoiceLineCogsPurchase}><h4>{T.invoiceLineCogsPurchase}</h4>{p.purchaseAttributedEvidence.map(e=>{
      const m=e.source,b=m.approval,r=b.recordedRounding
      return <section key={e.source.fingerprint}><Facts fields={{currentJournalId:e.currentJournalId,currentCogsAccountId:e.currentCogsAccountId,counterAccountId:e.counterAccountId,
        currentJournalTotal:e.currentJournalTotal,shipmentId:m.shipmentId,movementId:m.movementId,movementLineNumber:m.movementLineNumber,productId:m.productId,
        physicalQuantityKg:m.physicalQuantityKg,signedAmount:m.signedAmount,exactSignedAmount:ratio(m.exactSignedAmount),exactSignedResidue:ratio(m.exactSignedResidue)}}/>
        <h5>{T.invoiceLineCogsApproval}</h5><Facts fields={{companyId:b.companyId,sourceEventId:b.sourceEventId,invoiceLineId:b.invoiceLineId,proposalId:b.proposalId,
          proposalVersion:b.proposalVersion,journalId:b.journalId,status:b.status==='Posted'?C.posted:C.approvedWithoutJournal,signedAmount:b.signedAmount,currencyId:b.currencyId,
          moneyProfileId:b.moneyProfileId,moneyProfileVersion:b.moneyProfileVersion,moneyScale:b.moneyScale,cogsAccountId:b.cogsAccountId,counterAccountId:b.counterAccountId,
          accountingDate:b.accountingDate,preparedBy:b.preparedBy,approvedBy:b.approvedBy,approvedAtUtc:b.approvedAtUtc}}/>
        <h5>{T.invoiceLineCogsRounding}</h5><Facts fields={{currencyId:r.currencyId,moneyProfileId:r.moneyProfileId,moneyProfileVersion:r.moneyProfileVersion,moneyScale:r.moneyScale,
          exactSignedAmount:ratio(r.rounding.exactSignedAmount),roundedSignedAmount:r.rounding.roundedSignedAmount,exactSignedResidue:ratio(r.rounding.exactSignedResidue),ruleVersion:r.rounding.ruleVersion,
          ...(r.rounding.exactSignedAmountText===undefined?{}:{exactSignedAmountText:r.rounding.exactSignedAmountText}),
          ...(r.rounding.exactSignedResidueText===undefined?{}:{exactSignedResidueText:r.rounding.exactSignedResidueText})}}/>
        <section aria-label={T.invoiceLineCogsCandidates}><h5>{T.invoiceLineCogsCandidates}</h5>{m.candidates.map(c=><section key={c.acceptanceId}>
          <Facts fields={{companyId:c.companyId,sourceEventId:c.sourceEventId,invoiceLineId:c.invoiceLineId,acceptanceId:c.acceptanceId,receiptMovementId:c.receiptMovementId,
            rootLabelId:c.rootLabelId,productId:c.productId,invoicedRootShare:ratio(c.invoicedRootShare),invoicedNetQuantityKg:ratio(c.invoicedNetQuantityKg),exactTlAmount:ratio(c.exactTlAmount)}}/>
          <Shipment value={c.shipment}/></section>)}</section></section>
    })}</section>}
    {p.attributedCorrections===undefined?null:<section aria-label={T.invoiceLineCogsCorrections}><h4>{T.invoiceLineCogsCorrections}</h4>{p.attributedCorrections.map(c=><Facts key={c.attributionFingerprint}
      fields={{sourceJournalId:c.sourceJournalId,reversingCorrectionJournalId:c.reversingCorrectionJournalId,replacementCorrectionJournalId:c.replacementCorrectionJournalId}}/>)}</section>}
  </section>
}
