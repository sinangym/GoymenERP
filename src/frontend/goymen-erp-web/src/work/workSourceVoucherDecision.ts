import type { SourceVoucher } from '../accounting/sourceVoucherApi'
import { decodeJournalReversalProposal, type JournalReversalProposal } from '../accounting/journalReversalContract'
import { isBoundUuid } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'
import { catalogVersion } from './workApprovedSourceCatalogDecision'

// Candidate only: root owns the generic Work adapter and paired publication separately.
export const SOURCE_VOUCHER_WORK_KIND = 'accounting.source-voucher.v1'
export type WorkSourceVoucher = Omit<SourceVoucher,'finalizedVersion'|'version'|'mappingVersion'> & {
  readonly finalizedVersion:string;readonly version:string;readonly mappingVersion:string
}
export interface WorkSourceVoucherPreview {
  readonly voucher:WorkSourceVoucher;readonly createdAtUtc:string;readonly sourceCancelled:boolean;readonly periodOpen:boolean
}
export interface SourceVoucherDecisionRequest {readonly expectedVersion:'1';readonly approve:true;readonly reason:null;readonly delegationId:string|null}
export const SOURCE_VOUCHER_WORK_TEXT = {
  title:'Fatura fişi onay bilgileri',readOnly:'Kaynak fatura fişi ve satırları salt okunurdur. Onay, mevcut fiş için muhasebe işlemini yürütür.',
  lines:'Fatura fişi satırları',reversal:'Kaynak iptali ters kayıt önerisi',absent:'—',yes:'Evet',no:'Hayır',
  retained:'Önceki onayınız yeniden doğrulanıyor. Karar verdiğiniz sürüm korunuyor; güncel fiş bilgileri bu isteğin yerine kullanılmıyor.',
  fields:{id:'Fiş kimliği',companyId:'Şirket',documentKind:'Kaynak türü',sourceDocumentId:'Kaynak belge',invoiceId:'Fatura',finalizedVersion:'Kesinleşen fatura sürümü',
    sourceActorId:'Faturayı kesinleştiren',partyId:'Cari',documentCurrencyId:'Belge para birimi',exchangeRate:'Döviz kuru',documentDate:'Fatura tarihi',dueDate:'Vade tarihi',
    status:'Durum',version:'Fiş sürümü',mappingVersion:'Hesap eşleme sürümü',periodId:'Muhasebe dönemi',journalId:'Yevmiye kaydı',approvedBy:'Onaylayan',approvedAtUtc:'Onay zamanı',
    cancellationReason:'İptal gerekçesi',officialInvoiceNo:'Resmî fatura numarası',functionalPayable:'Ödenecek TL',cancellationId:'Kaynak iptal kaydı',
    createdAtUtc:'Fiş kayıt zamanı',sourceCancelled:'Kaynak iptal edilmiş',periodOpen:'Fatura dönemi açık',lineNumber:'Satır',mappingKey:'Hesap eşleme anahtarı',side:'Yön',
    accountId:'Hesap kimliği',functionalAmount:'TL tutarı',sourceLineNumber:'Kaynak satır',vatRateCode:'KDV oran kodu',accountCode:'Hesap kodu',accountName:'Hesap adı',
    proposalId:'Öneri kimliği',sourceJournalId:'Kaynak yevmiye',reversalJournalId:'Ters yevmiye',transactionDate:'İşlem tarihi',accountingDate:'Muhasebe tarihi',valueDate:'Valör tarihi',
    reason:'Gerekçe',proposedBy:'Hazırlayan',proposedAtUtc:'Hazırlama zamanı',decidedBy:'Karar veren',decidedAtUtc:'Karar zamanı',decisionReason:'Karar gerekçesi'}
} as const
const rec=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const nullableGuid=(v:unknown)=>v===null||guid(v)
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const nullableText=(v:unknown)=>v===null||typeof v==='string'
const int=(v:unknown):v is number=>typeof v==='number'&&Number.isInteger(v)&&v>0&&v<=2147483647
const utc=(v:unknown):v is string=>transferDispatchUtc(v)&&v===v.trim()
  &&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
const nullableUtc=(v:unknown)=>v===null||utc(v)
// Canonical native decimal text only; its exact native CLR representability is checked by the existing adapter callback on magnitude. Raw text is never changed.
const canonicalAmount=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()&&/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?$/.test(v)&&v!=='-0'
const voucherKeys=['id','companyId','documentKind','sourceDocumentId','invoiceId','finalizedVersion','sourceActorId','partyId','documentCurrencyId','exchangeRate',
  'documentDate','dueDate','status','version','mappingVersion','periodId','journalId','approvedBy','approvedAtUtc','cancellationReason','lines','officialInvoiceNo','functionalPayable'] as const
const lineKeys=['lineNumber','mappingKey','side','accountId','functionalAmount','sourceLineNumber','vatRateCode'] as const
export const isSourceVoucherDecisionRequest=(v:unknown):v is SourceVoucherDecisionRequest=>rec(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId'])
  &&v.expectedVersion==='1'&&v.approve===true&&v.reason===null&&nullableGuid(v.delegationId)

/** Work's three long fields are text; the native numeric decoder and its defaults are deliberately unchanged. */
export function decodeWorkSourceVoucherPreview(value:unknown,company:string,id:string,date:(v:unknown)=>v is string,nativeAmount:(v:unknown)=>boolean):WorkSourceVoucherPreview|null {
  if(!guid(company)||!guid(id)||!rec(value)||!hasExactKeys(value,['voucher','createdAtUtc','sourceCancelled','periodOpen'])||!rec(value.voucher)
    ||!utc(value.createdAtUtc)||typeof value.sourceCancelled!=='boolean'||typeof value.periodOpen!=='boolean')return null
  const v=value.voucher
  const amount=(x:unknown)=>canonicalAmount(x)&&nativeAmount(x.startsWith('-')?x.slice(1):x)
  if(!hasExactKeys(v,voucherKeys,['cancellationId','reversalProposal'])||!guid(v.companyId)||!same(v.companyId,company)||!guid(v.id)||!same(v.id,id)
    ||!['sourceDocumentId','invoiceId','sourceActorId','partyId','documentCurrencyId'].every(k=>guid(v[k]))
    ||!['finalizedVersion','version','mappingVersion'].every(k=>catalogVersion(v[k]))
    ||v.documentKind!=='sales-invoice'&&v.documentKind!=='purchase-invoice'
    ||typeof v.status!=='string'||!['AwaitingApproval','PeriodWaiting','Posted','Cancelled','CancellationReversalPending','CancelledByReversal'].includes(v.status)
    ||!date(v.documentDate)||v.dueDate!==null&&!date(v.dueDate)||v.exchangeRate!==null&&(typeof v.exchangeRate!=='string'||v.exchangeRate!==v.exchangeRate.trim()||!/^(?:0|[1-9][0-9]{0,17})(?:\.[0-9]{1,8})?$/.test(v.exchangeRate)||/^0(?:\.0+)?$/.test(v.exchangeRate)||!nativeAmount(v.exchangeRate))
    ||!['periodId','journalId','approvedBy'].every(k=>nullableGuid(v[k]))||!nullableUtc(v.approvedAtUtc)
    ||!nullableText(v.cancellationReason)||!nullableText(v.officialInvoiceNo)||!amount(v.functionalPayable)
    ||v.cancellationId!==undefined&&!nullableGuid(v.cancellationId)||value.sourceCancelled!==(v.cancellationId!=null)
    ||!value.sourceCancelled&&(v.cancellationReason!==null||v.reversalProposal!=null)
    ||!value.sourceCancelled&&(v.status==='CancellationReversalPending'||v.status==='CancelledByReversal')
    ||!Array.isArray(v.lines)||v.lines.length===0)return null
  for(const line of v.lines)if(!rec(line)||!hasExactKeys(line,lineKeys,['accountCode','accountName'])||!int(line.lineNumber)||!text(line.mappingKey)
    ||line.side!==1&&line.side!==2||!guid(line.accountId)||!amount(line.functionalAmount)||line.sourceLineNumber!==null&&!int(line.sourceLineNumber)
    ||!nullableText(line.vatRateCode)||line.accountCode!==undefined&&!nullableText(line.accountCode)||line.accountName!==undefined&&!nullableText(line.accountName))return null
  if(new Set(v.lines.map(line=>line.lineNumber)).size!==v.lines.length)return null
  if(v.status==='AwaitingApproval'||v.status==='PeriodWaiting'){
    if(value.sourceCancelled||v.version!=='1'||v.journalId!==null||v.approvedBy!==null||v.approvedAtUtc!==null
      ||v.status!==(value.periodOpen?'AwaitingApproval':'PeriodWaiting'))return null
  }else if(v.status!=='Cancelled'||v.journalId!==null){
    if(!guid(v.journalId)||!same(v.journalId,id)||!guid(v.periodId)||!guid(v.approvedBy)||same(v.sourceActorId,v.approvedBy)
      ||!utc(v.approvedAtUtc)||!catalogVersion(v.version)||BigInt(v.version)<2n)return null
  }else if(v.approvedBy!==null||v.approvedAtUtc!==null)return null
  let reversal:JournalReversalProposal|null|undefined
  if(v.reversalProposal!=null){
    reversal=decodeJournalReversalProposal(v.reversalProposal,company,date)
    if(reversal===null||!int(reversal.version)||!guid(reversal.proposalId)||!guid(reversal.sourceJournalId)||!guid(reversal.periodId)
      ||!guid(reversal.proposedBy)||!utc(reversal.proposedAtUtc)||reversal.decidedBy!==null&&!guid(reversal.decidedBy)
      ||reversal.decidedAtUtc!==null&&!utc(reversal.decidedAtUtc)||reversal.reversalJournalId!==null&&!guid(reversal.reversalJournalId)
      ||!guid(v.cancellationId)||!guid(v.journalId)||!same(reversal.sourceJournalId,v.journalId)
      ||v.status!=='CancellationReversalPending'&&v.status!=='CancelledByReversal'
      ||(reversal.status==='Approved')!==(v.status==='CancelledByReversal'))return null
  }else if(v.status==='CancelledByReversal')return null
  // All values have been validated. Retain the original public raw fields, including optional omission and money text.
  return value as unknown as WorkSourceVoucherPreview
}
export function validSourceVoucherPreview(value:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>v is string,nativeAmount:(v:unknown)=>boolean):boolean {
  const h=decodeWorkSourceVoucherPreview(value.sourceVoucher,company,id,date,nativeAmount)
  if(h===null||!same(value.companyId,company)||!same(value.itemId,id)||value.kind!==SOURCE_VOUCHER_WORK_KIND||value.sourceVersion!==h.voucher.version
    ||value.pending!==(!h.sourceCancelled&&(h.voucher.status==='AwaitingApproval'||h.voucher.status==='PeriodWaiting'))
    ||value.approvalReasonRequired!==false||value.rejectionSupported!==false)return false
  const v=h.voucher,label=v.officialInvoiceNo===null||v.officialInvoiceNo.trim()===''||v.officialInvoiceNo.length>200?v.invoiceId.toLowerCase().slice(0,8):v.officialInvoiceNo
  if(value.documentLabel!==label||!Array.isArray(value.rules)||value.rules.length!==(h.periodOpen?1:2)||!rec(value.rules[0]))return false
  const rule=value.rules[0]
  if(!hasExactKeys(rule,['ruleCode','threshold','actual','deviation','unit'])||rule.ruleCode!=='accounting.source-voucher-approval-required'
    ||rule.threshold!==null||rule.deviation!==null||rule.unit!==null||rule.actual!==v.functionalPayable)return false
  if(!h.periodOpen){const waiting=value.rules[1];if(!rec(waiting)||!hasExactKeys(waiting,['ruleCode','threshold','actual','deviation','unit'])
    ||waiting.ruleCode!=='accounting.source-voucher-period-waiting'||[waiting.threshold,waiting.actual,waiting.deviation,waiting.unit].some(x=>x!==null))return false}
  if(!Array.isArray(value.references)||value.references.length!==4)return false
  const refs=[['invoice',v.invoiceId,label],['source-document',v.sourceDocumentId,v.sourceDocumentId.toLowerCase().slice(0,8)],
    ['party',v.partyId,v.partyId.toLowerCase().slice(0,8)],['currency',v.documentCurrencyId,v.documentCurrencyId.toLowerCase().slice(0,8)]] as const
  const references=value.references
  return refs.every(([role,recordId,caption],index)=>{const r=references[index];return rec(r)&&hasExactKeys(r,['role','label','recordId','link'])&&r.role===role&&same(r.recordId,recordId)&&r.label===caption&&r.link===null})
}
