import { afterEach,expect,it,vi } from 'vitest'
import { act,cleanup,fireEvent,render,screen,waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient,QueryObserver } from '@tanstack/react-query'
import { createMemoryRouter,RouterProvider } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem } from '../settings/testHarness'
import { AiFeedbackPanel } from './AiFeedbackPanel'
import { AI_PANEL_CAPABILITY } from './aiCapabilities'
import { AI_FEEDBACK_TEXT as T } from './aiFeedbackText'
const id='cccccccc-0000-4000-8000-000000000001',path=`/ai/answers/${id}/feedback`
const view={answerId:id,version:'0',kind:null,reasonCode:null}
const receipt={answerId:id,version:'1',kind:'Accepted',reasonCode:null,originalCorrelationId:'original',replayed:false}
function mount(company=COMPANY_A,answerId=id,client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:'feedback-test',element:<AiFeedbackPanel companyId={company} answerId={answerId}/>},{path:'feedback-away',element:<p>SENTETIK başka ekran</p>}]}],{initialEntries:['/feedback-test']})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>)
 return router
}
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
it('requires a fixed reason outside Accepted and offers revert only for the latest Accepted state',async()=>{
 installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:url=>url.includes(path)?json(view):undefined});mount()
 const select=await screen.findByLabelText(T.kind),save=screen.getByRole('button',{name:T.save})
 expect((save as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(select,{target:{value:'Corrected'}});expect((save as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'incorrect_information'}});expect((save as HTMLButtonElement).disabled).toBe(false)
 expect((screen.getByRole('option',{name:T.kinds.Reverted}) as HTMLOptionElement).disabled).toBe(true)
 expect(screen.getByText(T.revertHelp)).toBeTruthy();expect(screen.queryByRole('textbox')).toBeNull()
})
it('records explicit user feedback using only the three native fields',async()=>{
 const calls=installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:(url,init)=>url.includes(path)?init?.method==='POST'?json(receipt):json(view):undefined});mount()
 await screen.findByLabelText(T.kind);await userEvent.setup().selectOptions(screen.getByLabelText(T.kind),'Accepted');await userEvent.setup().click(screen.getByRole('button',{name:T.save}))
 await screen.findByText(T.done,{exact:false})
 expect(calls.filter(call=>call.url.includes(path)&&call.method==='POST').map(call=>call.body)).toEqual([{expectedVersion:'0',kind:'Accepted',reasonCode:null}])
})
it('replays the exact unknown body/key after remount despite a newer read and keeps its original receipt version',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-feedback-session'});let posts=0
 const calls=installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:(url,init)=>{
  if(!url.includes(path))return undefined
  if(init?.method==='POST'){posts++;return posts===1?problem(503):json({...receipt,replayed:true},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})}
  return json(posts===0?view:{answerId:id,version:'4',kind:'Rejected',reasonCode:'other'})
 }})
 mount();await screen.findByLabelText(T.kind);fireEvent.change(screen.getByLabelText(T.kind),{target:{value:'Accepted'}});fireEvent.click(screen.getByRole('button',{name:T.save}));await screen.findByText(T.unknown)
 cleanup();mount();await screen.findByText(T.unknown);await screen.findByText(`${T.current}: ${T.kinds.Rejected} · ${T.version}: 4`)
 expect((screen.getByLabelText(T.kind) as HTMLSelectElement).value).toBe('Accepted');expect((screen.getByRole('button',{name:T.save}) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(`${T.done} ${T.receipt}: 1`)
 const sent=calls.filter(call=>call.url.includes(path)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(Object.keys(sent[0]!.body as object).sort()).toEqual(['expectedVersion','kind','reasonCode'])
})
it.each([{company:COMPANY_B,answer:id,capabilities:[AI_PANEL_CAPABILITY]},{company:COMPANY_A,answer:id+'\n',capabilities:[AI_PANEL_CAPABILITY]},{company:COMPANY_A,answer:id,capabilities:[]}])('does not fetch feedback outside actual company/answer/panel authority %j',async({company,answer,capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(path)?json(view):undefined});mount(company,answer)
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true));expect(calls.some(call=>call.url.includes('/ai/answers/'))).toBe(false)
})
it('drops a pending feedback UI across a new authentication session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-feedback-old'})
 const calls=installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):json(view):undefined})
 mount();await screen.findByLabelText(T.kind);fireEvent.change(screen.getByLabelText(T.kind),{target:{value:'Accepted'}});fireEvent.click(screen.getByRole('button',{name:T.save}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-feedback-new'});mount();await screen.findByLabelText(T.kind)
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(path)&&call.method==='POST')).toHaveLength(1)
})
it('does not apply a foreign receipt and keeps the original request available for retry',async()=>{
 installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:(url,init)=>url.includes(path)?init?.method==='POST'?json({...receipt,answerId:COMPANY_B}):json(view):undefined});mount()
 await screen.findByLabelText(T.kind);fireEvent.change(screen.getByLabelText(T.kind),{target:{value:'Accepted'}});fireEvent.click(screen.getByRole('button',{name:T.save}));await screen.findByText(T.unknown)
 expect(screen.queryByText(T.done,{exact:false})).toBeNull();expect(screen.getByRole('button',{name:T.retry})).toBeTruthy()
})

it('allows an explicitly reasoned revert only while the latest owner state is Accepted',async()=>{
 const calls=installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:(url,init)=>url.includes(path)?init?.method==='POST'?json({...receipt,version:'2',kind:'Reverted',reasonCode:'other'}):json({...view,version:'1',kind:'Accepted'}):undefined});mount()
 await screen.findByLabelText(T.kind);expect((screen.getByRole('option',{name:T.kinds.Reverted}) as HTMLOptionElement).disabled).toBe(false)
 fireEvent.change(screen.getByLabelText(T.kind),{target:{value:'Reverted'}});expect((screen.getByRole('button',{name:T.save}) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'other'}});fireEvent.click(screen.getByRole('button',{name:T.save}));await screen.findByText(T.done,{exact:false})
 expect(calls.filter(call=>call.url.includes(path)&&call.method==='POST')[0]!.body).toEqual({expectedVersion:'1',kind:'Reverted',reasonCode:'other'})
})
it('clears the feedback surface after a fresh 403 authority loss',async()=>{
 const capabilities=[AI_PANEL_CAPABILITY]
 const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(path))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(view)}});mount()
 await screen.findByLabelText(T.kind);fireEvent.change(screen.getByLabelText(T.kind),{target:{value:'Accepted'}});fireEvent.click(screen.getByRole('button',{name:T.save}))
 await waitFor(()=>expect(screen.queryByRole('region',{name:T.heading})).toBeNull());expect(calls.filter(call=>call.url.includes(path)&&call.method==='POST')).toHaveLength(1)
})
it('does not release a late receipt into another committed company',async()=>{
 let release!:(response:Response)=>void
 installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:(url,init)=>url.includes(path)?init?.method==='POST'?new Promise<Response>(resolve=>{release=resolve}):json(view):undefined});mount()
 await screen.findByLabelText(T.kind);fireEvent.change(screen.getByLabelText(T.kind),{target:{value:'Accepted'}});fireEvent.click(screen.getByRole('button',{name:T.save}))
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await waitFor(()=>expect(screen.queryByRole('region',{name:T.heading})).toBeNull())
 await act(async()=>{release(json(receipt));await Promise.resolve()});expect(screen.queryByText(T.done,{exact:false})).toBeNull()
})

it('starts a fresh generation read on route return even while another observer retains the old in-flight feedback query',async()=>{
 let releaseOld!:(response:Response)=>void,reads=0
 const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0}}})
 const calls=installFetch({capabilities:[AI_PANEL_CAPABILITY],respond:(url,init)=>{
  if(!url.includes(path)||init?.method==='POST')return undefined
  return ++reads===1?new Promise<Response>(resolve=>{releaseOld=resolve}):json({answerId:id,version:'2',kind:'Rejected',reasonCode:'other'})
 }})
 const router=mount(COMPANY_A,id,client)
 await waitFor(()=>expect(reads).toBe(1))
 const oldQuery=client.getQueryCache().findAll().find(query=>query.queryKey.includes('ai-answer-feedback'))
 expect(oldQuery).toBeDefined()
 // A second consumer can retain a query through unmount. Cancellation/gcTime alone is not an entry fence.
 const observer=new QueryObserver(client,{queryKey:oldQuery!.queryKey,enabled:false})
 const stop=observer.subscribe(()=>{})
 try{
  await act(async()=>{await router.navigate('/feedback-away')})
  await screen.findByText('SENTETIK başka ekran')
  await act(async()=>{await router.navigate('/feedback-test')})
  await screen.findByText(`${T.current}: ${T.kinds.Rejected} · ${T.version}: 2`)
  expect(reads).toBe(2)
  await act(async()=>{releaseOld(json({...view,version:'1',kind:'Accepted'}));await Promise.resolve()})
  expect(screen.getByText(`${T.current}: ${T.kinds.Rejected} · ${T.version}: 2`)).toBeTruthy()
  expect(screen.queryByText(`${T.current}: ${T.kinds.Accepted} · ${T.version}: 1`)).toBeNull()
  expect(calls.filter(call=>call.url.includes(path)&&call.method==='POST')).toHaveLength(0)
 }finally{stop();observer.destroy()}
})
