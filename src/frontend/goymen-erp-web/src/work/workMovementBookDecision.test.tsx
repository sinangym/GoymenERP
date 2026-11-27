import { afterEach,describe,expect,it,vi } from 'vitest'
import { act,cleanup,fireEvent,screen,waitFor,within } from '@testing-library/react'
import { MemoryRouter,Route,Routes } from 'react-router'
import { COMPANY_A,COMPANY_B,commitContext,renderFinance,shellFor } from '../finance/testHarness'
import { decodeCreditMovement } from '../finance/creditMovementApi'
import { decodeCashTransferView } from '../finance/cashTransferContract'
import { decodeBookValue } from '../finance/cashExchangeBookValue'
import { BOOK_VALUE_TEXT } from '../finance/cashExchangeBookValueText'
import { WorkDecisionPage } from './WorkDecisionPage'
import { decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkPackageEffectiveDate,sendWorkDecision } from './workDecisionApi'
import { NATIVE_FINANCE_WORK_CAPABILITY } from './workNativeFinanceDecision'
import { MOVEMENT_BOOK_ID as ID,ACCOUNT_ID,OTHER_ID,movementPreview,bookPreview } from './workMovementBookFixtures'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
const kinds=['finance.credit-movement.v1','finance.cash-exchange-book-value.v1'] as const
const preview=(kind:typeof kinds[number])=>kind===kinds[0]?movementPreview():bookPreview()
const request={expectedVersion:'1',approve:true,reason:null,delegationId:null}
const title=(kind:typeof kinds[number])=>kind===kinds[0]?T.creditMovementProposal:T.cashBookProposal
const response=(value:unknown,status=200,headers:Record<string,string>={})=>new Response(JSON.stringify(value),{status,headers})
function mount(kind:typeof kinds[number],caps:readonly string[]=[NATIVE_FINANCE_WORK_CAPABILITY[kind]]){
  return renderFinance(<MemoryRouter initialEntries={['/candidate/'+ID]}><Routes><Route path="candidate/:approvalId" element={<WorkDecisionPage kind={kind}/>}/></Routes></MemoryRouter>,
    {port:null,shell:{...shellFor(COMPANY_A,1,caps),sessionBinding:'SENTETIK movement book session'}})
}
afterEach(()=>{cleanup();vi.unstubAllGlobals();sessionStorage.clear()})
describe('movement and book owner human wire',()=>{
  it.each(kinds)('keeps exact complete native human projection without coercion: %s',kind=>{
    const value=preview(kind);expect(decodeWorkDecisionPreview(value,COMPANY_A,kind,ID)).toEqual(value)
    for(const patch of [{companyId:COMPANY_B},{itemId:OTHER_ID},{sourceVersion:'01'},{pending:false},{approvalReasonRequired:true},{rejectionSupported:false},{references:[]},{rules:[]},{extra:true},{expensePayment:{}}])
      expect(decodeWorkDecisionPreview({...value,...patch},COMPANY_A,kind,ID)).toBeNull()
  })
  it('reuses native decoders with explicit Work date/null seams while preserving default behavior',()=>{
    const m=movementPreview().creditMovement.movement,b=bookPreview().cashExchangeBookValue
    expect(decodeCreditMovement(m,COMPANY_A,ACCOUNT_ID)).toBeNull()
    expect(decodeCreditMovement(m,COMPANY_A,ACCOUNT_ID,isWorkPackageEffectiveDate)).toEqual(m)
    expect(decodeCashTransferView(b.transfer,COMPANY_A).ok).toBe(false)
    expect(decodeCashTransferView(b.transfer,COMPANY_A,isWorkPackageEffectiveDate).ok).toBe(true)
    expect(decodeBookValue({...b.proposal,targetFunctionalValueTl:null},COMPANY_A)).toBeNull()
    expect(decodeBookValue({...b.proposal,targetFunctionalValueTl:null},COMPANY_A,undefined,true)?.targetFunctionalValueTl).toBeNull()
    const {targetFunctionalValueTl: omitted,...legacy}=b.proposal;expect(omitted).toBe('3300')
    expect(decodeBookValue(legacy,COMPANY_A)?.targetFunctionalValueTl).toBeNull()
    expect(decodeBookValue({...b.proposal,targetFunctionalValueTl:0},COMPANY_A,undefined,true)).toBeNull()
  })
  it.each(['\n','\r','\r\n'])('refuses exact native scalar suffixes: %j',tail=>{
    const m=movementPreview(),h=m.creditMovement
    for(const movement of [{...h.movement,id:ID+tail},{...h.movement,version:'1'+tail},{...h.movement,proposedAtUtc:h.movement.proposedAtUtc+tail},
      {...h.movement,source:{...h.movement.source,components:[{...h.movement.source.components[0],amount:'1'+tail}]}},
      {...h.movement,source:{...h.movement.source,bankEffect:{...h.movement.source.bankEffect,amount:'1'+tail}}}])
      expect(decodeWorkDecisionPreview({...m,creditMovement:{...h,movement}},COMPANY_A,kinds[0],ID)).toBeNull()
    const b=bookPreview(),c=b.cashExchangeBookValue
    for(const proposal of [{...c.proposal,id:ID+tail},{...c.proposal,sourceBookValueTl:'1'+tail},{...c.proposal,transferSourceHash:'a'.repeat(64)+tail}])
      expect(decodeWorkDecisionPreview({...b,cashExchangeBookValue:{...c,proposal}},COMPANY_A,kinds[1],ID)).toBeNull()
    expect(decodeWorkDecisionPreview({...b,cashExchangeBookValue:{...c,transfer:{...c.transfer,preparation:{...c.transfer.preparation,sourceMoney:{...c.transfer.preparation.sourceMoney,amount:'1'+tail}}}}},COMPANY_A,kinds[1],ID)).toBeNull()
  })
  it('requires every source component/profile and exact rule/reference binding without rewriting money',()=>{
    const v=movementPreview(),h=v.creditMovement,m=h.movement
    for(const patch of [{sourceFactsDigest:'A'.repeat(64)},{proposedBy:''},{movement:{...m,companyId:COMPANY_B}},{movement:{...m,isOwnProposal:true}},
      {movement:{...m,source:{...m.source,components:[]}}},{movement:{...m,source:{...m.source,components:[m.source.components[0],m.source.components[0]]}}},
      {movement:{...m,moneyProfile:{...m.moneyProfile,currencyScale:29}}}])expect(decodeWorkDecisionPreview({...v,creditMovement:{...h,...patch}},COMPANY_A,kinds[0],ID)).toBeNull()
    const amount='9'.repeat(110)+'.0001'
    expect(decodeWorkDecisionPreview({...v,creditMovement:{...h,movement:{...m,source:{...m.source,components:[{kind:'principal',amount,functionalAmount:null}]}}}},COMPANY_A,kinds[0],ID)?.creditMovement?.movement.source.components[0].amount).toBe(amount)
    expect(decodeWorkDecisionPreview({...v,references:v.references.map(r=>r.role==='bank-account'?{...r,recordId:ACCOUNT_ID}:r)},COMPANY_A,kinds[0],ID)).toBeNull()
    expect(decodeWorkDecisionPreview({...v,rules:[{...v.rules[0],threshold:'1'}]},COMPANY_A,kinds[0],ID)).toBeNull()
  })
  it('keeps current corrected opening Approved3 distinct from original command Approved2',()=>{
    const v=movementPreview(),h=v.creditMovement,m=h.movement
    const current={...v,pending:false,sourceVersion:'3',references:v.references.filter(r=>r.role!=='bank-account'),rules:[{...v.rules[0],ruleCode:'finance.credit-movement-opening-balance',actual:null}],creditMovement:{...h,movement:{...m,status:'Approved',version:'3',decidedAtUtc:m.proposedAtUtc,reversedByMovementId:OTHER_ID,openingPeriodVersion:'SENTETIK period',source:{...m.source,kind:'OpeningBalance',bankEffect:null}}}}
    expect(decodeWorkDecisionPreview(current,COMPANY_A,kinds[0],ID)).toEqual(current)
    expect(decodeWorkDecisionPreview({...current,creditMovement:{...current.creditMovement,movement:{...current.creditMovement.movement,status:'Rejected',rejectionReason:'SENTETIK ret'}}},COMPANY_A,kinds[0],ID)).toBeNull()
  })
  it('refuses partial or foreign full transfer/proposal/evidence projections',()=>{
    const v=bookPreview(),h=v.cashExchangeBookValue
    for(const cashExchangeBookValue of [{...h,extra:true},{...h,transfer:{...h.transfer,companyId:COMPANY_B}},
      {...h,transfer:{...h.transfer,exchange:undefined}},{...h,transfer:{...h.transfer,transferConfirmed:false}},
      {...h,proposal:{...h.proposal,companyId:COMPANY_B}},{...h,proposal:{...h.proposal,transferId:OTHER_ID}},
      {...h,proposal:{...h.proposal,evidence:{...h.proposal.evidence,versionNo:0}}},{...h,proposal:{...h.proposal,evidence:{...h.proposal.evidence,extra:true}}}])
      expect(decodeWorkDecisionPreview({...v,cashExchangeBookValue},COMPANY_A,kinds[1],ID)).toBeNull()
    expect(decodeWorkDecisionPreview({...v,approvalSupported:false},COMPANY_A,kinds[1],ID)).toBeNull()
  })
  it.each(['omitted','null'] as const)('legacy target %s permits only reject and needs exact false support',shape=>{
    const v=bookPreview(),{targetFunctionalValueTl:unused,...p}=v.cashExchangeBookValue.proposal;expect(unused).toBe('3300')
    const value={...v,approvalSupported:false,rules:[v.rules[0],{...v.rules[1],ruleCode:'finance.cash-exchange-target-functional-value-missing',actual:null}],
      cashExchangeBookValue:{...v.cashExchangeBookValue,proposal:shape==='null'?{...p,targetFunctionalValueTl:null}:p}}
    expect(decodeWorkDecisionPreview(value,COMPANY_A,kinds[1],ID)).toEqual(value)
    for(const approvalSupported of [undefined,true,null])expect(decodeWorkDecisionPreview({...value,approvalSupported},COMPANY_A,kinds[1],ID)).toBeNull()
  })
  it.each(kinds)('strict original four-field command and native six-field receipt: %s',async kind=>{
    expect(isWorkDecisionRequest(request,kind)).toBe(true)
    expect(isWorkDecisionRequest({...request,approve:false,reason:'x'.repeat(1000)},kind)).toBe(true)
    for(const patch of [{expectedVersion:'2'},{reason:'note'},{approve:false,reason:' ret '},{approve:false,reason:'x'.repeat(1001)},{productPackageStandard:{effectiveFrom:'2026-11-27'}}])expect(isWorkDecisionRequest({...request,...patch},kind)).toBe(false)
    const answer={companyId:COMPANY_A,kind,itemId:ID,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK original'},headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}
    const fetch=vi.fn(async()=>response(answer,200,headers));vi.stubGlobal('fetch',fetch)
    expect((await sendWorkDecision(COMPANY_A,kind,ID,request,'SENTETIK key')).type).toBe('applied')
    for(const patch of [{companyId:COMPANY_B},{itemId:OTHER_ID},{kind:'finance.cash-account.v1'},{state:'Proposed'},{state:'Pending'},{state:'Rejected'},{extra:true},{originalCorrelationId:'other'}]){
      fetch.mockImplementation(async()=>response({...answer,...patch},200,headers));expect((await sendWorkDecision(COMPANY_A,kind,ID,request,'SENTETIK key')).type).toBe('unknown')}
  })
})
describe('movement and book human decision states',()=>{
  it.each(kinds.flatMap(kind=>[true,false].map(approve=>({kind,approve}))))('renders all native facts and freezes only the four-field command %j',async({kind,approve})=>{
    const posts:RequestInit[]=[];vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{
      if(init?.method==='POST'){posts.push(init);return response({companyId:COMPANY_A,kind,itemId:ID,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'SENTETIK'})}return response(preview(kind))}))
    mount(kind);const region=await screen.findByRole('region',{name:title(kind)})
    expect(within(region).getByText(kind===kinds[0]?'001250.5000':'SENTETIK full transfer description')).toBeTruthy()
    expect(within(region).getByText(kind===kinds[0]?'SENTETIK movement reason':'SENTETIK recorded book reason')).toBeTruthy()
    expect(screen.queryByText('a'.repeat(64))).toBeNull();expect(screen.queryByText('b'.repeat(64))).toBeNull()
    const reason=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement;expect(reason.maxLength).toBe(1000)
    expect(screen.getByRole('button',{name:T.reject}).matches(':disabled')).toBe(true)
    fireEvent.change(reason,{target:{value:'  SENTETIK literal ret  '}});fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
    expect(posts).toHaveLength(1);expect(JSON.parse(String(posts[0].body))).toEqual({...request,approve,reason:approve?null:'SENTETIK literal ret'})
    expect(screen.queryByRole('region',{name:title(kind)})).toBeNull()
  })
  it('shows a legacy missing TL notice and disabled approve while retaining explicit reject',async()=>{
    const v=bookPreview(),{targetFunctionalValueTl:unused,...proposal}=v.cashExchangeBookValue.proposal;expect(unused).toBe('3300')
    const value={...v,approvalSupported:false,rules:[v.rules[0],{...v.rules[1],ruleCode:'finance.cash-exchange-target-functional-value-missing',actual:null}],cashExchangeBookValue:{...v.cashExchangeBookValue,proposal}}
    let posts=0;vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{if(init?.method==='POST'){posts++;return response({companyId:COMPANY_A,kind:kinds[1],itemId:ID,state:'Rejected',replayed:false,originalCorrelationId:'SENTETIK'})}return response(value)}))
    mount(kinds[1]);await screen.findByRole('region',{name:title(kinds[1])});expect(screen.getByText(BOOK_VALUE_TEXT.targetValueMissing)).toBeTruthy()
    const approve=screen.getByRole('button',{name:T.approve});expect(approve.matches(':disabled')).toBe(true);fireEvent.click(approve);expect(posts).toBe(0)
    fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK native ret'}});fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.done);expect(posts).toBe(1)
  })
  it.each(kinds)('restores original unknown key/body without GET or private proof in session journal: %s',async kind=>{
    let gets=0;const posts:RequestInit[]=[];vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{
      if(init?.method!=='POST'){gets++;return response(preview(kind))}posts.push(init);return posts.length===1?new Response(null,{status:503}):response({companyId:COMPANY_A,kind,itemId:ID,state:'Rejected',replayed:true,originalCorrelationId:'SENTETIK original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'})}))
    mount(kind);await screen.findByRole('region',{name:title(kind)});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK frozen ret'}});fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.unknown)
    for(const secret of ['001250.5000','SENTETIK movement reason','SENTETIK recorded book reason','creditMovement','cashExchangeBookValue','sourceFactsDigest'])expect(JSON.stringify(sessionStorage)).not.toContain(secret)
    cleanup();mount(kind);await screen.findByText(T.nativeFinanceRetained);expect(gets).toBe(1);expect(screen.queryByRole('region',{name:title(kind)})).toBeNull()
    fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);expect(posts).toHaveLength(2);expect(posts[1].body).toBe(posts[0].body)
    expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
  })
  it.each(kinds)('suppresses an already inflight replacement GET after unknown: %s',async kind=>{
    let gets=0,release!:(v:Response)=>void;const late=new Promise<Response>(resolve=>{release=resolve})
    vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>init?.method==='POST'?new Response(null,{status:503}):++gets===1?response(preview(kind)):late))
    const {queryClient}=mount(kind);await screen.findByRole('region',{name:title(kind)});let refetch!:Promise<unknown>;act(()=>{refetch=queryClient.refetchQueries({queryKey:[]})})
    await waitFor(()=>expect(gets).toBe(2));fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    await act(async()=>{release(response({...preview(kind),documentLabel:'SENTETIK later current proposal'}));await refetch})
    expect(screen.queryByRole('region',{name:title(kind)})).toBeNull();expect(screen.queryByText('SENTETIK later current proposal')).toBeNull()
  })
  it.each(kinds)('missing purpose permission cannot fetch even with native browse: %s',kind=>{
    const fetch=vi.fn();vi.stubGlobal('fetch',fetch);mount(kind,['finance.credits.read','accounting.normal-journal.draft.read'])
    expect(document.querySelector('[data-state="not-found"]')).not.toBeNull();expect(fetch).not.toHaveBeenCalled()
  })
  it.each(['company','session'] as const)('current %s boundary cannot recover original unknown',async transition=>{
    const kind=kinds[0];let posts=0;vi.stubGlobal('fetch',vi.fn(async(_url:RequestInfo|URL,init?:RequestInit)=>{if(init?.method==='POST'){posts++;return new Response(null,{status:503})}return response(preview(kind))}))
    mount(kind);await screen.findByRole('region',{name:title(kind)});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    await commitContext({...shellFor(transition==='company'?COMPANY_B:COMPANY_A,2,[NATIVE_FINANCE_WORK_CAPABILITY[kind]]),sessionBinding:transition==='session'?'SENTETIK another session':'SENTETIK movement book session'})
    await waitFor(()=>expect(screen.queryByRole('button',{name:T.retry})).toBeNull());expect(posts).toBe(1)
  })
})
