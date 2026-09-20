/**
 * The shell mounts exactly the Logistics capability ids and routes that lane declares.
 *
 * Two files, two single writers: `src/logistics/capabilities.ts` belongs to the Logistics lane (SİPARİŞ),
 * `src/shell/capabilities.ts` to the shared-integration owner. A rename on one side alone would make
 * the shell quietly stop mounting a screen the server has granted.
 *
 * WHAT THIS IS NOT: backend parity or availability. The lane's own capabilities.test.ts pins its ids to the
 * backend provider; whether a Logistics screen opens is the server's snapshot (staged until the l04 suite).
 */

import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import {
  LOGISTICS_CAPABILITY,
  LOGISTICS_NAV_LABEL,
  LOGISTICS_ROUTE,
  instructionDetailRoute,
} from '../logistics/capabilities'

describe('shell ↔ Logistics lane capability ids', () => {
  it('declares the same three ids the lane does, and nothing else under logistics.', () => {
    expect(CAPABILITY.logisticsInstructionsList).toBe(LOGISTICS_CAPABILITY.instructionsList)
    expect(CAPABILITY.logisticsInstructionsCreate).toBe(LOGISTICS_CAPABILITY.instructionsCreate)
    expect(CAPABILITY.logisticsInstructionsDetail).toBe(LOGISTICS_CAPABILITY.instructionsDetail)
    const shellIds = (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('logistics.'))
    expect([...shellIds].sort()).toEqual([...(Object.values(LOGISTICS_CAPABILITY) as string[])].sort())
  })

  it('agrees with the lane on every route, value by value', () => {
    expect(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsList]).toBe(LOGISTICS_ROUTE.list)
    expect(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsCreate]).toBe(LOGISTICS_ROUTE.create)
    expect(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsDetail]).toBe(LOGISTICS_ROUTE.detail)
  })

  it('matches the lane’s own link helper, and the static create route is never an instruction id', () => {
    const instructionId = '7b1c0f2e-0000-4a00-8000-000000000002'
    const match = matchPath(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsDetail], instructionDetailRoute(instructionId))
    expect(match?.params.instructionId).toBe(instructionId)
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsList], LOGISTICS_ROUTE.create)).toBeNull()
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsList], instructionDetailRoute(instructionId))).toBeNull()
  })

  it('gives the list the lane’s nav label, and neither the create screen nor the detail one', () => {
    for (const id of Object.values(LOGISTICS_CAPABILITY)) {
      const shellId = (Object.values(CAPABILITY) as string[]).find((value) => value === id)!
      expect(CAPABILITY_NAV_LABEL[shellId as keyof typeof CAPABILITY_NAV_LABEL]).toBe(LOGISTICS_NAV_LABEL[id])
    }
    // One entry point per creation route (coordinator, 19 September 2026): the L-02 list carries its
    // own primary link to L-03, so the sidebar must not carry a second one with the same accessible
    // name — that duplication is what l04's strict-mode violation found, and a screen reader reads
    // both. The detail has no label either; an instruction is opened from a row.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.logisticsInstructionsCreate]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.logisticsInstructionsDetail]).toBe('')
  })
})
