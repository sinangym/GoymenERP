import type {WorkDecisionPreview} from './workDecisionApi'
import {INVOICE_LINE_COGS_WORK_KIND as kind} from './workInvoiceLineCogsDecision'
export const invoiceLineCogsId='ac540000-0000-4000-8000-000000000002'
export const cogsInvoice='ac540000-0000-4000-8000-000000000005',cogsLine='ac540000-0000-4000-8000-000000000006'
const id=(n:number)=>`ac540000-0000-4000-8000-${String(n).padStart(12,'0')}`
const sha=(c:string)=>c.repeat(64),at='2026-11-28T09:00:00.0000001Z'
const ratio={numerator:'1',denominator:'3'}
// Same native manufacturing/purchase proof shapes as the existing native codec fixtures, with Work-only Int64 text.
export function invoiceLineCogsFixture(company:string,approved=false,current='0') {
  const maker=id(4),checker=id(3),product=id(9),shipment=id(10),counter=id(12),account=id(11),currency=id(13)
  const returnRow={returnId:id(40),movementId:id(41),movementLineNumber:1,sourceIssueMovementId:id(14),quantityKg:0.25,recordedAtUtc:at}
  const physical={shipmentId:shipment,movementId:id(14),movementLineNumber:1,loadingInstructionLineId:id(15),productId:product,labelId:id(16),
    physicalQuantityKg:1,rootShare:ratio,netRootQuantityKg:ratio,recordedAtUtc:at,provenOriginalSequence:'9223372036854775807',physicalProofJson:'{"SENTETIK":"PRIVATE PHYSICAL JSON"}'}
  const manufacturing={companyId:company,sourceEventId:id(17),outputId:id(18),generationId:id(19),generationVersion:'9223372036854775807',receiptMovementId:id(20),
    journalId:id(21),currencyId:currency,cogsAccountId:account,counterAccountId:counter,journalTotal:'100.01',signedAmount:'33.34',exactSignedAmount:{numerator:'10001',denominator:'300'},
    signedRoundingResidue:'0',moneyProfileId:id(22),moneyProfileVersion:'9007199254740993',moneyScale:2,profileAccountingDate:'2026-11-28',
    shipment:{...physical,hasReturns:true,returns:[returnRow]},sourcePayloadSha256:sha('A'),lineageSha256:sha('B'),historySha256:sha('C'),fingerprint:sha('D')}
  const approval={companyId:company,sourceEventId:id(23),invoiceLineId:id(24),proposalId:id(25),proposalVersion:'9007199254740993',journalId:id(26),
    status:'Posted' as 'Posted'|'RoundedNoCost',signedAmount:'0.01',currencyId:currency,moneyProfileId:id(22),moneyProfileVersion:'9223372036854775807',moneyScale:2,
    cogsAccountId:account,counterAccountId:counter,accountingDate:'2026-11-28',preparedBy:maker,approvedBy:checker,approvedAtUtc:at,
    recordedRounding:{currencyId:currency,moneyProfileId:id(22),moneyProfileVersion:'9223372036854775807',moneyScale:2,
      rounding:{exactSignedAmount:{numerator:'1',denominator:'100'},roundedSignedAmount:'0.01',exactSignedResidue:{numerator:'0',denominator:'1'},
        ruleVersion:'accounting.inventory-cost-component-total.v1',exactSignedAmountText:'0.01',exactSignedResidueText:'0'}},sourceProofSha256:sha('E'),fingerprint:sha('F')}
  const purchase={approval,shipmentId:shipment,movementId:physical.movementId,movementLineNumber:1,productId:product,physicalQuantityKg:1,signedAmount:'0.01',
    exactSignedAmount:{numerator:'1',denominator:'100'},exactSignedResidue:{numerator:'0',denominator:'1'},fingerprint:sha('1'),
    candidates:[{companyId:company,sourceEventId:approval.sourceEventId,invoiceLineId:approval.invoiceLineId,acceptanceId:id(27),receiptMovementId:id(28),rootLabelId:id(29),productId:product,
      invoicedRootShare:ratio,invoicedNetQuantityKg:ratio,exactTlAmount:{numerator:'1',denominator:'100'},shipment:{...physical,returns:[returnRow]},
      sourceProofSha256:approval.sourceProofSha256,physicalSourceSha256:sha('2'),fingerprint:sha('3')}]}
  const proposal={proposalId:invoiceLineCogsId,companyId:company,invoiceId:cogsInvoice,invoiceLineId:cogsLine,finalizedVersion:'9007199254740993',version:approved?'2':'1',
    expectedLineVersion:'0',status:approved?'Approved' as const:'Prepared' as const,classification:2 as const,
    accountPlan:{packageId:id(30),packageVersion:'9223372036854775807',catalogVersion:'9007199254740993' as string|null,sourceVersion:'SENTETIK retained plan',sourceSha256:sha('4'),
      account:{accountId:account,code:'620',name:'SENTETIK retained account'}},periodId:id(31),transactionDate:'2026-11-27',accountingDate:'2026-11-28',valueDate:null as string|null,
    preparedBy:maker,preparedAtUtc:at,approvedBy:approved?checker:null,approvedAtUtc:approved?at:null,reason:'SENTETIK retained COGS reason',
    postedEvidence:{companyId:company,invoiceId:cogsInvoice,invoiceLineId:cogsLine,finalizedVersion:'9007199254740993',journalId:id(32),cogsJournalLineNumber:1,counterJournalLineNumber:2,
      cogsAccountId:account,counterAccountId:counter,currencyId:currency,amount:'100.0100',sourceFingerprint:sha('5')},
    reversingCorrectionJournalId:approved?id(33):null,replacementCorrectionJournalId:approved?id(34):null,
    attributedEvidence:[{source:manufacturing,currentJournalId:manufacturing.journalId,currentCogsAccountId:account,counterAccountId:counter,currentJournalTotal:'100.0100'}],
    purchaseAttributedEvidence:[{source:purchase,currentJournalId:approval.journalId as string|null,currentCogsAccountId:account,counterAccountId:counter,currentJournalTotal:'0.0100'}],
    ...(approved?{attributedCorrections:[{attributionFingerprint:manufacturing.fingerprint,sourceJournalId:manufacturing.journalId,reversingCorrectionJournalId:id(35),replacementCorrectionJournalId:id(36)}]}:{})}
  return {companyId:company,kind,itemId:invoiceLineCogsId,documentLabel:invoiceLineCogsId.slice(0,8),sourceVersion:proposal.version,pending:!approved&&current==='0',
    approvalReasonRequired:false,rejectionSupported:false,references:[{role:'invoice',label:cogsInvoice.slice(0,8),recordId:cogsInvoice,link:null},
      {role:'invoice-line',label:cogsLine.slice(0,8),recordId:cogsLine,link:null}],
    rules:[{ruleCode:'accounting.invoice-line-cogs-correction-approval-required',threshold:null,actual:null,deviation:null,unit:null}],
    invoiceLineCogs:{proposal,source:{companyId:company,invoiceId:cogsInvoice,invoiceLineId:cogsLine,finalizationAdmissionId:id(37),finalizedVersion:proposal.finalizedVersion,
      productId:product,shipmentId:shipment,dispatchNoteId:id(38),dispatchNoteLineNo:1,invoiceLineNo:1,invoiceDate:'2026-11-27',officialInvoiceNumber:'SENTETIK INV-54',ettn:id(39),fingerprint:sha('6')},
      currentLineVersion:approved&&current==='0'?'1':current,sourceFactsDigest:sha('7'),previewDigest:sha('8')}} satisfies WorkDecisionPreview
}
