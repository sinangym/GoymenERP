import { isValidElement } from 'react'
import { describe, expect, it } from 'vitest'
import { CapabilityRoute } from '../../shell/CapabilityRoute'
import { productRoutes } from '../productRoutes'
import { PAINT_MEMBERSHIP_CAPABILITY } from './contract'
import { paintMembershipRoutes } from './paintMembershipRoutes'

describe('paint membership module mount', () => {
  it('mounts once through the module-owned route table and its own shared capability gate', () => {
    const mounted = productRoutes.filter((route) => route.path === 'boya-urunleri')
    expect(mounted).toHaveLength(1)
    expect(mounted[0]).toBe(paintMembershipRoutes[0])
    const element = mounted[0].element
    expect(isValidElement(element) && element.type === CapabilityRoute).toBe(true)
    if (!isValidElement<{ capability: string }>(element)) throw new Error('missing synthetic route element')
    expect(element.props.capability).toBe(PAINT_MEMBERSHIP_CAPABILITY)
    expect(element.props.capability).toBe('paint-memberships.manage')
  })
})
