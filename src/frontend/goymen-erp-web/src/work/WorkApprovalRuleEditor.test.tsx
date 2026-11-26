import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem, commands } from '../settings/testHarness'
import { recordWorkApprovalRule, isRecordWorkApprovalRule } from '../settings/workApprovalRulesApi'
import { decodeWorkApprovalRule } from '../settings/workApprovalRulesContract'
import { RULE_EDITOR_TEXT as T } from './WorkApprovalRuleEditorText'
import { workRoutes } from './workRoutes'

const KIND = 'manufacturing.plan.v1'
const PATH = '/work/approval-rules'
const CAP = 'settings.work-approval-duration.manage'
const clients: QueryClient[] = []
const rule = (version = '0') => ({
  kind: KIND, ownerModule: 'manufacturing', approverPermission: 'manufacturing.plans.approve', durationClass: version === '0' ? 'Hours' : 'BusinessDays',
  escalationPermission: null, version, versionText: `work.approval-rule:${KIND}:${version}`, isDefault: version === '0',
  defaultBasis: version === '0' ? 'YOL-D18' : null, state: 'Current', recordedBy: version === '0' ? null : IDENTITY.userId,
  recordedAtUtc: version === '0' ? null : '2026-11-27T07:00:00Z', reason: version === '0' ? null : 'SENTETIK',
  versions: version === '0' ? [] : [{version,durationClass:'BusinessDays',approverPermission:'manufacturing.plans.approve',
    escalationPermission:null,recordedBy:IDENTITY.userId,recordedAtUtc:'2026-11-27T07:00:00Z',reason:'SENTETIK'}],
})
const table = (version = '0') => ({companyId: COMPANY_A,rules:[rule(version)]})
const body = {expectedVersion:'0',durationClass:'BusinessDays',escalationPermission:null,reason:'SENTETIK'} as const
function mount() {
  const router = createMemoryRouter([{path:'/',element:<AppShell />,children:workRoutes}],{initialEntries:['/calisma-yonetimi']})
  const client = new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}}); clients.push(client)
  return render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>)
}
async function fill() {
  const user = userEvent.setup()
  await user.click(await screen.findByText(/— kuralı düzenle/))
  await user.selectOptions(screen.getByRole('combobox',{name:T.duration}),'BusinessDays')
  await user.type(screen.getByRole('textbox',{name:T.reason}),'SENTETIK')
  await user.click(screen.getByRole('button',{name:T.save}))
}
afterEach(() => { cleanup(); clients.splice(0).forEach(c => c.clear()); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); Object.assign(IDENTITY,{authenticationSessionBinding:null}) })

describe('WP8 rule editor over the existing shell (SENTETIK)',() => {
  it('records exactly the default version and editable fields, retaining the read-only owner permission',async() => {
    let version = '0'
    const calls = installFetch({capabilities:[CAP],respond:(url,init) => {
      if (url.endsWith(`${PATH}/${KIND}`) && init?.method === 'PUT') {version='1';return json(rule('1'))}
      if (url.endsWith(PATH)) return json(table(version))
      return undefined
    }})
    mount();await fill()
    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect(commands(calls)).toHaveLength(1)
    expect(commands(calls)[0].body).toEqual(body)
    expect(commands(calls)[0].headers['X-Company-Id']).toBe(COMPANY_A)
    expect(screen.queryByRole('combobox',{name:T.approver})).toBeNull()
    expect(calls.filter(c => c.url.endsWith(PATH)).length).toBeGreaterThanOrEqual(2)
  })
  it('freezes unknown payload and restores the same key after remount before retry',async() => {
    Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-rule-session'})
    let sent = 0
    const calls = installFetch({capabilities:[CAP],respond:(url,init) => {
      if (url.endsWith(`${PATH}/${KIND}`) && init?.method === 'PUT') return ++sent === 1 ? problem(503)
        : new Response(JSON.stringify(rule('1')),{status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}})
      return url.endsWith(PATH) ? json(table(sent > 0 ? '2' : '0')) : undefined
    }})
    const first=mount();await fill();expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect(screen.getByRole('textbox',{name:T.reason}).matches(':disabled')).toBe(true)
    first.unmount();mount()
    await screen.findByText(T.unknown)
    expect(screen.getByText('0',{selector:'code'})).toBeTruthy()
    expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toBe('SENTETIK')
    await userEvent.setup().click(screen.getByRole('button',{name:T.retry}))
    expect(await screen.findByText(T.replayed)).toBeTruthy()
    expect(commands(calls)).toHaveLength(2)
    expect(commands(calls)[1].body).toEqual(commands(calls)[0].body)
    expect(commands(calls)[1].headers['Idempotency-Key']).toBe(commands(calls)[0].headers['Idempotency-Key'])
  })
  it('blocks stale resubmission until the user rereads a changed version',async() => {
    let version = '0'
    const calls=installFetch({capabilities:[CAP],respond:(url,init) => {
      if (url.endsWith(`${PATH}/${KIND}`) && init?.method === 'PUT') { version='2';return problem(409,{code:'work_approval_rule_version_conflict'}) }
      return url.endsWith(PATH) ? json(table(version)) : undefined
    }})
    mount();await fill();await screen.findByText(T.conflict)
    expect(screen.getByRole('textbox',{name:T.reason}).matches(':disabled')).toBe(true)
    await userEvent.setup().click(screen.getByRole('button',{name:T.refresh}))
    await waitFor(() => expect(screen.queryByText(T.conflict)).toBeNull())
    await userEvent.setup().click(screen.getByText(/— kuralı düzenle/))
    expect(screen.getByText('2',{selector:'code'})).toBeTruthy()
    expect(commands(calls)).toHaveLength(1)
  })
  it('isolates an unresolved attempt when the active company changes',async() => {
    Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-rule-company-session'})
    const calls=installFetch({capabilities:[CAP],respond:(url,init) => {
      if (url.endsWith(`${PATH}/${KIND}`) && init?.method === 'PUT') return problem(503)
      if (url.endsWith(PATH)) return json({...table(),companyId:((init?.headers ?? {}) as Record<string,string>)['X-Company-Id']})
      return undefined
    }})
    mount();await fill();await screen.findByText(T.unknown)
    const user=userEvent.setup()
    await user.selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
    await waitFor(() => expect(screen.queryByText(T.unknown)).toBeNull())
    await user.click(await screen.findByText(/— kuralı düzenle/))
    expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toBe('')
    expect(commands(calls)).toHaveLength(1)
    await user.selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_A)
    await screen.findByText(T.unknown)
    expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toBe('SENTETIK')
    expect(commands(calls)).toHaveLength(1)
  })
  it.each(['company','shape'])('keeps commands unavailable for wrong %s',async(mode) => {
    installFetch({capabilities:[CAP],respond:url => url.endsWith(PATH) ? json(mode==='company' ? {...table(),companyId:COMPANY_B} : {...table(),extra:true}) : undefined})
    mount();await screen.findByTestId('work-approval-rules')
    await waitFor(() => expect(screen.getByTestId('work-approval-rules').textContent).toContain('Beklenmeyen'))
    expect(screen.queryByRole('heading',{name:T.heading})).toBeNull()
  })
  it('does not read or offer the editor without its existing capability',async() => {
    const calls=installFetch({capabilities:['work.delegations.manage']})
    mount();await screen.findByRole('heading',{name:'Work yönetimi',level:1})
    expect(screen.queryByRole('heading',{name:T.heading})).toBeNull()
    expect(calls.some(c => c.url.includes(PATH))).toBe(false)
  })
  it('accepts the WP2 registered escalation default without changing expected version zero',async() => {
    installFetch({capabilities:[CAP],respond:url => url.endsWith(PATH) ? json({companyId:COMPANY_A,rules:[{...rule(),escalationPermission:'work.approvals.escalate'}]}) : undefined})
    mount();await userEvent.setup().click(await screen.findByText(/— kuralı düzenle/))
    expect((screen.getByRole('combobox',{name:T.escalation}) as HTMLSelectElement).value).toBe('work.approvals.escalate')
    expect(screen.getByText('0',{selector:'code'})).toBeTruthy()
  })
})

describe('WP8 strict command receipt',() => {
  // Keep each mutation a valid owner projection so the command correlation guard, rather than shape rejection, is exercised.
  const changedRule = (fields: Record<string,unknown>) => {
    const current=rule('1')
    return {...current,...fields,versions:current.versions.map(version => ({...version,...fields}))}
  }
  it.each([
    ['wrong kind', {...rule('1'),kind:'manufacturing.other.v1',versionText:'work.approval-rule:manufacturing.other.v1:1'},{}],
    ['extra member',{...rule('1'),extra:true},{}],
    ['unchanged version',rule(),{}],
    ['incomplete replay',rule('1'),{'Idempotency-Replayed':'true'}],
    ['orphan original',rule('1'),{'Idempotency-Original-Correlation-Id':'original'}],
    ['fresh mismatched duration',changedRule({durationClass:'Hours'}),{}],
    ['fresh mismatched escalation',changedRule({escalationPermission:'work.approvals.escalate'}),{}],
    ['fresh mismatched reason',changedRule({reason:'SENTETIK another change'}),{}],
    ['fresh jumped version',rule('3'),{}],
  ])('leaves %s unresolved',async(_name,value,headers) => {
    if (_name.startsWith('fresh')) expect(decodeWorkApprovalRule(value)).not.toBeNull()
    vi.stubGlobal('fetch',vi.fn(async() => new Response(JSON.stringify(value),{status:200,headers:{'Content-Type':'application/json',...headers}})))
    expect((await recordWorkApprovalRule(COMPANY_A,KIND,body,'SENTETIK-key')).type).toBe('unknown')
  })
  it('accepts a valid replay projecting a later current version with different values',async() => {
    const value={...rule('4'),...{durationClass:'Hours',escalationPermission:'work.approvals.escalate',reason:'SENTETIK later edit'},
      versions:rule('4').versions.map(version => ({...version,durationClass:'Hours',escalationPermission:'work.approvals.escalate',reason:'SENTETIK later edit'}))}
    vi.stubGlobal('fetch',vi.fn(async() => new Response(JSON.stringify(value),{status:200,headers:{
      'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original',
    }})))
    expect(await recordWorkApprovalRule(COMPANY_A,KIND,body,'SENTETIK-key')).toEqual({type:'applied',data:value,replayed:true})
  })
  it('rejects persisted edits to owner permission and noncanonical versions',() => {
    expect(isRecordWorkApprovalRule(body)).toBe(true)
    expect(isRecordWorkApprovalRule({...body,approverPermission:'other.approve'})).toBe(false)
    expect(isRecordWorkApprovalRule({...body,expectedVersion:'00'})).toBe(false)
  })
})
