/**
 * SAT-D61…D70 component cases — Ayarlar > İskonto limitleri.
 *
 * COMPONENT/DOM EVIDENCE ONLY: the harness stubs `fetch` with synthetic fixtures; nothing here is evidence of the real API,
 * real SQL or browser acceptance (that is `SettingsSalesDiscountLimitSqlServerRelationalAcceptanceTests`). What these cases
 * establish: the states (loading, empty, error, forbidden, conflict, unknown outcome), what is SENT (company header,
 * Idempotency-Key, exact bodies with versions as text and the reason), the same key on a retry after an unknown outcome,
 * and that the binding is written for every company the server listed.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  COMPANY_A,
  COMPANY_B,
  GROUP_MANAGER,
  GROUP_SELLER,
  LIMIT_PATH,
  PERSON,
  binding,
  commands,
  groupList,
  installFetch,
  json,
  noContent,
  problem,
  renderAt,
} from './testHarness'
import { LIMIT_TEXT } from './discountLimitText'

afterEach(() => {
  vi.unstubAllGlobals()
})

const groupsTable = () => screen.getByRole('table', { name: LIMIT_TEXT.groupsTableLabel })

describe('İskonto limitleri — groups', () => {
  it('shows the groups with their recorded and in-force limits, and offers no change on a passive group', async () => {
    const recorded = installFetch({ respond: (url) => (url.endsWith('/settings/sales-discount-limits/groups') ? json(groupList()) : undefined) })
    renderAt(LIMIT_PATH)

    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })
    const rows = within(groupsTable()).getAllByRole('row')
    expect(rows).toHaveLength(4)
    expect(within(rows[1]).getByText('SATIS-DESTEK + 3 puan')).toBeTruthy()
    expect(within(rows[1]).getByText('%38')).toBeTruthy()
    expect(within(rows[2]).getByText('Sabit %35')).toBeTruthy()
    expect(within(rows[3]).getByText(LIMIT_TEXT.passive)).toBeTruthy()
    expect(within(rows[3]).getByText(LIMIT_TEXT.noLimit)).toBeTruthy()
    expect(within(rows[3]).queryByRole('button')).toBeNull()
    const read = recorded.find((request) => request.url.endsWith('/settings/sales-discount-limits/groups'))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('distinguishes empty, forbidden and unavailable, and an ungranted screen calls nothing', async () => {
    installFetch({ respond: (url) => (url.endsWith('/groups') ? json(groupList({ groups: [] })) : undefined) })
    renderAt(LIMIT_PATH)
    expect(await screen.findByText(LIMIT_TEXT.noGroups)).toBeTruthy()
    vi.unstubAllGlobals()
    document.body.innerHTML = ''

    installFetch({ respond: (url) => (url.endsWith('/groups') ? problem(503, { code: 'settings_provider_unavailable' }) : undefined) })
    renderAt(LIMIT_PATH)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy())
    vi.unstubAllGlobals()
    document.body.innerHTML = ''

    const recorded = installFetch({ capabilities: [], respond: () => json(groupList()) })
    renderAt(LIMIT_PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })

  it('creates a group with the normalized code, the reason and one Idempotency-Key, then re-reads', async () => {
    let created = false
    const recorded = installFetch({
      respond: (url, init) => {
        if (url.endsWith('/settings/sales-discount-limits/groups') && init?.method === 'POST') {
          created = true
          return json({ id: '66666666-6666-4666-8666-666666666666', version: '1' }, 201)
        }
        if (url.endsWith('/settings/sales-discount-limits/groups')) return json(groupList())
        return undefined
      },
    })
    renderAt(LIMIT_PATH)
    const user = userEvent.setup()
    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })

    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.newGroup }))
    await user.type(screen.getByLabelText(LIMIT_TEXT.codeLabel), ' kidemli-satici ')
    await user.type(screen.getByLabelText(LIMIT_TEXT.nameLabel), 'Kıdemli Satıcı')
    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Yönetim kurulu kararı')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.save }))

    expect(await screen.findByRole('status')).toHaveProperty('textContent', LIMIT_TEXT.saved)
    expect(created).toBe(true)
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(command.body).toEqual({ code: 'KIDEMLI-SATICI', name: 'Kıdemli Satıcı', reason: 'Yönetim kurulu kararı' })
    await waitFor(() =>
      expect(recorded.filter((request) => request.url.endsWith('/groups') && request.method === 'GET').length).toBe(2),
    )
  })

  it('records a linked limit (group + extra points) with the version as text, and shows a cycle refusal', async () => {
    const recorded = installFetch({
      respond: (url, init) => {
        if (url.endsWith(`/groups/${GROUP_SELLER}/limit`) && init?.method === 'PUT') {
          return problem(422, { code: 'settings_discount_limit_link_cycle' })
        }
        if (url.endsWith('/groups')) return json(groupList())
        return undefined
      },
    })
    renderAt(LIMIT_PATH)
    const user = userEvent.setup()
    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })

    await user.click(screen.getByRole('button', { name: `${LIMIT_TEXT.editLimit} — SATIS-DESTEK` }))
    await user.click(screen.getByRole('radio', { name: LIMIT_TEXT.kindLinked }))
    await user.selectOptions(screen.getByLabelText(LIMIT_TEXT.linkedGroupLabel), GROUP_MANAGER)
    await user.type(screen.getByLabelText(LIMIT_TEXT.extraPointsLabel), '2.5')
    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Deneme')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.save }))

    expect((await screen.findByRole('alert')).textContent).toContain('döngü')
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '2',
      kind: 'Linked',
      percent: null,
      linkedGroupId: GROUP_MANAGER,
      extraPoints: '2.5',
      reason: 'Deneme',
      // SAT-D78: the seller already has a change scheduled for 1 November, so a new record starts no earlier.
      effectiveFrom: '2026-11-01',
    })
  })

  it('SAT-D78: shows the scheduled change and sends a later effective date, refusing a past one before sending', async () => {
    const recorded = installFetch({
      respond: (url, init) => {
        if (url.endsWith(`/groups/${GROUP_MANAGER}/limit`) && init?.method === 'PUT') return noContent()
        if (url.endsWith('/groups')) return json(groupList())
        return undefined
      },
    })
    renderAt(LIMIT_PATH)
    const user = userEvent.setup()
    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })
    const scheduled = screen.getByRole('list', { name: `${LIMIT_TEXT.scheduledListLabel} — SATIS-DESTEK` })
    expect(scheduled.textContent).toContain('01.11.2026 itibarıyla: Sabit %40 — o gün %40')

    await user.click(screen.getByRole('button', { name: `${LIMIT_TEXT.editLimit} — MUDUR` }))
    const date = screen.getByLabelText(LIMIT_TEXT.effectiveFromLabel)
    expect((date as HTMLInputElement).value).toBe('2026-10-27')
    await user.clear(date)
    await user.type(date, '2026-10-26')
    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Geçmiş')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.save }))
    expect(screen.getByText(LIMIT_TEXT.effectiveFromError)).toBeTruthy()
    expect(commands(recorded)).toEqual([])

    await user.clear(date)
    await user.type(date, '2027-01-01')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.save }))
    await waitFor(() => expect(commands(recorded)).toHaveLength(1))
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '2',
      kind: 'Linked',
      percent: null,
      linkedGroupId: GROUP_SELLER,
      extraPoints: '3',
      reason: 'Geçmiş',
      effectiveFrom: '2027-01-01',
    })
  })

  it('SAT-D84: corrects a scheduled record in place of it and withdraws one, naming a record already in force', async () => {
    const recorded = installFetch({
      respond: (url, init) => {
        if (url.endsWith(`/groups/${GROUP_SELLER}/limit/correction`) && init?.method === 'POST') return noContent()
        if (url.endsWith(`/groups/${GROUP_SELLER}/limit/withdrawal`) && init?.method === 'POST') {
          return problem(409, { code: 'settings_discount_limit_record_in_effect' })
        }
        if (url.endsWith('/groups')) return json(groupList())
        return undefined
      },
    })
    renderAt(LIMIT_PATH)
    const user = userEvent.setup()
    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })

    await user.click(screen.getByRole('button', { name: `${LIMIT_TEXT.correctScheduled} — SATIS-DESTEK 01.11.2026` }))
    expect((screen.getByLabelText(LIMIT_TEXT.effectiveFromLabel) as HTMLInputElement).value).toBe('2026-11-01')
    const percent = screen.getByLabelText(LIMIT_TEXT.percentLabel)
    expect((percent as HTMLInputElement).value).toBe('40')
    await user.clear(percent)
    await user.type(percent, '42')
    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Yanlış girildi')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.save }))
    expect((await screen.findByRole('status')).textContent).toBe(LIMIT_TEXT.saved)
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '2',
      limitVersion: '2',
      kind: 'Fixed',
      percent: '42',
      linkedGroupId: null,
      extraPoints: null,
      reason: 'Yanlış girildi',
      effectiveFrom: null,
    })

    await user.click(await screen.findByRole('button', { name: `${LIMIT_TEXT.withdrawScheduled} — SATIS-DESTEK 01.11.2026` }))
    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Vazgeçildi')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.withdrawScheduled }))
    expect((await screen.findByRole('alert')).textContent).toContain('yürürlüğe girdi')
    expect(commands(recorded)[1].body).toEqual({ expectedVersion: '2', limitVersion: '2', reason: 'Vazgeçildi' })
    expect(commands(recorded)[1].headers['Idempotency-Key']).not.toBe(commands(recorded)[0].headers['Idempotency-Key'])
  })

  it('keeps the SAME Idempotency-Key for a retry after an unknown outcome, and a version conflict re-reads', async () => {
    let attempts = 0
    const recorded = installFetch({
      respond: (url, init) => {
        if (url.endsWith(`/groups/${GROUP_SELLER}/name`) && init?.method === 'PUT') {
          attempts += 1
          return attempts === 1 ? problem(503) : problem(409, { code: 'settings_discount_limit_version_conflict' })
        }
        if (url.endsWith('/groups')) return json(groupList())
        return undefined
      },
    })
    renderAt(LIMIT_PATH)
    const user = userEvent.setup()
    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })

    await user.click(screen.getByRole('button', { name: `${LIMIT_TEXT.rename} — SATIS-DESTEK` }))
    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Ad düzeltme')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.save }))
    expect((await screen.findByRole('alert')).textContent).toBe(LIMIT_TEXT.unknownOutcome)

    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.retrySame }))
    expect((await screen.findByRole('status')).textContent).toContain('Kayıt siz açtıktan sonra değişti')
    const [first, second] = commands(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(first.body).toEqual({ expectedVersion: '2', name: 'Satış Destek', reason: 'Ad düzeltme' })
    await waitFor(() =>
      expect(recorded.filter((request) => request.url.endsWith('/groups') && request.method === 'GET').length).toBe(2),
    )
  })
})

describe('İskonto limitleri — person → group binding', () => {
  it('writes the chosen codes to every company with the versions it read, and names the failing company', async () => {
    let saves = 0
    const recorded = installFetch({
      respond: (url, init) => {
        if (url.endsWith('/groups')) return json(groupList())
        if (url.includes('/settings/sales-discount-limits/people')) {
          return json({ items: [{ userId: PERSON, displayName: 'SENTETIK Satıcı' }], hasMore: false })
        }
        if (url.endsWith(`/bindings/${PERSON}`) && init?.method === 'PUT') {
          saves += 1
          return saves === 1
            ? problem(422, { code: 'settings_discount_limit_binding_group_not_found', companyId: COMPANY_B, groupCode: 'MUDUR' })
            : noContent()
        }
        if (url.endsWith(`/bindings/${PERSON}`)) return json(binding('1', '0'))
        return undefined
      },
    })
    renderAt(LIMIT_PATH)
    const user = userEvent.setup()
    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })

    await user.type(screen.getByLabelText(LIMIT_TEXT.searchLabel), 'Sat')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.search }))
    await user.click(await screen.findByRole('button', { name: `${LIMIT_TEXT.selectPerson} — SENTETIK Satıcı` }))
    const table = await screen.findByRole('table', { name: LIMIT_TEXT.bindingTableLabel })
    expect(within(table).getByText('GSP — Göymen Sac Profil')).toBeTruthy()
    expect(within(table).getByText(LIMIT_TEXT.noBinding)).toBeTruthy()

    expect((screen.getByRole('checkbox', { name: /SATIS-DESTEK/ }) as HTMLInputElement).checked).toBe(true)
    await user.click(screen.getByRole('checkbox', { name: /MUDUR/ }))
    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Görev değişikliği')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.saveBinding }))

    expect((await screen.findByRole('alert')).textContent).toContain('GSP — Göymen Sac Profil')
    const search = recorded.find((request) => request.url.includes('/people'))
    expect(search?.url).toContain('q=Sat')
    const [refused] = commands(recorded)
    expect(refused.body).toEqual({
      groupCodes: ['MUDUR', 'SATIS-DESTEK'],
      companies: [
        { companyId: COMPANY_A, expectedVersion: '1' },
        { companyId: COMPANY_B, expectedVersion: '0' },
      ],
      reason: 'Görev değişikliği',
    })

    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.saveBinding }))
    expect((await screen.findByRole('status')).textContent).toBe(LIMIT_TEXT.saved)
    const second = commands(recorded)[1]
    expect(second.headers['Idempotency-Key']).not.toBe(refused.headers['Idempotency-Key'])
    expect(second.headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('SAT-D116: shows a company where the person does not work as skipped and writes only to the others', async () => {
    const recorded = installFetch({
      respond: (url, init) => {
        if (url.endsWith('/groups')) return json(groupList())
        if (url.includes('/settings/sales-discount-limits/people')) {
          return json({ items: [{ userId: PERSON, displayName: 'SENTETIK Satıcı' }], hasMore: false })
        }
        if (url.endsWith(`/bindings/${PERSON}`) && init?.method === 'PUT') return noContent()
        if (url.endsWith(`/bindings/${PERSON}`)) return json(binding('1', '0', false))
        return undefined
      },
    })
    renderAt(LIMIT_PATH)
    const user = userEvent.setup()
    await screen.findByRole('table', { name: LIMIT_TEXT.groupsTableLabel })

    await user.type(screen.getByLabelText(LIMIT_TEXT.searchLabel), 'Sat')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.search }))
    await user.click(await screen.findByRole('button', { name: `${LIMIT_TEXT.selectPerson} — SENTETIK Satıcı` }))
    const table = await screen.findByRole('table', { name: LIMIT_TEXT.bindingTableLabel })
    const rows = within(table).getAllByRole('row')
    expect(within(rows[2]).getByText('GSP — Göymen Sac Profil')).toBeTruthy()
    expect(within(rows[2]).getByText(LIMIT_TEXT.skippedCompany)).toBeTruthy()

    await user.type(screen.getByLabelText(LIMIT_TEXT.reasonLabel), 'Yalnız GDC')
    await user.click(screen.getByRole('button', { name: LIMIT_TEXT.saveBinding }))
    await waitFor(() => expect(commands(recorded)).toHaveLength(1))
    expect(commands(recorded)[0].body).toEqual({
      groupCodes: ['SATIS-DESTEK'],
      companies: [{ companyId: COMPANY_A, expectedVersion: '1' }],
      reason: 'Yalnız GDC',
    })
  })
})
