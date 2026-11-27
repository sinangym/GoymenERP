import { afterEach,expect,it,vi } from 'vitest'
import { act,cleanup,fireEvent,render,screen,waitFor,within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient,QueryObserver } from '@tanstack/react-query'
import { createMemoryRouter,RouterProvider } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem } from '../settings/testHarness'
import { WorkMorningSummary } from './WorkMorningSummary'
import { MORNING_SUMMARY_TEXT as T,MORNING_KIND_TEXT } from './workMorningSummaryText'
const cap='finance.cash-account.work-decision',path='/work/morning-summary',digest='cccccccc-0000-4000-8000-000000000001'
const value={companyId:COMPANY_A,digestId:digest,slotUtc:'2026-11-27T06:00:00Z',scheduleVersion:'SENTETIK-settings-v1',counts:[{kind:'task-assigned',count:7},{kind:'allocation-released',count:3}]}
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:'morning-test',element:<CapabilityRoute capability={[cap]}><WorkMorningSummary/></CapabilityRoute>},{path:'morning-away',element:<p>SENTETIK başka ekran</p>}]}],{initialEntries:['/morning-test']})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return router
}
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
it('shows only the released own digest source and raw per-kind counts with no AI, total or mutation',async()=>{
 const calls=installFetch({capabilities:[cap],respond:url=>url.endsWith(path)?json(value):undefined});mount()
 const region=await screen.findByRole('region',{name:T.heading});await within(region).findByText(value.scheduleVersion)
 expect(within(region).getByText(value.slotUtc)).toBeTruthy();expect(within(region).getByText(digest)).toBeTruthy()
 expect(within(region).getByRole('rowheader',{name:MORNING_KIND_TEXT['task-assigned']})).toBeTruthy()
 expect(within(region).getAllByRole('cell').map(cell=>cell.textContent)).toEqual(['7','3'])
 expect(calls.filter(call=>call.url.endsWith(path))).toHaveLength(1);expect(calls.find(call=>call.url.endsWith(path))!.headers['X-Company-Id']).toBe(COMPANY_A)
 expect(calls.some(call=>call.url.includes('/ai/')||call.method==='POST')).toBe(false);expect(JSON.stringify(sessionStorage)).not.toContain(digest)
})
it('keeps 204 separate from a counted empty or zero summary',async()=>{
 installFetch({capabilities:[cap],respond:url=>url.endsWith(path)?new Response(null,{status:204}):undefined});mount()
 await screen.findByText(T.empty);expect(screen.queryByRole('table')).toBeNull();expect(screen.queryByText(digest)).toBeNull()
})
it('shows 503 as unavailable without partial counts, then rereads the same native endpoint explicitly',async()=>{
 let reads=0;const calls=installFetch({capabilities:[cap],respond:url=>url.endsWith(path)?++reads===1?problem(503):json(value):undefined});mount()
 await screen.findByText(T.unavailable);expect(screen.queryByRole('table')).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(value.scheduleVersion)
 expect(reads).toBe(2);expect(calls.some(call=>call.method==='POST')).toBe(false)
})
it.each([{companyId:COMPANY_B},{counts:[{kind:'future',count:1}]},{counts:[{kind:'task-assigned',count:-1}]},{counts:[{kind:'task-assigned',count:1},{kind:'task-assigned',count:2}]}])('does not paint partial source data when the response fails the contract %#',async delta=>{
 installFetch({capabilities:[cap],respond:url=>url.endsWith(path)?json({...value,...delta}):undefined});mount()
 await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('table')).toBeNull();expect(screen.queryByText(digest)).toBeNull()
})
it('closes on a fresh 403 and does not preserve the previous authorized counts',async()=>{
 const caps=[cap];let reads=0
 installFetch({capabilities:caps,respond:url=>{if(!url.endsWith(path))return undefined;if(++reads===1)return json(value);caps.splice(0);return problem(403)}});mount()
 await screen.findByText(value.scheduleVersion);fireEvent.click(screen.getByRole('button',{name:T.retry}))
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('table')).toBeNull();expect(screen.queryByText(digest)).toBeNull()
})
it('does not read without the existing host route capability',async()=>{
 const calls=installFetch({capabilities:[],respond:url=>url.endsWith(path)?json(value):undefined});mount()
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.endsWith(path))).toBe(false)
})
it('does not release a late old-company digest into the newly committed company',async()=>{
 let release!:(response:Response)=>void
 const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.endsWith(path)?new Headers(init?.headers).get('X-Company-Id')===COMPANY_A?new Promise<Response>(resolve=>{release=resolve}):json({...value,companyId:COMPANY_B,scheduleVersion:'SENTETIK-company-B',counts:[{kind:'task-assigned',count:9}]}):undefined});mount()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith(path))).toBe(true));await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await screen.findByText('SENTETIK-company-B');await act(async()=>{release(json(value));await Promise.resolve()})
 expect(screen.queryByText(value.scheduleVersion)).toBeNull();expect(screen.getAllByRole('cell').map(cell=>cell.textContent)).toEqual(['9'])
})
it('starts a new authenticated read instead of showing an old-session late response',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-morning-old'});let release!:(response:Response)=>void,reads=0
 const calls=installFetch({capabilities:[cap],respond:url=>url.endsWith(path)?++reads===1?new Promise<Response>(resolve=>{release=resolve}):json({...value,scheduleVersion:'SENTETIK-new-session'}):undefined});mount()
 await waitFor(()=>expect(reads).toBe(1));cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-morning-new'});mount()
 await screen.findByText('SENTETIK-new-session');await act(async()=>{release(json(value));await Promise.resolve()})
 expect(screen.queryByText(value.scheduleVersion)).toBeNull();expect(calls.filter(call=>call.url.endsWith(path))).toHaveLength(2)
})
it('uses a new route generation even while another observer retains the previous in-flight query',async()=>{
 let release!:(response:Response)=>void,reads=0
 const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0}}})
 installFetch({capabilities:[cap],respond:url=>url.endsWith(path)?++reads===1?new Promise<Response>(resolve=>{release=resolve}):json({...value,scheduleVersion:'SENTETIK-new-entry'}):undefined})
 const router=mount(client);await waitFor(()=>expect(reads).toBe(1));const old=client.getQueryCache().findAll().find(q=>q.queryKey.includes('work-morning-summary'))
 expect(old).toBeDefined();const observer=new QueryObserver(client,{queryKey:old!.queryKey,enabled:false}),stop=observer.subscribe(()=>{})
 try{await act(async()=>{await router.navigate('/morning-away')});await screen.findByText('SENTETIK başka ekran');await act(async()=>{await router.navigate('/morning-test')})
  await screen.findByText('SENTETIK-new-entry');expect(reads).toBe(2);await act(async()=>{release(json(value));await Promise.resolve()});expect(screen.queryByText(value.scheduleVersion)).toBeNull()
 }finally{stop();observer.destroy()}
})
