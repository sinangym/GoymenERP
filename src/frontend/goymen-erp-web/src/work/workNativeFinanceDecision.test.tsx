import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { renderFinance, shellFor, COMPANY_A, COMPANY_B, commitContext } from '../finance/testHarness'
import { WorkDecisionPage } from './WorkDecisionPage'
import { decodeWorkDecisionPreview, isWorkDecisionRequest, sendWorkDecision, WORK_DECISION_KINDS } from './workDecisionApi'
import { NATIVE_FINANCE_WORK_CAPABILITY, type NativeFinanceDecisionKind } from './workNativeFinanceDecision'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
const id='de420000-0000-4000-8000-000000000001', openingId='de420000-0000-4000-8000-000000000002', account='de420000-0000-4000-8000-000000000003',
  party='de420000-0000-4000-8000-000000000004', currency='de420000-0000-4000-8000-000000000005'
const kinds=['finance.bank-credit-limit.v1','finance.opening-balance.v1','finance.opening-balance-reversal.v1'] as const
function preview(kind:NativeFinanceDecisionKind) {
  const bank=kind==='finance.bank-credit-limit.v1', reversal=kind==='finance.opening-balance-reversal.v1'
  const limit={id,companyId:COMPANY_A,lenderPartyId:party,lenderCompanyPartyAccountId:account,group:'Cash',currencyId:currency,version:'1',status:'Proposed',
    source:{creditAccountId:account,expectedCreditVersion:'2',expectedCurrentLimitId:null,expectedCurrentLimitVersion:null,amount:'0001250.5000',
      sourceDocument:{reference:'SENTETIK immutable source',documentDate:'0001-01-02',revision:'SENTETIK revision'},reason:'SENTETIK recorded limit reason'},
    isOwnProposal:false,proposedAtUtc:'0001-01-01T00:00:00Z',decidedAtUtc:null,rejectionReason:null,supersededById:null}
  const opening={id:reversal?openingId:id,companyId:COMPANY_A,targetKind:'PartyAccount',targetId:account,partyId:party,currencyId:currency,direction:'Debit',amount:'0001250.5000',
    asOfDate:'0001-01-01',status:reversal?'Approved':'Proposed',openingPeriodVersion:'SENTETIK period',isOwnProposal:reversal,
    proposedAtUtc:'0001-01-01T00:00:00Z',decidedAtUtc:reversal?'0001-01-01T00:00:00Z':null,rejectionReason:null,version:reversal?'2':'1',
    reversal:reversal?{id,status:'Proposed',reason:'SENTETIK immutable reversal reason',openingPeriodVersion:'SENTETIK reversal period',isOwnProposal:false,
      proposedAtUtc:'0001-01-01T00:00:00Z',decidedAtUtc:null,rejectionReason:null,version:'1'}:null}
  const bankCreditLimit={limit,proposedBy:'SENTETIK maker',sourceFactsDigest:'a'.repeat(64)}, openingBalance={openingBalance:opening}
  const base={companyId:COMPANY_A,kind,itemId:id,documentLabel:'SENTETIK owner caption',sourceVersion:'1',pending:true,approvalReasonRequired:false,rejectionSupported:true,
    references:(bank?[['credit-account',account],['lender-party',party],['company-account',account],['currency',currency]]:
      [...(reversal?[['opening-balance',openingId]]:[]),['company-account',account],['party',party],['currency',currency]]).map(([role,recordId])=>({role,recordId,label:'SENTETIK '+role,link:null})),
    rules:[{ruleCode:bank?'finance.bank-credit-limit-approval-required':reversal?'finance.opening-balance-reversal-approval-required':'finance.opening-balance-approval-required',threshold:null,actual:'1250.5',deviation:null,unit:null}]}
  const value:typeof base & {bankCreditLimit?:typeof bankCreditLimit;openingBalance?:typeof openingBalance}={
    ...base,...(bank?{bankCreditLimit}:{openingBalance})}
  return value
}
const input={expectedVersion:'1',approve:true,reason:null,delegationId:null}
afterEach(()=>{cleanup();vi.unstubAllGlobals();sessionStorage.clear()})
describe('three native Finance Work human contracts',()=>{
  it.each(kinds)('publishes each native purpose once with lossless owner fields: %s',kind=>{
    const value=preview(kind);expect(decodeWorkDecisionPreview(value,COMPANY_A,kind,id)).toEqual(value)
    expect(WORK_DECISION_KINDS).toHaveLength(46);expect(WORK_DECISION_KINDS.filter(k=>k===kind)).toEqual([kind])
  })
  it.each(kinds)('rejects malformed, cross-kind and foreign evidence: %s',kind=>{
    const value=preview(kind)
    for(const patch of [{companyId:COMPANY_B},{itemId:account},{sourceVersion:'2'},{pending:false},{approvalReasonRequired:true},{rejectionSupported:false},
      {references:[]},{rules:[{...value.rules[0],unit:'TRY'}]},{bankCreditLimit:undefined,openingBalance:undefined},{expensePayment:{}}])
      expect(decodeWorkDecisionPreview({...value,...patch},COMPANY_A,kind,id)).toBeNull()
    if(value.bankCreditLimit){
      const h=value.bankCreditLimit
      expect(decodeWorkDecisionPreview({...value,bankCreditLimit:{...h,limit:{...h.limit,source:{...h.limit.source,sourceDocument:{...h.limit.source.sourceDocument,documentDate:'0001-01-01'}}}}},COMPANY_A,kind,id)).toBeNull()
      for(const patch of [{extra:true},{sourceFactsDigest:'A'.repeat(64)},{proposedBy:''},{limit:{...value.bankCreditLimit.limit,companyId:COMPANY_B}},
        {limit:{...value.bankCreditLimit.limit,isOwnProposal:true}}])expect(decodeWorkDecisionPreview({...value,bankCreditLimit:{...value.bankCreditLimit,...patch}},COMPANY_A,kind,id)).toBeNull()
    }else if(value.openingBalance){
      const v=value.openingBalance.openingBalance
      for(const patch of [{id:party},{version:'01'},{companyId:COMPANY_B},{amount:'1.0\n'},{asOfDate:'0000-01-01'},{proposedAtUtc:'2026-11-27T00:00:00+03:00'}])
        expect(decodeWorkDecisionPreview({...value,openingBalance:{openingBalance:{...v,...patch}}},COMPANY_A,kind,id)).toBeNull()
    }
  })
  it.each(kinds.flatMap(kind=>['\n','\r','\r\n'].map(tail=>({kind,tail}))))('rejects native identity/time/amount suffixes without promoting human proof: %j',({kind,tail})=>{
    for(const field of ['id','version','proposedAtUtc','amount']){
      const value=preview(kind)
      if(value.bankCreditLimit){
        const v=value.bankCreditLimit.limit
        if(field==='amount')v.source.amount+=tail
        else v[field as 'id'|'version'|'proposedAtUtc']+=tail
      }else if(value.openingBalance){
        const v=value.openingBalance.openingBalance
        v[field as 'id'|'version'|'proposedAtUtc'|'amount']+=tail
      }
      expect(decodeWorkDecisionPreview(value,COMPANY_A,kind,id)).toBeNull()
    }
  })
  it('preserves native CLR opening amounts and independent BigInteger bank-limit money without conversion',()=>{
    for(const kind of ['finance.opening-balance.v1','finance.opening-balance-reversal.v1'] as const){
      const value=preview(kind),human=value.openingBalance!
      for(const amount of ['79228162514264337593543950336','9'.repeat(30),'0.'+'0'.repeat(28)+'1','1'.repeat(65)])
        expect(decodeWorkDecisionPreview({...value,openingBalance:{openingBalance:{...human.openingBalance,amount}}},COMPANY_A,kind,id)).toBeNull()
      for(const amount of ['79228162514264337593543950335','0001250.5000','0.'+'0'.repeat(27)+'1','0000000000000000000000000000000000000001.00000000000000000000000'])
        expect(decodeWorkDecisionPreview({...value,openingBalance:{openingBalance:{...human.openingBalance,amount}}},COMPANY_A,kind,id)).not.toBeNull()
    }
    const kind='finance.bank-credit-limit.v1', value=preview(kind),human=value.bankCreditLimit!
    const raw='9'.repeat(70)+'.00001'
    const result=decodeWorkDecisionPreview({...value,bankCreditLimit:{...human,limit:{...human.limit,source:{...human.limit.source,amount:raw}}}},COMPANY_A,kind,id)
    expect(result?.bankCreditLimit?.limit.source.amount).toBe(raw)
  })
  it('accepts normal opening Reversed3 without inventing a latest reversal and retained rejected A under Reversed3',()=>{
    const kind='finance.opening-balance.v1', value=preview(kind), v=value.openingBalance!.openingBalance
    expect(decodeWorkDecisionPreview({...value,pending:false,sourceVersion:'3',openingBalance:{openingBalance:{...v,status:'Reversed',version:'3',decidedAtUtc:'0001-01-01T00:00:00Z'}}},COMPANY_A,kind,id)).not.toBeNull()
    const rk='finance.opening-balance-reversal.v1', r=preview(rk), o=r.openingBalance!.openingBalance
    expect(decodeWorkDecisionPreview({...r,pending:false,sourceVersion:'2',openingBalance:{openingBalance:{...o,status:'Reversed',version:'3',reversal:{...o.reversal,status:'Rejected',version:'2',decidedAtUtc:'0001-01-01T00:00:00Z',rejectionReason:'SENTETIK old A'}}}},COMPANY_A,rk,id)).not.toBeNull()
  })
  it.each(kinds)('accepts only native original version1 and canonical approve/null or reject bound: %s',kind=>{
    const max=kind==='finance.bank-credit-limit.v1'?1000:500
    expect(isWorkDecisionRequest(input,kind)).toBe(true)
    expect(isWorkDecisionRequest({...input,approve:false,reason:'x'.repeat(max)},kind)).toBe(true)
    for(const patch of [{expectedVersion:'2'},{expectedVersion:'1\n'},{reason:'note'},{approve:false},{approve:false,reason:' ret '},{approve:false,reason:'x'.repeat(max+1)},{bankCreditLimit:{}}])
      expect(isWorkDecisionRequest({...input,...patch},kind)).toBe(false)
  })
  it.each(kinds)('trusts only the six-field original Approved/Rejected receipt, never a newer parent state: %s',async kind=>{
    const answer={companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK original'}
    const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}
    const fetch=vi.fn(async()=>new Response(JSON.stringify(answer),{status:200,headers}));vi.stubGlobal('fetch',fetch)
    expect((await sendWorkDecision(COMPANY_A,kind,id,input,'SENTETIK key')).type).toBe('applied')
    for(const patch of [{companyId:COMPANY_B},{itemId:account},{kind:'finance.cash-account.v1'},{state:'Reversed'},{state:'Proposed'},{state:'Rejected'},{extra:true},{originalCorrelationId:'other'}]){
      fetch.mockImplementation(async()=>new Response(JSON.stringify({...answer,...patch}),{status:200,headers}))
      expect((await sendWorkDecision(COMPANY_A,kind,id,input,'SENTETIK key')).type).toBe('unknown')
    }
  })
})
// Candidate component harness uses real Shell/Query/attempt state; public route publication is deliberately unchanged.
function mount(kind:NativeFinanceDecisionKind, caps:readonly string[]=[NATIVE_FINANCE_WORK_CAPABILITY[kind]]) {
  const node=<MemoryRouter initialEntries={[`/candidate/${id}`]}><Routes><Route path="candidate/:approvalId" element={<WorkDecisionPage kind={kind}/>} /></Routes></MemoryRouter>
  return renderFinance(node,{port:null,shell:{...shellFor(COMPANY_A,1,caps),sessionBinding:'SENTETIK native finance session'}})
}
const title=(kind:NativeFinanceDecisionKind)=>kind==='finance.bank-credit-limit.v1'?T.bankLimitProposal:kind==='finance.opening-balance.v1'?T.openingProposal:T.openingReversalProposal
describe('candidate native Finance Work decisions',()=>{
  it.each(kinds.flatMap(kind=>[true,false].map(approve=>({kind,approve}))))('shows full human facts and sends only native decision %j',async({kind,approve})=>{
    const posts:RequestInit[]=[]
    vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{if(init?.method==='POST'){posts.push(init);return new Response(JSON.stringify({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}),{status:200})}return new Response(JSON.stringify(preview(kind)),{status:200})}))
    mount(kind);const region=await screen.findByRole('region',{name:title(kind)})
    expect(within(region).getByText('0001250.5000')).toBeTruthy();expect(screen.queryByText('a'.repeat(64))).toBeNull()
    if(kind==='finance.bank-credit-limit.v1')expect(within(region).getByText('SENTETIK immutable source')).toBeTruthy()
    const reason=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
    expect(reason.maxLength).toBe(kind==='finance.bank-credit-limit.v1'?1000:500)
    expect(screen.getByRole('button',{name:T.reject}).matches(':disabled')).toBe(true)
    fireEvent.change(reason,{target:{value:'  SENTETIK rejection  '}})
    fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
    expect(posts).toHaveLength(1);expect(JSON.parse(String(posts[0].body))).toEqual({...input,approve,reason:approve?null:'SENTETIK rejection'})
    expect(screen.queryByRole('region',{name:title(kind)})).toBeNull()
    expect(vi.mocked(globalThis.fetch).mock.calls.every(([url])=>String(url).includes('/work/decisions/'))).toBe(true)
  })
  it.each(kinds)('restores unknown original body/key with no owner GET or private facts in journal: %s',async kind=>{
    const posts:RequestInit[]=[];let gets=0
    vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{
      if(init?.method!=='POST'){gets++;return new Response(JSON.stringify(preview(kind)),{status:200})}
      posts.push(init);return posts.length===1?new Response(null,{status:503}):new Response(JSON.stringify({companyId:COMPANY_A,kind,itemId:id,state:'Rejected',replayed:true,originalCorrelationId:'SENTETIK original'}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}})
    }))
    mount(kind);await screen.findByRole('region',{name:title(kind)});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK decision'}})
    fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.unknown)
    for(const secret of ['0001250.5000','SENTETIK immutable source','SENTETIK recorded limit reason','sourceFactsDigest','openingBalance'])expect(JSON.stringify(sessionStorage)).not.toContain(secret)
    cleanup();mount(kind);await screen.findByText(T.nativeFinanceRetained)
    expect(screen.queryByRole('region',{name:title(kind)})).toBeNull();expect(gets).toBe(1)
    fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
    expect(posts).toHaveLength(2);expect(posts[1].body).toBe(posts[0].body)
    expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
  })
  it.each(kinds)('hides an in-flight replacement human GET while the frozen command is reconciled: %s',async kind=>{
    let gets=0,release!:(value:Response)=>void
    const late=new Promise<Response>(resolve=>{release=resolve})
    vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{
      if(init?.method==='POST')return new Response(null,{status:503})
      return ++gets===1?new Response(JSON.stringify(preview(kind)),{status:200}):late
    }))
    const {queryClient}=mount(kind);await screen.findByRole('region',{name:title(kind)})
    let refetch!:Promise<unknown>;act(()=>{refetch=queryClient.refetchQueries({queryKey:[]})})
    await waitFor(()=>expect(gets).toBe(2));fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    const newer=preview(kind);newer.documentLabel='SENTETIK replacement caption'
    await act(async()=>{release(new Response(JSON.stringify(newer),{status:200}));await refetch})
    expect(screen.queryByRole('region',{name:title(kind)})).toBeNull();expect(screen.queryByText('SENTETIK replacement caption')).toBeNull()
  })
  it.each(['company','session'] as const)('does not expose or retry a retained decision in another %s',async transition=>{
    const kind='finance.bank-credit-limit.v1',posts:RequestInit[]=[]
    vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{
      if(init?.method==='POST'){posts.push(init);return new Response(null,{status:503})}
      return new Response(JSON.stringify(preview(kind)),{status:200})
    }))
    const view=mount(kind);await screen.findByRole('region',{name:title(kind)});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    const next={...shellFor(transition==='company'?COMPANY_B:COMPANY_A,2,[NATIVE_FINANCE_WORK_CAPABILITY[kind]]),
      sessionBinding:transition==='session'?'SENTETIK another native session':'SENTETIK native finance session'}
    await commitContext(next)
    await waitFor(()=>expect(screen.queryByRole('button',{name:T.retry})).toBeNull());expect(posts).toHaveLength(1)
    view.unmount()
  })
  it.each(kinds)('does not fetch or recover under a missing purpose capability: %s',async kind=>{
    const fetch=vi.fn();vi.stubGlobal('fetch',fetch);mount(kind,['finance.credits.read','finance.opening-balances.read'])
    expect(document.querySelector('[data-state="not-found"]')).not.toBeNull();expect(fetch).not.toHaveBeenCalled()
  })
})
