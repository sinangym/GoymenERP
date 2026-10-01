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
 * links to this screen at all" — emptying a label satisfies it either way. An empty label is a
 * NORMAL and correct choice, not a workaround: a screen opened from a list row, a card or a context
 * action carries none, which is why so many here are empty.
 *
 * Careful with the rule this explains, because an earlier version of these lines was STRICTER than
 * it (#372 review, CARİ): the rule is EXACTLY ONE entry point per creation route
 * (`docs/delivery/TEKNIK_TUZAKLAR.md` §8). A reader who takes a stricter sentence literally would
 * delete a screen's only entry point and produce the very MFG-02 condition this file exists to
 * catch. An explanation of a guard must never be stricter than the guard.
 *
 * UPDATED (CARI-NORMAL): those lines used to add "…the sidebar entry when the list does not link to
 * it, or the list's own link when it does" and cited `/cari/yeni` in the sidebar as COMPLIANT. The
 * invariant is unchanged, but that CHOICE — and therefore the example — is superseded for creation
 * routes by K5 (Sinan, 20 Eylül 2026, #66 5748258299): the single authorised "Yeni …" belongs
 * INSIDE the relevant list, and cari/ürün/sipariş are named. Cari now complies the other way: the
 * sidebar entry is gone and C-01 carries the action. The sentence is corrected rather than left
 * standing because a stale explanation that declares a violation "compliant" is not merely wrong —
 * it is a citable licence to restore the violation.
 *
 * MEASURED, and NOT this file's to enforce: no list page in the repo links to its own create route
 * (`urun/yeni` and `siparisler/yeni` appear in zero list pages), so Product and Sales still use
 * sidebar-create. A rule here forbidding that would redden two other lanes' screens today and leave
 * only an exemption list — a guard cannot land while the instances it would catch still stand, and
 * those instances are not this lane's to repair. The mechanism belongs after those modules move.
 *
 * What the file cannot do is confirm the link that makes an empty label correct, so the orphan rule's
 * message states that obligation and points at the lane's own test rather than implying a guarantee
 * this file does not provide.
 *
 * Nor is this the one-entry-point rule itself. Whether a list ALSO links to its create screen
 * is a fact about that lane's page, not about these two tables; the lane's own tests and the
 * per-module drift tests cover the pairing. This guards the shell's half.
 */

import { describe, expect, it } from 'vitest'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, type CapabilityId } from './capabilities'

describe('nav entry points', () => {
  it('publishes the Finance allocation work list through exactly one named shell entry', () => {
    expect(
      NAV_CAPABILITIES.filter((capability) => capability === CAPABILITY.financeAllocationsList),
    ).toEqual([CAPABILITY.financeAllocationsList])
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.financeAllocationsList]).toBe('Finansal Dağıtımlar')
  })

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
        'rendered. Two fixes, both legitimate. (1) The screen belongs in the sidebar: add the ' +
        'capability to NAV_CAPABILITIES. (2) The screen is deliberately opened from a list row, a ' +
        'card or a context action — the normal case for detail and create screens, which is why the ' +
        'screens reached that way carry no label: empty its label. Option 2 carries an obligation, ' +
        'not a free pass: the screen must then really be reachable from that row or list, and THIS ' +
        'file does not verify it — it reads the two tables, never the pages, so the lane that owns ' +
        'the screen has to pin that link in its own test. Emptying a label with nothing linking to ' +
        'the screen silences this rule and leaves the screen reachable only by typing its URL, ' +
        'which is the MFG-02 condition the rule exists to catch.',
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
