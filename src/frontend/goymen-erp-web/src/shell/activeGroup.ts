/**
 * The sidebar's drawn shape and the active-group rule, kept out of `AppShell.tsx` so the component file exports
 * components only (fast refresh) and the rule can be tested on synthetic groups (`activeGroup.test.ts`).
 */
import { NAV_PATH_OWNERS, type NavEntryId, type NavGroupId } from './navigation'

export interface NavLeaf {
  readonly key: NavEntryId
  readonly path: string
  readonly label: string
  /** NAV-OPENING-01 / KLN-D2: the section label drawn above this entry, when it is its section's first granted entry. */
  readonly subheading?: string | undefined
}

export interface DrawnGroup {
  readonly id: NavGroupId
  readonly label: string
  readonly leaves: readonly NavLeaf[]
}

const firstSegment = (path: string) => path.split('/')[1] ?? ''

/**
 * The work area the open page belongs to: the group of the longest entry path the page is or lies
 * under (`/satis/siparisler/yeni` is Satış; `/stok/acilis/yeni` is the opening-stock entry's group). A page
 * under no entry first asks `NAV_PATH_OWNERS`: a declared prefix belongs to the group that draws its owner
 * ENTRY (a label card under `/stok` is Stok & Lojistik's while the stock positions are granted) and to no group
 * when that entry is not drawn — never to another group that merely shares the segment. Any other page (a
 * plan) falls back to its first path segment, but only when exactly one group owns that segment.
 */
export function activeGroupOf(groups: readonly DrawnGroup[], pathname: string): NavGroupId | null {
  let best: { readonly id: NavGroupId; readonly length: number } | null = null
  for (const group of groups) {
    for (const leaf of group.leaves) {
      const under = pathname === leaf.path || pathname.startsWith(`${leaf.path}/`)
      if (under && (best === null || leaf.path.length > best.length)) {
        best = { id: group.id, length: leaf.path.length }
      }
    }
  }
  if (best) return best.id
  const declared = NAV_PATH_OWNERS.find((owner) => pathname === owner.prefix || pathname.startsWith(`${owner.prefix}/`))
  if (declared) {
    return groups.find((group) => group.leaves.some((leaf) => leaf.key === declared.entry))?.id ?? null
  }
  const segment = firstSegment(pathname)
  if (!segment) return null
  const owners = groups.filter((group) => group.leaves.some((leaf) => firstSegment(leaf.path) === segment))
  return owners.length === 1 ? owners[0].id : null
}
