import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, IDENTITY, ROLE_OPERATOR, ROLE_AUDITOR, USER_ALICE, USER_BORA, assignment, installFetch, json, renderAt, roleDetail, roleSummary, userDetail, userSummary } from './testHarness'
import { IDENTITY_ACCESS_CONTROL as CONTROL, IDENTITY_CAPABILITY, accessListPath } from './capabilities'
import { ACCESS_GRANT_TEXT as T } from './identityText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const assignmentId = 'aaaaaaaa-1111-4111-8111-111111111111'
const requestId = 'bbbbbbbb-1111-4111-8111-111111111111'
const selected = () => userDetail(USER_ALICE, 'SENTETIK hedef', { assignments: [assignment(ROLE_OPERATOR, 'SENTETIK', 'SENTETIK rol', { assignmentId, version: '7' })] })
function response(url: string, critical = false): Response | undefined {
  if (url.endsWith('/identity/access/users')) return json([userSummary(USER_ALICE, 'SENTETIK hedef')])
  if (url.endsWith('/identity/access/roles')) return json([roleSummary(ROLE_OPERATOR, 'SENTETIK', 'SENTETIK rol', { isCritical: critical })])
  if (url.endsWith(`/identity/access/users/${USER_ALICE}`)) return json(selected())
  return undefined
}
const grantRequest = (requestedBy = USER_BORA, target = USER_ALICE) => ({ requestId, requestedBy: { userId: requestedBy, displayName: 'SENTETIK açan' }, target: { userId: target, displayName: 'SENTETIK hedef' }, roleId: ROLE_OPERATOR, roleCode: 'SENTETIK', roleName: 'SENTETIK rol', status: 'Pending', requestedAtUtc: '2026-11-16T10:00:00Z', validFromUtc: null, validUntilUtc: null, reason: 'SENTETIK', version: '9' })

describe('KIM-S6 access commands', () => {
  it('uses the server critical flag to open a request instead of directly assigning a role', async () => {
    const calls = installFetch({ capabilities: [IDENTITY_CAPABILITY.accessList, CONTROL.manage], respond: (url, init) => {
      if (init?.method === 'POST' && url.endsWith('/grant-requests')) return json({ id: requestId, version: '1', status: 'Pending', roleAssignmentId: null }, 201)
      return response(url, true)
    } })
    renderAtSelected()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.assign }))
    await user.selectOptions(screen.getByLabelText(T.role), ROLE_OPERATOR)
    expect(screen.getByText(T.critical)).toBeTruthy()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK atama')
    await user.click(screen.getByRole('button', { name: T.request }))
    expect(await screen.findByText(T.succeeded)).toBeTruthy()
    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0].url).toMatch(/\/identity\/access\/grant-requests$/)
    expect(posts[0].headers['X-Company-Id']).toBe(COMPANY_A)
    expect(posts[0].body).toMatchObject({ roleId: ROLE_OPERATOR, userId: USER_ALICE, reason: 'SENTETIK atama', validFromUtc: null, validUntilUtc: null })
  })

  it('revokes with the persisted version and preserves its attempt after an unknown outcome', async () => {
    let attempts = 0
    const calls = installFetch({ capabilities: [IDENTITY_CAPABILITY.accessList, CONTROL.manage], respond: (url, init) => {
      if (init?.method === 'POST' && url.endsWith('/revoke')) {
        attempts += 1
        return attempts === 1 ? new Response('{}', { status: 503 }) : new Response(JSON.stringify({ id: assignmentId, version: '8', status: 'Revoked', roleAssignmentId: assignmentId }), { status: 200, headers: { 'Content-Type': 'application/json', 'Idempotency-Replayed': 'true' } })
      }
      return response(url)
    } })
    renderAtSelected()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: `${T.revoke} · SENTETIK rol` }))
    expect(screen.getByText(T.lastAssignment)).toBeTruthy()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK geri alma')
    await user.click(screen.getByRole('button', { name: T.confirm }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.replay)).toBeTruthy()
    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].body).toEqual({ expectedVersion: '7', reason: 'SENTETIK geri alma' })
    expect(posts[1].body).toEqual(posts[0].body)
    expect(posts[1].headers['Idempotency-Key']).toBe(posts[0].headers['Idempotency-Key'])
  })

  it('shows a distinct last Admin refusal without reporting a successful revocation', async () => {
    installFetch({ capabilities: [IDENTITY_CAPABILITY.accessList, CONTROL.manage], respond: (url, init) => init?.method === 'POST'
      ? json({ code: 'identity.last_admin_required' }, 409) : response(url) })
    renderAtSelected()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: `${T.revoke} · SENTETIK rol` }))
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.confirm }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.lastAdmin)
    expect(screen.queryByText(T.succeeded)).toBeNull()
  })

  it.each(['requester', 'target'])('hides approve and reject when the actor is the %s', async (self) => {
    const calls = installFetch({ capabilities: [IDENTITY_CAPABILITY.accessList, CONTROL.manage, CONTROL.decide], respond: (url) => url.endsWith('/grant-requests?status=pending')
      ? json([self === 'requester' ? grantRequest(IDENTITY.userId) : grantRequest(USER_BORA, IDENTITY.userId)]) : response(url) })
    renderAtSelected()
    expect(await screen.findByText(T.secondPerson)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: T.reject })).toBeNull()
    expect(calls.filter((call) => call.method === 'POST')).toEqual([])
  })

  it('uses a pending request version on second-person approval', async () => {
    const calls = installFetch({ capabilities: [IDENTITY_CAPABILITY.accessList, CONTROL.decide], respond: (url, init) => {
      if (url.endsWith('/grant-requests?status=pending')) return json([grantRequest()])
      if (init?.method === 'POST' && url.endsWith('/approve')) return json({ id: requestId, version: '10', status: 'Approved', roleAssignmentId: assignmentId })
      return response(url)
    } })
    renderAtSelected()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.approve }))
    await user.click(screen.getByRole('button', { name: T.confirm }))
    await waitFor(() => expect(calls.filter((call) => call.method === 'POST')).toHaveLength(1))
    expect(calls.find((call) => call.method === 'POST')?.body).toEqual({ expectedVersion: '9' })
  })
})

const renderAtSelected = () => renderAt(accessListPath(USER_ALICE))

function selectionResponse(url: string): Response | undefined {
  if (url.endsWith('/identity/access/roles')) return json([
    roleSummary(ROLE_OPERATOR, 'SENTETIK-MAKER', 'SENTETIK maker'),
    roleSummary(ROLE_AUDITOR, 'SENTETIK-REVIEW', 'SENTETIK reviewer', { isCritical: true }),
    roleSummary('66666666-6666-4666-8666-666666666666', 'SENTETIK-PASSIVE', 'SENTETIK passive', { isActive: false }),
  ])
  if (url.endsWith(`/identity/access/roles/${ROLE_OPERATOR}`)) return json(roleDetail(ROLE_OPERATOR, 'SENTETIK-MAKER', 'SENTETIK maker', {
    permissions: [{ code: 'SENTETIK.shared', isCritical: false }, { code: 'SENTETIK.propose', isCritical: false }],
  }))
  if (url.endsWith(`/identity/access/roles/${ROLE_AUDITOR}`)) return json(roleDetail(ROLE_AUDITOR, 'SENTETIK-REVIEW', 'SENTETIK reviewer', {
    permissions: [{ code: 'SENTETIK.shared', isCritical: false }, { code: 'SENTETIK.review', isCritical: true }],
  }))
  return response(url)
}
const selectionCapabilities = [IDENTITY_CAPABILITY.accessList, IDENTITY_CAPABILITY.roleDetail, CONTROL.manage]
async function chooseRoleBasket() {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('checkbox', { name: 'SENTETIK maker' }))
  await user.click(screen.getByRole('checkbox', { name: `SENTETIK reviewer · ${T.requiresApproval}` }))
  await within(await screen.findByRole('region', { name: T.selectedPermissions })).findByText('SENTETIK.propose')
  return user
}
describe('existing role selection preview and explicit native commands', () => {
  it('shows only live active roles, separates selected permission union from current owner permissions, and writes nothing', async () => {
    const calls = installFetch({ capabilities: selectionCapabilities, respond: selectionResponse })
    renderAtSelected()
    await chooseRoleBasket()
    expect(screen.queryByRole('checkbox', { name: 'SENTETIK passive' })).toBeNull()
    expect(screen.getByText(`${T.company}: GDC · Göymen Demir Çelik`)).toBeTruthy()
    const proposed = within(screen.getByRole('region', { name: T.selectedPermissions }))
    expect(proposed.getAllByText('SENTETIK.shared')).toHaveLength(1)
    expect(proposed.getByText(`SENTETIK.review · ${T.requiresApproval}`)).toBeTruthy()
    const current = within(screen.getByRole('region', { name: T.currentPermissions }))
    expect(current.getByText('organization.locations.read')).toBeTruthy()
    expect(current.queryByText('SENTETIK.propose')).toBeNull()
    expect(calls.filter(call => call.method === 'POST')).toEqual([])
    const details = calls.filter(call => /\/identity\/access\/roles\//.test(call.url))
    expect(details).toHaveLength(2)
    expect(details.every(call => call.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
  })
  it('records one human-confirmed role at a time and retains the remaining selection without automatically assigning it', async () => {
    const calls = installFetch({ capabilities: selectionCapabilities, respond: (url, init) => {
      if (init?.method === 'POST') return url.endsWith('/grant-requests')
        ? json({ id: requestId, version: '1', status: 'Pending', roleAssignmentId: null }, 201)
        : json({ id: assignmentId, version: '1', status: 'Active', roleAssignmentId: assignmentId }, 201)
      return selectionResponse(url)
    } })
    renderAtSelected(); const user = await chooseRoleBasket()
    await user.click(screen.getByRole('button', { name: `${T.prepareRole} · SENTETIK maker` }))
    expect(screen.getByLabelText(T.role).hasAttribute('disabled')).toBe(true)
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK first role')
    await user.click(screen.getByRole('button', { name: T.assign }))
    expect(await screen.findByText(`${T.remainingRoles}: 1`)).toBeTruthy()
    expect(screen.getByText(T.roleStepRecorded)).toBeTruthy()
    expect(calls.filter(call => call.method === 'POST')).toHaveLength(1)
    expect(calls.find(call => call.method === 'POST')?.body).toMatchObject({ roleId: ROLE_OPERATOR, userId: USER_ALICE })
    await user.click(screen.getByRole('button', { name: `${T.prepareRole} · SENTETIK reviewer` }))
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK second role')
    await user.click(screen.getByRole('button', { name: T.request }))
    expect(await screen.findByText(`${T.remainingRoles}: 0`)).toBeTruthy()
    const posts = calls.filter(call => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[1].url).toMatch(/\/grant-requests$/)
    expect(posts[1].body).toMatchObject({ roleId: ROLE_AUDITOR, userId: USER_ALICE })
    expect(posts[0].headers['Idempotency-Key']).not.toBe(posts[1].headers['Idempotency-Key'])
    expect(within(screen.getByRole('region', { name: T.currentPermissions })).queryByText('SENTETIK.review')).toBeNull()
  })
  it('freezes the basket during an unknown native result and retries only the original role body and key', async () => {
    let attempts = 0
    const calls = installFetch({ capabilities: selectionCapabilities, respond: (url, init) => {
      if (init?.method === 'POST') return ++attempts === 1 ? json({}, 503)
        : new Response(JSON.stringify({ id: assignmentId, version: '1', status: 'Active', roleAssignmentId: assignmentId }),
          { status: 201, headers: { 'Content-Type': 'application/json', 'Idempotency-Replayed': 'true' } })
      return selectionResponse(url)
    } })
    renderAtSelected(); const user = await chooseRoleBasket()
    await user.click(screen.getByRole('button', { name: `${T.prepareRole} · SENTETIK maker` }))
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK same role')
    await user.click(screen.getByRole('button', { name: T.assign }))
    await screen.findByText(T.unknown)
    await user.click(screen.getByRole('checkbox', { name: `SENTETIK reviewer · ${T.requiresApproval}` }))
    expect(screen.getByRole('checkbox', { name: `SENTETIK reviewer · ${T.requiresApproval}` })).toHaveProperty('checked', true)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.replay)
    const posts = calls.filter(call => call.method === 'POST')
    expect(posts).toHaveLength(2); expect(posts[0].body).toEqual(posts[1].body)
    expect(posts[0].headers['Idempotency-Key']).toBe(posts[1].headers['Idempotency-Key'])
    expect(screen.getByText(`${T.remainingRoles}: 1`)).toBeTruthy()
  })
  it('does not label an unavailable role preview complete or create a command', async () => {
    const calls = installFetch({ capabilities: selectionCapabilities, respond: url => url.endsWith(`/identity/access/roles/${ROLE_OPERATOR}`)
      ? json({}, 503) : selectionResponse(url) })
    renderAtSelected(); const user = userEvent.setup()
    await user.click(await screen.findByRole('checkbox', { name: 'SENTETIK maker' }))
    await waitFor(() => expect(calls.some(call => call.url.endsWith(`/identity/access/roles/${ROLE_OPERATOR}`))).toBe(true))
    expect(screen.queryByRole('region', { name: T.selectedPermissions })).toBeNull()
    expect(screen.getByRole('button', { name: `${T.prepareRole} · SENTETIK maker` }).hasAttribute('disabled')).toBe(true)
    expect(calls.filter(call => call.method === 'POST')).toEqual([])
  })
})
