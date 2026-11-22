/**
 * The sidebar's granted entries, computed from the committed capability snapshot. Moved here unchanged from
 * `AppShell.tsx` (W1-B, DALGA_PLANI_v2 1.3) so the sidebar and the command palette read ONE rule: the palette offers
 * exactly the screens the sidebar draws, never a screen the sidebar would hide (DEC-GOV-007, ERP-000K §8.1).
 */

import { hasCapability, type ShellState } from './shellState'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE } from '../product/priceLists/capabilities'
import { PRICE_LIST_TEXT } from '../product/priceLists/priceListText'
import { CAPABILITY_ROUTE } from './capabilities'
import { CAPABILITY_NAV_LABEL, NAV_GROUPS, NAV_PURPOSE_CAPABILITIES, type NavEntryId, type NavGroup } from './navigation'
import type { DrawnGroup, NavLeaf } from './activeGroup'

function leafOf(entry: NavEntryId): NavLeaf {
  // #590: the price list is drawn on its OWN capability, with its own route and heading.
  if (entry === PRICE_LIST_CAPABILITY.list) {
    return { key: entry, path: PRICE_LIST_ROUTE.list, label: PRICE_LIST_TEXT.listHeading }
  }
  return { key: entry, path: CAPABILITY_ROUTE[entry], label: CAPABILITY_NAV_LABEL[entry] }
}

/**
 * A group's granted leaves. A subheading opens a section at the entry that declares it, and the section runs to the
 * next declaring entry (KLN-D2: Ana Veriler, Sistem Yönetimi). The label is drawn above the section's FIRST GRANTED
 * entry, so an actor holding only a later entry of a section still sees which section it is in. A section with no
 * granted entry draws no label: a subheading is never drawn on its own.
 */
function grantedLeaves(group: NavGroup, state: ShellState): NavLeaf[] {
  const leaves: NavLeaf[] = []
  let pending: string | undefined
  for (const entry of group.entries) {
    const declared = group.subheadings?.[entry]
    if (declared !== undefined) pending = declared
    const purposes = NAV_PURPOSE_CAPABILITIES[entry] ?? [entry]
    if (!purposes.some((purpose) => hasCapability(state, purpose))) continue
    leaves.push({ ...leafOf(entry), subheading: pending })
    pending = undefined
  }
  return leaves
}

/** The groups this actor sees: each with only its granted entries, and none left empty. */
export function drawnGroups(state: ShellState): DrawnGroup[] {
  return NAV_GROUPS.map((group) => ({ id: group.id, label: group.label, leaves: grantedLeaves(group, state) }))
    .filter((group) => group.leaves.length > 0)
}
