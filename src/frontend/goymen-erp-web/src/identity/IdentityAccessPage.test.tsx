/**
 * AT-004 component cases — "Kullanıcılar ve yetkiler" and the role card.
 *
 * COMPONENT/DOM EVIDENCE ONLY: the harness stubs `fetch` with synthetic fixtures, so nothing here is evidence of
 * real API, real SQL or browser acceptance. What these cases establish: the user flow (pick a user → grants → open
 * a role → back keeps the selection → reopen shows the same), what is NOT sent (no write of any kind, no request for
 * an id this surface could not have produced), and that every read carries the active company.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  COMPANY_A,
  ROLE_AUDITOR,
  ROLE_OPERATOR,
  SITE_OSM,
  USER_ALICE,
  USER_BORA,
  assignment,
  installFetch,
  json,
  problem,
  renderAt,
  requestsFor,
  roleDetail,
  roleSummary,
  userDetail,
  userSummary,
  type RecordedRequest,
} from './testHarness'
import { IDENTITY_CAPABILITY, accessListPath, roleDetailPath } from './capabilities'

afterEach(() => {
  vi.unstubAllGlobals()
})

const USERS = [
  userSummary(USER_BORA, 'Şule Bora', { assignmentCount: 2, effectiveAssignmentCount: 1 }),
  userSummary(USER_ALICE, 'Alice'),
]
const ROLES = [
  roleSummary(ROLE_AUDITOR, 'AUDITOR', 'Denetçi', { holderCount: 0, effectiveHolderCount: 0 }),
  roleSummary(ROLE_OPERATOR, 'OPERATOR', 'Operatör'),
]

function respond(overrides: { user?: () => Response; role?: () => Response; users?: unknown } = {}) {
  return (url: string) => {
    if (url.endsWith('/identity/access/users')) return json(overrides.users ?? USERS)
    if (url.endsWith('/identity/access/roles')) return json(ROLES)
    if (url.includes(`/identity/access/users/${USER_ALICE}`)) {
      return overrides.user?.() ?? json(userDetail(USER_ALICE, 'Alice'))
    }
    if (url.includes(`/identity/access/users/${USER_BORA}`)) return json(userDetail(USER_BORA, 'Şule Bora'))
    if (url.includes(`/identity/access/roles/${ROLE_OPERATOR}`)) {
      return overrides.role?.() ?? json(roleDetail(ROLE_OPERATOR, 'OPERATOR', 'Operatör'))
    }
    return undefined
  }
}

const accessReads = (recorded: readonly RecordedRequest[]) => requestsFor(recorded, '/identity/access/')
const userList = () => screen.getByRole('list', { name: 'Kullanıcılar' })
const assignmentsTable = () => screen.getByRole('table', { name: 'Rol atamaları' })

describe('Kullanıcılar ve yetkiler — list and selection', () => {
  it('lists the users in Turkish order and reads no user until one is picked', async () => {
    const recorded = installFetch({ respond: respond() })
    renderAt(accessListPath())

    expect(await screen.findByRole('heading', { name: 'Kullanıcılar ve yetkiler', level: 1 })).toBeTruthy()
    await waitFor(() => expect(within(userList()).getAllByRole('link')).toHaveLength(2))
    expect(within(userList()).getAllByRole('link').map((link) => link.textContent)).toEqual([
      'Alice1 geçerli / 1 atama',
      'Şule Bora1 geçerli / 2 atama',
    ])
    expect(screen.getByText('Rolleri, yetkileri ve kapsamları görmek için bir kullanıcı seçin.')).toBeTruthy()
    expect(requestsFor(recorded, '/identity/access/users/')).toHaveLength(0)
    // Every read names the active company; none is anything but a GET.
    expect(accessReads(recorded).every((request) => request.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
    expect(recorded.every((request) => request.method === 'GET')).toBe(true)
  })

  it('picking a user shows roles, validity, scopes, approver and the permissions that hold now', async () => {
    const recorded = installFetch({
      respond: respond({
        user: () =>
          json(
            userDetail(USER_ALICE, 'Alice', {
              assignments: [
                assignment(ROLE_OPERATOR, 'OPERATOR', 'Operatör', {
                  scopes: [
                    { type: 'Company', id: COMPANY_A },
                    { type: 'Site', id: SITE_OSM },
                  ],
                }),
                assignment(ROLE_AUDITOR, 'AUDITOR', 'Denetçi', {
                  validity: 'scheduled',
                  validFromUtc: '2027-01-01T00:00:00+00:00',
                  validUntilUtc: '2027-06-01T00:00:00+00:00',
                  approvedBy: null,
                }),
              ],
            }),
          ),
      }),
    })
    renderAt(accessListPath())
    const user = userEvent.setup()

    await user.click(await within(await screen.findByRole('list', { name: 'Kullanıcılar' })).findByRole('link', { name: /Alice/ }))

    expect(await screen.findByRole('heading', { name: 'Alice', level: 2 })).toBeTruthy()
    const operatorRow = within(assignmentsTable()).getByRole('rowheader', { name: /Operatör/ }).closest('tr') as HTMLElement
    expect(within(operatorRow).getByText('Geçerli')).toBeTruthy()
    expect(within(operatorRow).getByText('Süresiz')).toBeTruthy()
    expect(within(operatorRow).getByText('Şirket geneli')).toBeTruthy()
    expect(within(operatorRow).getByText(SITE_OSM)).toBeTruthy()
    expect(within(operatorRow).getByText('Onaylayan Yönetici')).toBeTruthy()
    const auditorRow = within(assignmentsTable()).getByRole('rowheader', { name: /Denetçi/ }).closest('tr') as HTMLElement
    expect(within(auditorRow).getByText('Henüz başlamadı')).toBeTruthy()
    expect(within(auditorRow).getByText('Kayıtlı onaylayan yok')).toBeTruthy()
    expect(within(auditorRow).getByText('01.01.2027 03:00')).toBeTruthy()

    const effective = screen.getByRole('list', { name: 'Şu an geçerli yetkiler' })
    const audit = within(effective).getByText('identity.audit.read').closest('li') as HTMLElement
    expect(within(audit).getByText('Kritik')).toBeTruthy()
    const locations = within(effective).getByText('organization.locations.read').closest('li') as HTMLElement
    expect(within(locations).queryByText('Kritik')).toBeNull()

    expect(within(userList()).getByRole('link', { name: /Alice/ }).getAttribute('aria-current')).toBe('true')
    const reads = requestsFor(recorded, `/identity/access/users/${USER_ALICE}`)
    expect(reads).toHaveLength(1)
    expect(reads[0].headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('opening a role and going back keeps the selected user; reopening the link shows the same', async () => {
    installFetch({ respond: respond() })
    const { router } = renderAt(accessListPath(USER_ALICE))
    const user = userEvent.setup()

    expect(await screen.findByRole('heading', { name: 'Alice', level: 2 })).toBeTruthy()
    await user.click(within(assignmentsTable()).getByRole('rowheader', { name: /Operatör/ }).querySelector('a') as HTMLElement)

    expect(await screen.findByRole('heading', { name: 'Operatör', level: 1 })).toBeTruthy()
    expect(router.state.location.pathname).toBe(`/ayarlar/kullanicilar-ve-yetkiler/roller/${ROLE_OPERATOR}`)
    const permissions = screen.getByRole('list', { name: 'Rolün yetkileri' })
    expect(within(permissions).getByText('identity.audit.read')).toBeTruthy()
    expect(within(permissions).getByText('Kritik')).toBeTruthy()
    const holder = screen.getByRole('rowheader', { name: 'Alice' }).closest('tr') as HTMLElement
    expect(within(holder).getByText('Tesis')).toBeTruthy()
    expect(within(holder).getByText('01.01.2027 03:00')).toBeTruthy()

    await user.click(within(screen.getByRole('navigation', { name: 'Geri' })).getByRole('link'))
    expect(await screen.findByRole('heading', { name: 'Alice', level: 2 })).toBeTruthy()
    expect(router.state.location.search).toBe(`?kullanici=${USER_ALICE}`)
    expect(within(userList()).getByRole('link', { name: /Alice/ }).getAttribute('aria-current')).toBe('true')
  })

  it('a malformed selection is never sent, and a user the server does not return is the neutral not-found', async () => {
    const recorded = installFetch({ respond: respond({ user: () => problem(404) }) })
    renderAt('/ayarlar/kullanicilar-ve-yetkiler?kullanici=not-an-id')
    await waitFor(() => expect(within(userList()).getAllByRole('link')).toHaveLength(2))
    expect(screen.getByText('Rolleri, yetkileri ve kapsamları görmek için bir kullanıcı seçin.')).toBeTruthy()
    expect(requestsFor(recorded, '/identity/access/users/')).toHaveLength(0)
    vi.unstubAllGlobals()

    installFetch({ respond: respond({ user: () => problem(404) }) })
    renderAt(accessListPath(USER_ALICE))
    const blocks = await screen.findAllByText(/Bulunamadı — kullanıcı yetkileri/)
    expect(blocks.length).toBeGreaterThan(0)
  })

  it('an unknown validity token is a malformed answer, never a guessed state', async () => {
    installFetch({
      respond: respond({
        user: () =>
          json(userDetail(USER_ALICE, 'Alice', { assignments: [assignment(ROLE_OPERATOR, 'OPERATOR', 'Operatör', { validity: 'probably' })] })),
      }),
    })
    renderAt(accessListPath(USER_ALICE))
    expect(await screen.findByText(/Beklenmeyen yanıt — kullanıcı yetkileri/)).toBeTruthy()
  })

  it('offers no write surface at all', async () => {
    const recorded = installFetch({ respond: respond() })
    renderAt(accessListPath(USER_ALICE))
    expect(await screen.findByRole('heading', { name: 'Alice', level: 2 })).toBeTruthy()
    const main = screen.getByRole('heading', { name: 'Kullanıcılar ve yetkiler', level: 1 }).closest('section') as HTMLElement
    expect(within(main).queryAllByRole('button')).toHaveLength(0)
    expect(within(main).queryAllByRole('textbox')).toHaveLength(0)
    expect(within(main).queryAllByRole('checkbox')).toHaveLength(0)
    expect(recorded.every((request) => request.method === 'GET')).toBe(true)
  })

  it('without the role card capability a role is plain text and no role is read', async () => {
    const recorded = installFetch({ capabilities: [IDENTITY_CAPABILITY.accessList], respond: respond() })
    renderAt(accessListPath(USER_ALICE))
    expect(await screen.findByRole('heading', { name: 'Alice', level: 2 })).toBeTruthy()
    expect(within(assignmentsTable()).getByRole('rowheader', { name: /Operatör/ }).querySelector('a')).toBeNull()
    expect(screen.queryAllByRole('link', { name: 'Denetçi' })).toHaveLength(0)
    expect(requestsFor(recorded, '/identity/access/roles/')).toHaveLength(0)
  })

  it('without the list capability nothing mounts and nothing is read', async () => {
    const recorded = installFetch({ capabilities: [], respond: respond() })
    renderAt(accessListPath(USER_ALICE))
    expect(await screen.findByText(/Bulunamadı/)).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Kullanıcılar ve yetkiler' })).toBeNull()
    expect(accessReads(recorded)).toHaveLength(0)
  })
})

describe('Kullanıcılar ve yetkiler — role card', () => {
  it('a role with no holder here says so; its permissions still show', async () => {
    installFetch({ respond: respond({ role: () => json(roleDetail(ROLE_OPERATOR, 'OPERATOR', 'Operatör', { holders: [] })) }) })
    renderAt(roleDetailPath(ROLE_OPERATOR))
    expect(await screen.findByText('Bu rol bu şirkette kimseye atanmamış.')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Rolün yetkileri' })).getAllByRole('listitem')).toHaveLength(2)
  })

  it('a holder links back to the access list with that user selected', async () => {
    installFetch({ respond: respond() })
    const { router } = renderAt(roleDetailPath(ROLE_OPERATOR))
    const user = userEvent.setup()
    await user.click(await screen.findByRole('link', { name: 'Alice' }))
    expect(await screen.findByRole('heading', { name: 'Alice', level: 2 })).toBeTruthy()
    expect(router.state.location.search).toBe(`?kullanici=${USER_ALICE}`)
  })

  it('a malformed role id is never sent; an unknown role is the neutral not-found; an answer for another role is malformed', async () => {
    let recorded = installFetch({ respond: respond() })
    renderAt('/ayarlar/kullanicilar-ve-yetkiler/roller/yeni')
    expect(await screen.findByText(/Bulunamadı — rol/)).toBeTruthy()
    expect(requestsFor(recorded, '/identity/access/roles/')).toHaveLength(0)
    vi.unstubAllGlobals()

    recorded = installFetch({ respond: respond({ role: () => problem(404) }) })
    renderAt(roleDetailPath(ROLE_OPERATOR))
    expect(await screen.findByText(/Bulunamadı — rol/)).toBeTruthy()
    vi.unstubAllGlobals()

    installFetch({ respond: respond({ role: () => json(roleDetail(ROLE_AUDITOR, 'AUDITOR', 'Denetçi')) }) })
    renderAt(roleDetailPath(ROLE_OPERATOR))
    expect(await screen.findByText(/Beklenmeyen yanıt — rol/)).toBeTruthy()
  })
})
