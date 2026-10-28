import { describe, expect, it } from 'vitest'
import {
  CUSTOMER_GROUP_NAME_MAX,
  CUSTOMER_GROUP_TEXT,
  checkGroupName,
  decodeAccountCustomerGroup,
  decodeCustomerGroupList,
  groupProblemCodeOf,
  selectableGroups,
} from './customerGroup'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const PARTY = '11111111-1111-1111-1111-111111111111'
const ACCOUNT = '44444444-4444-4444-4444-444444444444'
const G1 = '55555555-5555-4555-8555-555555555555'
const G2 = '66666666-6666-4666-8666-666666666666'

const group = (over: Record<string, unknown> = {}) => ({ id: G1, name: 'Toptancı', isActive: true, version: '1', ...over })

describe('SAT-D57 customer group contract', () => {
  it('decodes the company list strictly and only for the company it was asked for', () => {
    const raw = { companyId: COMPANY, items: [group(), group({ id: G2, name: 'Perakendeci', isActive: false })], viewer: { canManage: true } }
    const list = decodeCustomerGroupList(raw, COMPANY)
    expect(list?.items.map((item) => item.name)).toEqual(['Toptancı', 'Perakendeci'])
    expect(list?.viewer.canManage).toBe(true)

    expect(decodeCustomerGroupList(raw, 'bbbbbbbb-0000-0000-0000-000000000002')).toBeNull()
    expect(decodeCustomerGroupList({ ...raw, items: [group(), group()] }, COMPANY)).toBeNull()
    expect(decodeCustomerGroupList({ ...raw, items: [group({ version: 2 })] }, COMPANY)).toBeNull()
    expect(decodeCustomerGroupList({ ...raw, items: [group({ name: '   ' })] }, COMPANY)).toBeNull()
    expect(decodeCustomerGroupList({ ...raw, viewer: {} }, COMPANY)).toBeNull()
  })

  it('decodes the account view with or without a group, never a partly valid group', () => {
    const view = {
      partyId: PARTY,
      accountId: ACCOUNT,
      companyId: COMPANY,
      version: '3',
      accountIsActive: true,
      customerGroup: null,
      viewer: { canAssign: false },
    }
    expect(decodeAccountCustomerGroup(view, { partyId: PARTY, companyId: COMPANY })?.customerGroup).toBeNull()
    expect(decodeAccountCustomerGroup({ ...view, customerGroup: group() }, { partyId: PARTY, companyId: COMPANY })?.customerGroup?.id).toBe(G1)
    expect(decodeAccountCustomerGroup({ ...view, customerGroup: { id: G1 } }, { partyId: PARTY, companyId: COMPANY })).toBeNull()
    expect(decodeAccountCustomerGroup({ ...view, version: '01' }, { partyId: PARTY, companyId: COMPANY })).toBeNull()
    expect(decodeAccountCustomerGroup(view, { partyId: PARTY, companyId: 'bbbbbbbb-0000-0000-0000-000000000002' })).toBeNull()
    expect(decodeAccountCustomerGroup({ ...view, customerGroup: undefined }, { partyId: PARTY, companyId: COMPANY })).toBeNull()
  })

  it('checks a name the way the server normalizes it', () => {
    expect(checkGroupName('  İmalatçı ')).toEqual({ ok: true, value: 'İmalatçı' })
    expect(checkGroupName('   ')).toEqual({ ok: false, message: CUSTOMER_GROUP_TEXT.nameEmpty })
    expect(checkGroupName('x'.repeat(CUSTOMER_GROUP_NAME_MAX + 1))).toEqual({ ok: false, message: CUSTOMER_GROUP_TEXT.nameTooLong })
    expect(checkGroupName('Bayi\u0007')).toEqual({ ok: false, message: CUSTOMER_GROUP_TEXT.nameControl })
    expect(checkGroupName('x'.repeat(CUSTOMER_GROUP_NAME_MAX)).ok).toBe(true)
  })

  it('offers only active groups, plus the account current group even when it is passive', () => {
    const list = decodeCustomerGroupList(
      { companyId: COMPANY, items: [group(), group({ id: G2, name: 'Eski', isActive: false })], viewer: { canManage: false } },
      COMPANY,
    )!
    expect(selectableGroups(list, null).map((item) => item.id)).toEqual([G1])
    expect(selectableGroups(list, list.items[1]).map((item) => item.id)).toEqual([G1, G2])
  })

  it('reads only its own stable problem codes', () => {
    expect(groupProblemCodeOf({ code: 'customer_group_inactive' })).toBe('customer_group_inactive')
    expect(groupProblemCodeOf({ code: 'something_else' })).toBeNull()
    expect(groupProblemCodeOf(null)).toBeNull()
  })
})
