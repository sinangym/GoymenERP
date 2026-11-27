import { useState } from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { AppShell } from '../shell/AppShell'
import { ShellProvider } from '../shell/ShellProvider'
import { CAPABILITY } from '../shell/capabilities'
import type { CounterpartyMutationResult } from './mutationContract'
import { TAX_IDENTITY_TEXT as TAX } from './taxIdentity'
import { resetPendingTaxIdentityAttempts } from './pendingTaxIdentityAttempt'
import { CariQuickCreate } from './CariQuickCreate'
import { findQuickCreatedOffer } from './quickCreateSelection'
import { QUICK_CARI_TEXT as T } from './quickCreateText'
import { A_ID, B_ID, PARTY, PARTY_2, ALL_CARI_CAPABILITIES, installFetch, json, noContent, LEGAL_NAME, mutationView, problem } from './commandPageHarness'

function mount(onCreated: (record: CounterpartyMutationResult, signal: AbortSignal) => Promise<boolean> = vi.fn(async () => true)) {
  function Form() {
    const [note, setNote] = useState(''), [sent, setSent] = useState(false), [unresolved, setUnresolved] = useState(false)
    return <form onSubmit={e => { e.preventDefault(); if (!unresolved) setSent(true) }}>
      <label>İşlem açıklaması<input value={note} onChange={e => setNote(e.target.value)} /></label>
      <button type="submit" disabled={unresolved}>Ana işlemi gönder</button>
      <CariQuickCreate onUnresolvedChange={setUnresolved} initialRoles={1} onCreated={onCreated} />
      {sent ? <p>Ana işlem gönderildi</p> : null}
    </form>
  }
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: [{ path: 'quick', element: <Form /> }] }], { initialEntries: ['/quick'] })
  render(<ShellProvider queryClient={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}><RouterProvider router={router} /></ShellProvider>)
  return { router, onCreated }
}
async function openAndFill() {
  await userEvent.click(await screen.findByRole('button', { name: T.open }))
  const dialog = screen.getByRole('dialog')
  await userEvent.selectOptions(within(dialog).getByLabelText('Taraf türü'), '1')
  await userEvent.type(within(dialog).getByLabelText('Hukuki ad'), LEGAL_NAME)
  expect((within(dialog).getByRole('checkbox', { name: 'Müşteri' }) as HTMLInputElement).checked).toBe(true)
  await waitFor(() => expect((within(dialog).getByRole('button', { name: 'Cari oluştur' }) as HTMLButtonElement).disabled).toBe(false))
  return dialog
}
afterEach(() => { cleanup(); resetPendingTaxIdentityAttempts(); localStorage.clear(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

it('creates through the native form without navigating or submitting/clearing the filled caller form', async () => {
  const log = installFetch({ create: () => json(mutationView(), 201) })
  const { router, onCreated } = mount()
  await screen.findByRole('button', { name: T.open })
  await userEvent.type(screen.getByLabelText('İşlem açıklaması'), 'Korunan açıklama')
  const dialog = await openAndFill()
  await userEvent.click(within(dialog).getByRole('button', { name: 'Cari oluştur' }))
  await screen.findByText(T.selected)
  expect(onCreated).toHaveBeenCalledTimes(1)
  expect(log.creates()).toHaveLength(1)
  expect(log.creates()[0].body).toMatchObject({ roles: 1 })
  expect(log.links()).toHaveLength(0)
  expect(router.state.location.pathname).toBe('/quick')
  expect(screen.queryByText('Ana işlem gönderildi')).toBeNull()
  await userEvent.click(within(dialog).getByRole('button', { name: T.close }))
  expect((screen.getByLabelText('İşlem açıklaması') as HTMLInputElement).value).toBe('Korunan açıklama')
})
it('without create authority exposes no active-card form, probe or pretend request', async () => {
  const log = installFetch({ capabilities: ALL_CARI_CAPABILITIES.filter(c => c !== CAPABILITY.cariPartiesCreate) })
  mount()
  await screen.findByText(T.denied)
  expect(screen.queryByRole('button', { name: T.open })).toBeNull()
  expect(log.calls.filter(c => c.url.includes('/counterparties'))).toHaveLength(0)
})
it('hiding and reopening an unknown native attempt retains its frozen fields, exact key and body', async () => {
  let attempt = 0
  const log = installFetch({ create: () => { if (++attempt === 1) throw new TypeError('offline'); return json(mutationView(), 201, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'original' }) } })
  mount()
  const dialog = await openAndFill()
  await userEvent.click(within(dialog).getByRole('button', { name: 'Cari oluştur' }))
  await within(dialog).findByText(/İşlem sonucu belirsiz/)
  await userEvent.click(within(dialog).getByRole('button', { name: T.close }))
  await userEvent.click(screen.getByRole('button', { name: T.resume }))
  expect((within(dialog).getByLabelText('Hukuki ad') as HTMLInputElement).value).toBe(LEGAL_NAME)
  expect((within(dialog).getByRole('button', { name: 'Cari oluştur' }) as HTMLButtonElement).disabled).toBe(true)
  expect(log.creates()).toHaveLength(1)
  expect((screen.getByRole('button', { name: 'Ana işlemi gönder' }) as HTMLButtonElement).disabled).toBe(true)
  await userEvent.click(within(dialog).getByRole('button', { name: 'Aynı işlemi yeniden dene' }))
  await screen.findByText(T.selected)
  expect(log.creates()).toHaveLength(2)
  expect(log.creates()[1].idempotencyKey).toBe(log.creates()[0].idempotencyKey)
  expect(log.creates()[1].body).toEqual(log.creates()[0].body)
})
it('an unavailable owner eligibility read preserves the caller and reports the unselected created card', async () => {
  installFetch({ create: () => json(mutationView(), 201) })
  mount(vi.fn(async () => false))
  const dialog = await openAndFill()
  await userEvent.click(within(dialog).getByRole('button', { name: 'Cari oluştur' }))
  await screen.findByText(T.notEligible)
  expect(screen.queryByText(T.selected)).toBeNull()
})
it('a company switch aborts a late eligibility result and remounts a fresh inline form', async () => {
  let resolve!: (value: boolean) => void
  const promise = new Promise<boolean>(done => { resolve = done }), signals: AbortSignal[] = []
  installFetch({ create: () => json(mutationView(), 201) })
  mount(vi.fn((_record: CounterpartyMutationResult, signal: AbortSignal) => { signals.push(signal); return promise }))
  const dialog = await openAndFill()
  await userEvent.click(within(dialog).getByRole('button', { name: 'Cari oluştur' }))
  await screen.findByText(T.checking)
  await userEvent.click(within(dialog).getByRole('button', { name: T.close }))
  await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)
  await waitFor(() => expect(signals[0].aborted).toBe(true))
  await act(async () => resolve(true))
  expect(screen.queryByText(T.selected)).toBeNull()
  await userEvent.click(await screen.findByRole('button', { name: T.open }))
  expect((screen.getByLabelText('Hukuki ad') as HTMLInputElement).value).toBe('')
})
it('the owner offer walk follows cursors and fails closed on aborted, unavailable or repeated pages', async () => {
  const controller = new AbortController()
  const read = vi.fn().mockResolvedValueOnce({ items: ['other'], nextCursor: 'page2' }).mockResolvedValueOnce({ items: ['eligible'], nextCursor: null })
  expect(await findQuickCreatedOffer(controller.signal, read, item => item === 'eligible')).toBe('eligible')
  expect(read.mock.calls).toEqual([[undefined], ['page2']])
  expect(await findQuickCreatedOffer(controller.signal, async () => null, () => true)).toBeNull()
  const loop = vi.fn(async () => ({ items: [], nextCursor: 'again' }))
  expect(await findQuickCreatedOffer(controller.signal, loop, () => false)).toBeNull()
  expect(loop).toHaveBeenCalledTimes(2)
  controller.abort()
  const unused = vi.fn()
  expect(await findQuickCreatedOffer(controller.signal, unused, () => true)).toBeNull()
  expect(unused).not.toHaveBeenCalled()
})

it('a fresh native 403 revokes the create entry and never announces or selects a card', async () => {
  let revoked = false
  const log = installFetch({ capabilities: () => revoked ? [] : ALL_CARI_CAPABILITIES, create: () => { revoked = true; return problem(403, 'forbidden') } })
  const { onCreated } = mount()
  const dialog = await openAndFill()
  await userEvent.click(within(dialog).getByRole('button', { name: 'Cari oluştur' }))
  await screen.findByText(T.denied)
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Cari oluştur' })).toBeNull())
  expect(log.creates()).toHaveLength(1)
  expect(onCreated).not.toHaveBeenCalled()
  expect(screen.queryByText(T.selected)).toBeNull()
})


function taxView(over: Record<string,unknown>={}) {
 return {partyId:PARTY,companyId:A_ID,version:'1',taxIdentityNumber:null,kind:null,duplicates:[],pendingChange:null,lastDecidedChange:null,
  viewer:{canUpdate:true,canApprove:false,isRequesterOfPending:false},...over}
}
const emptyPage=()=>json({items:[],nextCursor:null,hasMore:false,totalCount:null,countScope:null})
async function createThenOpenTax() {
 const dialog=await openAndFill()
 await userEvent.click(within(dialog).getByRole('button',{name:'Cari oluştur'}));await screen.findByText(T.selected)
 await userEvent.click(within(dialog).getByRole('button',{name:T.taxOpen}))
 return dialog
}

it('opens the existing tax panel only on request and saves duplicate-warning identity as a separate native command before rechecking the owner',async()=>{
 let stored=false
 const duplicate={partyId:PARTY_2,code:'CAR-000002',legalName:'SENTETIK Sube',accountIsActive:true}
 const log=installFetch({create:()=>json(mutationView(),201),counterparties:call=>{
  if(call.url.endsWith('/tax-identity/duplicates'))return json({taxIdentityNumber:'1234567890',kind:'vkn',duplicates:[duplicate]})
  if(call.url.endsWith('/tax-identity')) {
   if(call.method==='PUT'){stored=true;return noContent()}
   return json(taxView(stored?{version:'2',taxIdentityNumber:'1234567890',kind:'vkn',duplicates:[duplicate]}:{}))
  }
  return emptyPage()
 }})
 const onCreated=vi.fn(async()=>true);mount(onCreated)
 await userEvent.type(await screen.findByLabelText('İşlem açıklaması'),'Korunan işlem')
 const dialog=await openAndFill();await userEvent.click(within(dialog).getByRole('button',{name:'Cari oluştur'}));await screen.findByText(T.selected)
 expect(log.calls.filter(c=>c.url.includes('/tax-identity'))).toHaveLength(0)
 expect(onCreated).toHaveBeenCalledTimes(1)
 await userEvent.click(within(dialog).getByRole('button',{name:T.taxOpen}));await within(dialog).findByText(TAX.none)
 expect(within(dialog).getByText(T.taxSeparate)).toBeTruthy()
 await userEvent.type(within(dialog).getByLabelText(TAX.inputLabel),'1234567891')
 await userEvent.click(within(dialog).getByRole('button',{name:TAX.save}));await within(dialog).findByText(TAX.invalidVkn)
 expect(log.calls.filter(c=>c.url.endsWith('/duplicates'))).toHaveLength(0)
 await userEvent.clear(within(dialog).getByLabelText(TAX.inputLabel));await userEvent.type(within(dialog).getByLabelText(TAX.inputLabel),'1234567890')
 await userEvent.click(within(dialog).getByRole('button',{name:TAX.save}));await within(dialog).findByText(TAX.duplicateTitle)
 expect(log.calls.filter(c=>c.method==='PUT')).toHaveLength(0)
 await userEvent.click(within(dialog).getByRole('button',{name:TAX.saveAnyway}));await within(dialog).findByText(TAX.saved)
 await waitFor(()=>expect(onCreated).toHaveBeenCalledTimes(2))
 expect(log.creates()).toHaveLength(1);expect(log.links()).toHaveLength(0)
 const sent=log.calls.filter(c=>c.method==='PUT');expect(sent).toHaveLength(1)
 expect(sent[0].url).toBe(`/api/v1/counterparties/${PARTY}/tax-identity`)
 expect(sent[0].body).toEqual({taxIdentityNumber:'1234567890',expectedVersion:'1'})
 expect(sent[0].companyId).toBe(A_ID);expect(sent[0].idempotencyKey).toBeTruthy()
 expect(sent[0].idempotencyKey).not.toBe(log.creates()[0].idempotencyKey)
 expect(log.creates()[0].body).not.toHaveProperty('taxIdentityNumber')
 await waitFor(()=>expect(within(dialog).getByText('1234567890')).toBeTruthy())
 expect((screen.getByLabelText('İşlem açıklaması') as HTMLInputElement).value).toBe('Korunan işlem')
 expect(screen.queryByText('Ana işlem gönderildi')).toBeNull()
})

it('create-only authority shows the missing tax-read explanation without fetching or inventing an entry form',async()=>{
 const log=installFetch({capabilities:ALL_CARI_CAPABILITIES.filter(c=>c!==CAPABILITY.cariPartiesDetail),create:()=>json(mutationView(),201)})
 mount();const dialog=await openAndFill();await userEvent.click(within(dialog).getByRole('button',{name:'Cari oluştur'}));await screen.findByText(T.selected)
 expect(within(dialog).getByText(T.taxReadUnavailable)).toBeTruthy()
 expect(within(dialog).queryByRole('button',{name:T.taxOpen})).toBeNull()
 expect(log.calls.filter(c=>c.url.includes('/tax-identity'))).toHaveLength(0)
})

it('uses fresh owner viewer authority for tax update and keeps a read-only missing-information note',async()=>{
 const log=installFetch({create:()=>json(mutationView(),201),counterparties:call=>call.url.endsWith('/tax-identity')
  ?json(taxView({viewer:{canUpdate:false,canApprove:false,isRequesterOfPending:false}})):emptyPage()})
 mount();const dialog=await createThenOpenTax();await within(dialog).findByText(T.taxUpdateUnavailable)
 expect(within(dialog).queryByLabelText(TAX.inputLabel)).toBeNull()
 expect(log.calls.filter(c=>c.method==='PUT')).toHaveLength(0)
})

it('retains an unknown tax attempt while hiding the modal, blocks caller submission/reselection and retries the same native key/body',async()=>{
 let sends=0
 const log=installFetch({create:()=>json(mutationView(),201),counterparties:call=>{
  if(call.url.endsWith('/duplicates'))return json({taxIdentityNumber:'1234567890',kind:'vkn',duplicates:[]})
  if(call.url.endsWith('/tax-identity')) {
   if(call.method==='PUT') {if(++sends===1)throw new TypeError('offline');return noContent({'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})}
   return json(taxView(sends?{version:'2',taxIdentityNumber:'1234567890',kind:'vkn'}:{}))
  }
  return emptyPage()
 }})
 const onCreated=vi.fn(async()=>true);mount(onCreated)
 const dialog=await createThenOpenTax();await within(dialog).findByText(TAX.none)
 await userEvent.type(within(dialog).getByLabelText(TAX.inputLabel),'1234567890')
 await userEvent.click(within(dialog).getByRole('button',{name:TAX.save}));await within(dialog).findByText(TAX.unknownOutcome)
 expect((screen.getByRole('button',{name:'Ana işlemi gönder'}) as HTMLButtonElement).disabled).toBe(true)
 expect((within(dialog).getByRole('button',{name:T.recheck}) as HTMLButtonElement).disabled).toBe(true)
 await userEvent.click(within(dialog).getByRole('button',{name:T.close}))
 expect((screen.getByRole('button',{name:'Ana işlemi gönder'}) as HTMLButtonElement).disabled).toBe(true)
 await userEvent.click(screen.getByRole('button',{name:T.resume}));await within(dialog).findByText(TAX.unknownOutcome)
 expect(onCreated).toHaveBeenCalledTimes(1)
 await userEvent.click(within(dialog).getByRole('button',{name:TAX.retrySame}));await within(dialog).findByText(TAX.saved)
 await waitFor(()=>expect(onCreated).toHaveBeenCalledTimes(2))
 const sent=log.calls.filter(c=>c.method==='PUT');expect(sent).toHaveLength(2)
 expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].idempotencyKey).toBe(sent[0].idempotencyKey)
 expect(sent[1].companyId).toBe(A_ID);expect(log.creates()).toHaveLength(1)
 await waitFor(()=>expect((screen.getByRole('button',{name:'Ana işlemi gönder'}) as HTMLButtonElement).disabled).toBe(false))
})

it('does not expose a foreign-company tax read or issue a tax write',async()=>{
 const log=installFetch({create:()=>json(mutationView(),201),counterparties:call=>call.url.endsWith('/tax-identity')?json(taxView({companyId:B_ID})):emptyPage()})
 mount();const dialog=await createThenOpenTax();await within(dialog).findByText(TAX.loadFailed)
 expect(within(dialog).queryByLabelText(TAX.inputLabel)).toBeNull();expect(log.calls.filter(c=>c.method==='PUT')).toHaveLength(0)
})


it('keeps the tax attempt on stay and never reselects the old company after an explicit scope departure and late acknowledgement',async()=>{
 let resolve!: (response:Response)=>void
 const response=new Promise<Response>(done=>{resolve=done})
 const log=installFetch({create:()=>json(mutationView(),201),counterparties:call=>{
  if(call.url.endsWith('/duplicates'))return json({taxIdentityNumber:'1234567890',kind:'vkn',duplicates:[]})
  if(call.url.endsWith('/tax-identity'))return call.method==='PUT'?response:json(taxView())
  return emptyPage()
 }})
 const onCreated=vi.fn(async()=>true);mount(onCreated)
 const dialog=await createThenOpenTax();await within(dialog).findByText(TAX.none)
 await userEvent.type(within(dialog).getByLabelText(TAX.inputLabel),'1234567890')
 await userEvent.click(within(dialog).getByRole('button',{name:TAX.save}))
 await waitFor(()=>expect(log.calls.filter(c=>c.method==='PUT')).toHaveLength(1))
 await userEvent.click(within(dialog).getByRole('button',{name:T.close}))
 await userEvent.selectOptions(screen.getByRole('combobox',{name:'Şirket'}),B_ID)
 await userEvent.click(await screen.findByRole('button',{name:'Kal'}))
 expect((screen.getByRole('combobox',{name:'Şirket'}) as HTMLSelectElement).value).toBe(A_ID)
 await userEvent.click(screen.getByRole('button',{name:T.resume}))
 expect((within(dialog).getByRole('button',{name:T.recheck}) as HTMLButtonElement).disabled).toBe(true)
 await userEvent.click(within(dialog).getByRole('button',{name:T.close}))
 await userEvent.selectOptions(screen.getByRole('combobox',{name:'Şirket'}),B_ID)
 await userEvent.click(await screen.findByRole('button',{name:'Değişiklikleri bırak'}))
 await screen.findByRole('button',{name:T.open})
 await act(async()=>{resolve(noContent());await response})
 expect(onCreated).toHaveBeenCalledTimes(1)
 expect(log.calls.filter(c=>c.url.includes('/tax-identity')&&c.companyId===B_ID)).toHaveLength(0)
 expect((screen.getByRole('combobox',{name:'Şirket'}) as HTMLSelectElement).value).toBe(B_ID)
 expect(screen.queryByText(T.selected)).toBeNull()
})
