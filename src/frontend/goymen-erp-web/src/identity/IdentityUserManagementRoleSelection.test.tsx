import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, COMPANY_B, ROLE_AUDITOR, ROLE_OPERATOR, USER_ALICE, installFetch, json, renderAt, roleDetail, roleSummary } from './testHarness'
import { IDENTITY_ACCESS_CONTROL as CONTROL, IDENTITY_CAPABILITY, IDENTITY_ROUTE } from './capabilities'
import { ACCESS_GRANT_TEXT as G } from './identityText'
import { USER_MANAGEMENT_TEXT as T } from './userManagementText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const capabilities = [IDENTITY_CAPABILITY.userManagement, IDENTITY_CAPABILITY.roleDetail, CONTROL.manage]
// This owner response deliberately has no assignment or effective-permission fields.
const managedUser = { userId: USER_ALICE, displayName: 'SENTETIK yeni kullanıcı', userName: 'synthetic', isActive: true, version: '1', mustChangePassword: true, isLocked: false }
const assignmentId = 'aaaaaaaa-1111-4111-8111-111111111111'
const requestId = 'bbbbbbbb-1111-4111-8111-111111111111'
function response(url: string): Response | undefined {
  if (url.endsWith('/identity/access/managed-users')) return json([managedUser])
  if (url.endsWith('/identity/access/roles')) return json([
    roleSummary(ROLE_OPERATOR, 'SENTETIK', 'SENTETIK normal'),
    roleSummary(ROLE_AUDITOR, 'SENTETIK_C', 'SENTETIK kritik', { isCritical: true }),
  ])
  if (url.endsWith(`/identity/access/roles/${ROLE_OPERATOR}`)) return json(roleDetail(ROLE_OPERATOR, 'SENTETIK', 'SENTETIK normal', { permissions: [{ code: 'SENTETIK.read', isCritical: false }] }))
  if (url.endsWith(`/identity/access/roles/${ROLE_AUDITOR}`)) return json(roleDetail(ROLE_AUDITOR, 'SENTETIK_C', 'SENTETIK kritik', { permissions: [{ code: 'SENTETIK.critical', isCritical: true }] }))
  return undefined
}
async function basket() {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: T.assign }))
  await user.click(await screen.findByRole('checkbox', { name: 'SENTETIK normal' }))
  await user.click(screen.getByRole('checkbox', { name: `SENTETIK kritik · ${G.requiresApproval}` }))
  await screen.findByRole('region', { name: G.selectedPermissions })
  return user
}

describe('ROLE-UX first-role management path', () => {
  it('previews live roles for a user without inventing company effective permissions or sending an assignment', async () => {
    const calls = installFetch({ capabilities, respond: response })
    renderAt(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement]); await basket()
    expect(screen.getByText(G.currentPermissionsUnavailable)).toBeTruthy()
    expect(screen.queryByText(G.noCurrentPermissions)).toBeNull()
    expect(screen.getByText(`${G.company}: GDC · Göymen Demir Çelik`)).toBeTruthy()
    expect(within(screen.getByRole('region', { name: G.selectedPermissions })).getByText('SENTETIK.read')).toBeTruthy()
    expect(calls.some(call => /\/identity\/access\/users\//.test(call.url))).toBe(false)
    expect(calls.filter(call => call.method === 'POST')).toEqual([])
  })
  it('requires separate human commands for the first normal role and the critical request, retaining the remaining draft', async () => {
    const calls = installFetch({ capabilities, respond: (url, init) => init?.method === 'POST'
      ? url.endsWith('/grant-requests') ? json({ id: requestId, version: '1', status: 'Pending', roleAssignmentId: null }, 201)
        : json({ id: assignmentId, version: '1', status: 'Active', roleAssignmentId: assignmentId }, 201)
      : response(url) })
    renderAt(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement]); const user = await basket()
    await user.click(screen.getByRole('button', { name: `${G.prepareRole} · SENTETIK normal` }))
    expect(screen.getByLabelText(G.role)).toHaveProperty('disabled', true)
    await user.type(screen.getByLabelText(G.reason), 'SENTETIK first role')
    await user.click(screen.getByRole('button', { name: G.assign }))
    await screen.findByText(`${G.remainingRoles}: 1`)
    expect(calls.filter(call => call.method === 'POST')).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: `${G.prepareRole} · SENTETIK kritik` }))
    await user.type(screen.getByLabelText(G.reason), 'SENTETIK critical request')
    await user.click(screen.getByRole('button', { name: G.request }))
    await screen.findByText(`${G.remainingRoles}: 0`)
    const posts = calls.filter(call => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].body).toMatchObject({ userId: USER_ALICE, roleId: ROLE_OPERATOR })
    expect(posts[1].body).toMatchObject({ userId: USER_ALICE, roleId: ROLE_AUDITOR })
    expect(posts[1].url).toMatch(/\/grant-requests$/)
    expect(posts.every(call => call.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
    expect(posts[0].headers['Idempotency-Key']).not.toBe(posts[1].headers['Idempotency-Key'])
    expect(screen.getAllByText(G.roleStepRecorded)).toHaveLength(2)
    expect(screen.getByText(G.currentPermissionsUnavailable)).toBeTruthy()
  })
  it('freezes the basket on an unknown first-role result and retries the same native attempt without fanout', async () => {
    let attempts = 0
    const calls = installFetch({ capabilities, respond: (url, init) => init?.method === 'POST'
      ? ++attempts === 1 ? json({}, 503) : new Response(JSON.stringify({ id: assignmentId, version: '1', status: 'Active', roleAssignmentId: assignmentId }),
        { status: 201, headers: { 'Content-Type': 'application/json', 'Idempotency-Replayed': 'true' } }) : response(url) })
    renderAt(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement]); const user = await basket()
    await user.click(screen.getByRole('button', { name: `${G.prepareRole} · SENTETIK normal` }))
    await user.type(screen.getByLabelText(G.reason), 'SENTETIK retry')
    await user.click(screen.getByRole('button', { name: G.assign })); await screen.findByText(G.unknown)
    expect(screen.getByRole('button', { name: `${G.prepareRole} · SENTETIK kritik` })).toHaveProperty('disabled', true)
    await user.click(screen.getByRole('button', { name: G.retry })); await screen.findByText(G.replay)
    const posts = calls.filter(call => call.method === 'POST')
    expect(posts).toHaveLength(2); expect(posts[0].body).toEqual(posts[1].body)
    expect(posts[0].headers['Idempotency-Key']).toBe(posts[1].headers['Idempotency-Key'])
    expect(screen.getByText(`${G.remainingRoles}: 1`)).toBeTruthy()
  })
  it('does not infer grant management from the global managed-user list', async () => {
    const calls = installFetch({ capabilities: [IDENTITY_CAPABILITY.userManagement], respond: response })
    renderAt(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement]); await screen.findByText(managedUser.displayName)
    expect(screen.queryByRole('button', { name: T.assign })).toBeNull()
    expect(calls.some(call => call.url.endsWith('/identity/access/roles'))).toBe(false)
    expect(calls.filter(call => call.method === 'POST')).toEqual([])
  })
  it('does not prepare a basket role without the native role-detail capability', async () => {
    const calls = installFetch({ capabilities: [IDENTITY_CAPABILITY.userManagement, CONTROL.manage], respond: response })
    renderAt(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement]); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.assign }))
    await user.click(await screen.findByRole('checkbox', { name: 'SENTETIK normal' }))
    expect(screen.getByRole('button', { name: `${G.prepareRole} · SENTETIK normal` })).toHaveProperty('disabled', true)
    expect(screen.getByText(G.previewUnavailable)).toBeTruthy()
    expect(calls.some(call => /\/identity\/access\/roles\//.test(call.url))).toBe(false)
    expect(calls.filter(call => call.method === 'POST')).toEqual([])
  })
  it('clears the prepared first-role command and basket when the company changes', async () => {
    const calls = installFetch({ capabilities, respond: response })
    renderAt(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement]); const user = await basket()
    await user.click(screen.getByRole('button', { name: `${G.prepareRole} · SENTETIK normal` }))
    await user.type(screen.getByLabelText(G.reason), 'SENTETIK old company')
    await user.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    await waitFor(() => expect(screen.queryByLabelText(G.reason)).toBeNull())
    await user.click(await screen.findByRole('button', { name: T.assign }))
    expect(await screen.findByRole('checkbox', { name: 'SENTETIK normal' })).toHaveProperty('checked', false)
    expect(screen.getByText(`${G.company}: GSP · Göymen Sac Profil`)).toBeTruthy()
    expect(screen.queryByText(G.roleStepRecorded)).toBeNull()
    expect(calls.filter(call => call.method === 'POST')).toEqual([])
  })
})
