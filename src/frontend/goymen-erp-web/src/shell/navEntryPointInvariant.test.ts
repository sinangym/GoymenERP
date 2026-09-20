/**
 * The nav list and the label table must agree, in BOTH directions, for every module.
 *
 * Why this exists rather than a per-module assertion: #363's review found that the two tests
 * guarding the L-03 rule — mine in the shell, the lane's in its own file — both asserted only that
 * the label is `''`. Neither asserted membership of `NAV_CAPABILITIES`. So re-adding the capability
 * to that list tomorrow would keep both of them green and put a link with NO ACCESSIBLE NAME on the
 * screen, which is the exact outcome the rule exists to prevent and is worse than the duplicate it
 * replaced. A gate that cannot fail on the thing it guards is not a gate.
 *
 * `AppShell` maps `NAV_CAPABILITIES` straight to nav items and reads each one's label from
 * `CAPABILITY_NAV_LABEL`, so the two rules below are exactly what "no nameless link, no orphan
 * label" means in this codebase. They hold for every module, so a future creation screen gets the
 * protection without anyone remembering to add a test.
 *
 * WHAT THIS IS NOT: the one-entry-point rule itself. Whether a list ALSO links to its create screen
 * is a fact about that lane's page, not about these two tables; the lane's own tests and the
 * per-module drift tests cover the pairing. This guards the shell's half.
 */

import { describe, expect, it } from 'vitest'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, type CapabilityId } from './capabilities'

describe('nav entry points', () => {
  it('gives every nav entry a non-empty label — no nameless link can be rendered', () => {
    const nameless = NAV_CAPABILITIES.filter((capability) => !CAPABILITY_NAV_LABEL[capability]?.trim())
    expect(nameless).toEqual([])
  })

  it('keeps every empty-labelled capability OUT of the nav list', () => {
    const emptyLabelled = (Object.values(CAPABILITY) as CapabilityId[]).filter(
      (capability) => !CAPABILITY_NAV_LABEL[capability]?.trim(),
    )
    const leaked = emptyLabelled.filter((capability) => NAV_CAPABILITIES.includes(capability))
    expect(leaked).toEqual([])
  })

  it('lists every known capability in the label table, so neither side can drift silently', () => {
    const missing = (Object.values(CAPABILITY) as CapabilityId[]).filter(
      (capability) => CAPABILITY_NAV_LABEL[capability] === undefined,
    )
    expect(missing).toEqual([])
    // And the nav list may only name capabilities the shell actually knows.
    const unknown = NAV_CAPABILITIES.filter(
      (capability) => !(Object.values(CAPABILITY) as string[]).includes(capability),
    )
    expect(unknown).toEqual([])
  })
})
