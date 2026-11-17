import type { ReactElement } from 'react'
import { matchRoutes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { SUBCONTRACT_CAPABILITY, SUBCONTRACT_DELIVERY_ROUTE } from '../logistics/subcontractDeliveryContract'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import { logisticsSubcontractRoutes } from './logisticsSubcontractRoutes'
import { NAV_CAPABILITIES, NAV_GROUPS } from './navigation'

describe('subcontract delivery shell authority', () => {
  it('mounts the three owner screens behind their separate capabilities', () => {
    const screens = [CAPABILITY.logisticsSubcontractList, CAPABILITY.logisticsSubcontractCreate, CAPABILITY.logisticsSubcontractDetail]
    for (const capability of screens) {
      const path = CAPABILITY_ROUTE[capability].replace(':instructionId', '11111111-1111-4111-8111-111111111111')
      const route = matchRoutes(logisticsSubcontractRoutes, path)?.at(-1)?.route
      expect((route?.element as ReactElement<{ capability: string }>).props.capability).toBe(capability)
    }
    expect(logisticsSubcontractRoutes).toHaveLength(3)
    expect(CAPABILITY_ROUTE[CAPABILITY.logisticsSubcontractList]).toBe(SUBCONTRACT_DELIVERY_ROUTE)
  })

  it('registers every owner action while keeping one navigation entry', () => {
    const ids = Object.values(SUBCONTRACT_CAPABILITY)
    expect(Object.values(CAPABILITY).filter(id => id.startsWith('logistics.subcontract-deliveries.')).sort()).toEqual([...ids].sort())
    expect(NAV_CAPABILITIES.filter(id => ids.includes(id as typeof ids[number]))).toEqual([SUBCONTRACT_CAPABILITY.list])
    expect(NAV_GROUPS.filter(group => group.entries.includes(CAPABILITY.logisticsSubcontractList))).toHaveLength(1)
    for (const id of ids) expect(CAPABILITY_NAV_LABEL[id]).toBe(id === SUBCONTRACT_CAPABILITY.list ? 'Fason teslimler' : '')
  })
})
