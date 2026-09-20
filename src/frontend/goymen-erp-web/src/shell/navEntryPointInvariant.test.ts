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
 * `CAPABILITY_NAV_LABEL`, so the rules below are what "no nameless link, no orphan label" means in
 * this codebase. They hold for every module, so a future screen gets the protection without anyone
 * remembering to add a test.
 *
 * CORRECTION (#367 review, P3 carried from #363): the first two rules are CONTRAPOSITIVES of one
 * another — "every nav entry has a label" and "no empty-labelled capability is in nav" are the same
 * proposition written twice, so they always pass and fail together. This file promised two
 * guarantees and delivered one. The third rule below is the direction that was missing: a NON-empty
 * label whose capability is absent from the nav list — a label that can never be rendered, which is
 * what "orphan label" actually means.
 *
 * It was not hypothetical. `manufacturingOrdersList` had a mounted route and the label
 * 'Üretim emirleri', its drift test asserted that label, and it was absent from `NAV_CAPABILITIES`:
 * MFG-02 could only be opened by typing its URL (#368 fixed the entry; this rule stops it coming
 * back). The rule is added AFTER that fix on purpose — landing it first would have turned a gate red
 * for a defect belonging to another lane's slice, which teaches people to distrust the gate.
 *
 * WHAT THIS IS NOT, and the limit matters for the orphan rule's remedy: this file reads the two
 * TABLES, never the pages. So it cannot tell "this screen is reached from a list row" from "nothing
 * links to this screen at all" — emptying a label satisfies it either way. That is why the orphan
 * rule's message refuses to offer an empty label as an equal remedy: silencing a guard is not the
 * same as fixing what it guards.
 *
 * Nor is this the one-entry-point rule itself. Whether a list ALSO links to its create screen
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

  // REDUNDANT, and kept deliberately (#371 review, FİNANS note (a)): this is the contrapositive of
  // the rule above, so rules 1 and 3 together already give nav === named and this one cannot fail
  // alone. It stays because "no empty-labelled capability is in nav" is the form a reader looks for
  // when they are asking about a specific screen. Counting it as a third independent guarantee is
  // the mistake this comment exists to prevent: there are TWO.
  it('keeps every empty-labelled capability OUT of the nav list (contrapositive of the rule above)', () => {
    const emptyLabelled = (Object.values(CAPABILITY) as CapabilityId[]).filter(
      (capability) => !CAPABILITY_NAV_LABEL[capability]?.trim(),
    )
    const leaked = emptyLabelled.filter((capability) => NAV_CAPABILITIES.includes(capability))
    expect(leaked).toEqual([])
  })

  it('keeps no ORPHAN label: a named capability that the nav list never renders', () => {
    // The direction the first two rules cannot see. A non-empty label is a promise that the entry is
    // drawn somewhere; if its capability is not in the nav list, the promise is dead text and the
    // screen it names may be reachable only by typing a URL.
    const named = (Object.values(CAPABILITY) as CapabilityId[]).filter((capability) =>
      CAPABILITY_NAV_LABEL[capability]?.trim(),
    )
    const orphans = named.filter((capability) => !NAV_CAPABILITIES.includes(capability))
    expect(
      orphans,
      'these capabilities carry a nav label but are not in NAV_CAPABILITIES, so the label is never ' +
        'rendered. Add the capability to the nav list. Emptying the label also silences this rule, ' +
        'but it is only correct when the screen is genuinely reached from somewhere else — a list ' +
        'row, a card, a context action — and it is WRONG when nothing links to the screen: that ' +
        'leaves it reachable only by typing its URL, which is the MFG-02 condition this rule exists ' +
        'to catch. This file cannot tell the two apart, because it reads the two tables and not the ' +
        'pages; the lane that owns the screen has to show where it is reached from.',
    ).toEqual([])
    // A rule that filtered nothing would pass vacuously and mean nothing, so pin that it had input.
    expect(named.length).toBeGreaterThan(0)
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
