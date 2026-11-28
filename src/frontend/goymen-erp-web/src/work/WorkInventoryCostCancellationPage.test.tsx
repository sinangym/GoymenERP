import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import { inventoryCostCancellationFixture as fixture } from './inventoryCostCancellationDecisionFixture'
import { WorkInventoryCostCancellationFacts } from './WorkInventoryCostCancellationFacts'
import { INVENTORY_COST_CANCELLATION_WORK_TEXT as T } from './workInventoryCostCancellationDecision'
import { WORK_DECISION_TEXT as W } from './workDecisionText'

const company='ac560000-0000-4000-8000-000000000001'
afterEach(cleanup)
// Core-only rendering. Root owns generic Work page/API lifecycle wiring; these are neither route nor browser tests.
describe('inventory cost cancellation human facts',()=>{
 it('shows the full retained original, prepared/current components, independent versions and complete legal cancellation reason read-only',()=>{
  const v=fixture(company,'Prepared','Prepared','9007199254740993','7'),h=v.inventoryCostCancellation
  render(<WorkInventoryCostCancellationFacts human={h}/>);const region=screen.getByRole('region',{name:T.title})
  const original=within(region).getByRole('region',{name:T.original}),prepared=within(region).getByRole('region',{name:T.prepared}),current=within(region).getByRole('region',{name:T.current}),source=within(region).getByRole('region',{name:T.source})
  for(const raw of ['SENTETIK INV-55','SENTETIK maliyet hazırlama gerekçesi','SENTETIK maliyet karşı hesabı','SENTETIK kayıtlı plan','1 / 3','9007199254740993','9223372036854775807'])expect(original.textContent).toContain(raw)
  for(const raw of ['SENTETIK kayıtlı maliyet paylarının iptal gerekçesi','SENTETIK üretim payı','SENTETIK sıfır alış payı','100.01','-0.01','9007199254740993','9223372036854775807']){expect(prepared.textContent).toContain(raw);expect(current.textContent).toContain(raw)}
  expect(prepared.textContent).toContain(W.inventoryCostFields.preparedRecordedAtUtc)
  expect(source.textContent).toContain(h.sourceCancellation.reason)
  for(const [key,value] of Object.entries(h.sourceCancellation)){expect(source.textContent).toContain(T.sourceFields[key as keyof typeof T.sourceFields]);if(key!=='documentKind')expect(source.textContent).toContain(value)}
  expect(source.textContent).toContain(T.salesInvoice)
  expect(within(region).queryByRole('button')).toBeNull();expect(within(region).queryByRole('textbox')).toBeNull();expect(within(region).queryByRole('combobox')).toBeNull()
  for(const raw of ['sourceProofJson','physicalProofJson','sourceFactsDigest','previewDigest','fingerprint','economicKey','approvalReceipt'])expect(region.textContent).not.toContain(raw)
 })
 it.each(['AwaitingPreparation','NoCost','AlreadyAccounted'] as const)('does not fabricate a preparation for original N1 %s',status=>{
  const h=fixture(company,'Prepared',status).inventoryCostCancellation;render(<WorkInventoryCostCancellationFacts human={h}/>)
  const original=screen.getByRole('region',{name:T.original});expect(within(original).queryByText(W.inventoryCostFields.preparation)).toBeNull()
  expect(original.textContent).toContain(h.originalProposal.proposalId);expect(original.textContent).toContain(h.originalProposal.version)
  expect(screen.getByRole('region',{name:T.prepared}).textContent).toContain(h.preparedCancellation.reason)
 })
 it('retains every component row and money profile without a 12-row truncation',()=>{
  const h=fixture(company).inventoryCostCancellation,base=h.preparedCancellation.components[0]!
  const components=Array.from({length:13},(_,index)=>({...base,postingPurpose:`SENTETIK pay ${index+1}`}))
  Object.assign(h.preparedCancellation,{components});Object.assign(h.cancellation,{components:structuredClone(components)})
  render(<WorkInventoryCostCancellationFacts human={h}/>);
  for(const label of [T.prepared,T.current]){const region=screen.getByRole('region',{name:label});for(const c of components)expect(region.textContent).toContain(c.postingPurpose)}
 })
 it('shows the genuine approved M+1 result, unchanged original N and zero acknowledgement without an invented journal',()=>{
  const h=fixture(company,'Approved','Prepared','9223372036854775806','9007199254740993').inventoryCostCancellation
  render(<WorkInventoryCostCancellationFacts human={h}/>)
  expect(screen.getByRole('region',{name:T.original}).textContent).toContain('9007199254740993')
  expect(screen.getByRole('region',{name:T.prepared}).textContent).toContain('9223372036854775806')
  const result=screen.getByRole('region',{name:T.current});expect(result.textContent).toContain('9223372036854775807');expect(result.textContent).toContain('Onaylandı')
  expect(h.cancellation.components[2]?.reversingJournalId).toBeUndefined();expect(within(result).queryByRole('button')).toBeNull()
 })
 it('keeps an explicit presentation allowlist even if a caller bypasses transport validation',()=>{
  const h=fixture(company).inventoryCostCancellation
  Object.assign(h.originalProposal.component,{sourceProofJson:'SENTETIK PRIVATE ORIGINAL',fingerprint:'SENTETIK PRIVATE HASH'})
  Object.assign(h.preparedCancellation,{approvalReceipt:'SENTETIK PRIVATE RECEIPT'})
  Object.assign(h.sourceCancellation,{requestHash:'SENTETIK PRIVATE SOURCE',correlationId:'SENTETIK PRIVATE CORRELATION'})
  render(<WorkInventoryCostCancellationFacts human={h}/>);expect(document.body.textContent).not.toContain('SENTETIK PRIVATE')
 })
})
