import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { decodeDelegationPage } from './workDelegationApi'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import { WORK_MANAGEMENT_TEXT as M } from './workManagementText'

const id='cccccccc-0000-4000-8000-0000000000f1',kind='finance.cash-account.v1',cap='finance.cash-account.work-decision'
const delegator='dddddddd-0000-4000-8000-000000000001'
const row=(n=1,otherKind=kind) => ({id:`cccccccc-0000-4000-8000-${String(n).padStart(12,'0')}`,companyId:COMPANY_A,kind:otherKind,
  approverPermission:'finance.cash-account.approve',delegatorUserId:delegator,delegateUserId:IDENTITY.userId,
  startsAtUtc:'2026-11-27T08:00:00Z',endsAtUtc:'2026-11-28T08:00:00Z',version:'1',recordedByUserId:delegator,
  recordedAtUtc:'2026-11-26T07:00:00Z',reason:'SENTETIK delegation',revokedByUserId:null,revokedAtUtc:null,revocationReason:null})
const page=(items: unknown[],current=0,hasMore=false,people: unknown[]=[]) => ({companyId:COMPANY_A,items,page:current,hasMore,people})
const preview={companyId:COMPANY_A,kind,itemId:id,documentLabel:'SENTETIK kasa',sourceVersion:'7',pending:true,approvalReasonRequired:false,references:[],rules:[]}
const clients: QueryClient[]=[]
function mount(route=`/onaylarim/${kind}/${id}`) {
  const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[route]})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});clients.push(client)
  render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>)
}
afterEach(()=>{cleanup();clients.splice(0).forEach(client=>client.clear());vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear()})

describe('authorized delegation display names (SENTETIK)',()=>{
  it('accepts exact owner-page name bindings and rejects foreign, duplicate or malformed identities',()=>{
    const value=page([row()],0,false,[{userId:delegator,displayName:'SENTETIK Ali'}])
    expect(decodeDelegationPage(value,COMPANY_A,0)?.people[0].displayName).toBe('SENTETIK Ali')
    for(const people of [[{userId:id,displayName:'foreign'}],[...value.people,...value.people],[{userId:delegator,displayName:''}],
      [{userId:delegator,displayName:'Ali',email:'unexpected'}]]) expect(decodeDelegationPage({...value,people},COMPANY_A,0)).toBeNull()
    expect(decodeDelegationPage({...value,companyId:COMPANY_B},COMPANY_A,0)).toBeNull()
    expect(decodeDelegationPage({...value,page:1},COMPANY_A,0)).toBeNull()
  })
  it.each([true,false])('shows authorized history names or explicit unavailable fallback (%s)',async(names)=>{
    installFetch({capabilities:['work.delegations.manage'],respond:url=>url.includes('/work/delegations?')
      ? json(page([row()],0,false,names?[{userId:delegator,displayName:'SENTETIK Ali'},{userId:IDENTITY.userId,displayName:'SENTETIK Bora'}]:[])):undefined})
    mount('/calisma-yonetimi')
    expect(await screen.findByText(names?'SENTETIK Ali':`${M.nameUnavailable} (${delegator})`)).toBeTruthy()
    expect(screen.getByText(names?'SENTETIK Bora':`${M.nameUnavailable} (${IDENTITY.userId})`)).toBeTruthy()
  })
})

describe('incoming delegation continuation',()=>{
  it('loads beyond the first 200 records and submits the selected later-page delegation',async()=>{
    const target=row(201)
    const calls=installFetch({capabilities:[cap],respond:(url,init)=>{
      if(url.includes('/work/delegations?')){
        const p=new URL(url,'https://example.invalid').searchParams.get('page')
        return json(p==='0'?page(Array.from({length:200},(_,i)=>row(i+1,'sales.order.v1')),0,true)
          :page([target],1,false,[{userId:delegator,displayName:'SENTETIK later delegator'}]))
      }
      if(url.includes(`/work/decisions/${kind}/${id}`)) return init?.method==='POST'
        ? json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'}) : json(preview)
      return undefined
    }})
    mount();await screen.findByText('SENTETIK kasa',{exact:false});const user=userEvent.setup()
    await user.click(screen.getByRole('checkbox',{name:T.delegation}))
    await screen.findByText(T.delegationNoMatchesYet)
    expect(screen.queryByText(T.delegationEmpty)).toBeNull()
    await user.click(screen.getByRole('button',{name:T.delegationMore}))
    await screen.findByRole('option',{name:/SENTETIK later delegator/})
    await user.selectOptions(screen.getByRole('combobox',{name:T.delegationChoice}),target.id)
    await user.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
    expect(calls.find(call=>call.method==='POST')?.body).toMatchObject({delegationId:target.id,expectedVersion:'7'})
    expect(calls.filter(call=>call.url.includes('/work/delegations?')).map(call=>new URL(call.url,'https://example.invalid').searchParams.get('page'))).toEqual(['0','1'])
  })
  it('retains the current choice across continuation failure and retries the same page',async()=>{
    let next=0
    const first=row()
    const calls=installFetch({capabilities:[cap],respond:url=>{
      if(url.includes('/work/delegations?')){
        const p=new URL(url,'https://example.invalid').searchParams.get('page')
        return p==='0'?json(page([first],0,true,[{userId:delegator,displayName:'SENTETIK Ali'}]))
          :++next===1?problem(503):json(page([row(2)],1,false))
      }
      return url.includes('/work/decisions/')?json(preview):undefined
    }})
    mount();await screen.findByText('SENTETIK kasa',{exact:false});const user=userEvent.setup()
    await user.click(screen.getByRole('checkbox',{name:T.delegation}));await screen.findByRole('option',{name:/SENTETIK Ali/})
    await user.selectOptions(screen.getByRole('combobox',{name:T.delegationChoice}),first.id)
    await user.click(screen.getByRole('button',{name:T.delegationMore}));await screen.findByRole('button',{name:'Tekrar dene'})
    expect((screen.getByRole('combobox',{name:T.delegationChoice}) as HTMLSelectElement).value).toBe(first.id)
    await user.click(screen.getByRole('button',{name:'Tekrar dene'}))
    await waitFor(()=>expect(screen.queryByRole('button',{name:T.delegationMore})).toBeNull())
    expect((screen.getByRole('combobox',{name:T.delegationChoice}) as HTMLSelectElement).value).toBe(first.id)
    expect(calls.filter(call=>call.url.includes('/work/delegations?')).map(call=>new URL(call.url,'https://example.invalid').searchParams.get('page'))).toEqual(['0','1','1'])
  })
})
