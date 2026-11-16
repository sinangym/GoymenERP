import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, IDENTITY, ROLE_OPERATOR, USER_ALICE, USER_BORA, assignment, installFetch, json, renderAt, roleSummary, userDetail, userSummary } from './testHarness'
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
