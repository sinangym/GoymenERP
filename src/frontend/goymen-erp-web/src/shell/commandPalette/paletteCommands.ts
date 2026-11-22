/**
 * W1-B (DALGA_PLANI_v2 1.3) — what the command palette may offer, derived ONLY from the committed server capability
 * snapshot. Nothing here grants, hides or routes anything on its own:
 *
 * - screens: exactly the sidebar's granted entries (`drawnGroups`, the same function the sidebar draws from);
 * - create screens: a fixed list of context-free "Yeni …" routes, each offered only while the snapshot carries the very
 *   capability its route gate (`CapabilityRoute`) checks — so the palette can never open a screen the gate would refuse;
 * - the global search: offered only where the toolbar search box itself is (a searchable owner list is granted).
 *
 * Every command only NAVIGATES. No command writes, submits or approves anything (AI-D25's closed list stays closed);
 * the opened screen and the backend authorize again on their own (ERP-000K §10.2).
 *
 * Create routes that need a context the palette cannot supply are deliberately absent: the receipt draft
 * (`finance.receipts.draft`, entered from a Cari account) and the manual bank payment (`finance.manual-bank-payments.create`,
 * entered with an owner reference). Opening them bare would show a screen that cannot be used.
 */

import { capabilityFailClosed, hasCapability, type ShellState } from '../shellState'
import { CAPABILITY, CAPABILITY_ROUTE, type CapabilityId } from '../capabilities'
import { drawnGroups } from '../navEntries'
import { SEARCHABLE_LIST_CAPABILITIES, searchPath } from '../../search/searchCapabilities'
import { normalizeTerm } from '../../search/searchContract'

/** The create screens the palette may open, in offered order. Every route is static (no `:param`) and context-free. */
export const PALETTE_CREATE_CAPABILITIES = [
  CAPABILITY.cariPartiesCreate,
  CAPABILITY.productItemsCreate,
  CAPABILITY.salesDraftsCreate,
  CAPABILITY.salesCustomerReturnsCreate,
  CAPABILITY.purchasingRequestsCreate,
  CAPABILITY.purchasingOrdersCreate,
  CAPABILITY.purchasingServiceOrdersCreate,
  CAPABILITY.manufacturingPlansCreate,
  CAPABILITY.logisticsInstructionsCreate,
  CAPABILITY.logisticsSubcontractCreate,
  CAPABILITY.financeBankAccountsPropose,
  CAPABILITY.accountingPeriodsPropose,
  CAPABILITY.accountingNormalDraftCreate,
] as const satisfies readonly CapabilityId[]

export type PaletteCreateCapability = (typeof PALETTE_CREATE_CAPABILITIES)[number]

export type PaletteCommandKind = 'screen' | 'create' | 'search'

export interface PaletteCommand {
  /** Stable id: `screen:<entry>`, `create:<capability>` or `search`. Only screen/create ids are ever remembered. */
  readonly id: string
  readonly kind: PaletteCommandKind
  readonly label: string
  /** The work area's label, read after the label and searched too. Empty for create and search. */
  readonly context: string
  readonly path: string
}

/** Screen and create commands this actor may open right now; empty whenever the snapshot is unresolved (fail-closed). */
export function grantedCommands(state: ShellState, createLabel: Readonly<Record<PaletteCreateCapability, string>>): PaletteCommand[] {
  if (capabilityFailClosed(state)) return []
  const commands: PaletteCommand[] = []
  const seen = new Set<string>()
  for (const group of drawnGroups(state)) {
    for (const leaf of group.leaves) {
      const id = `screen:${leaf.key}`
      if (seen.has(id) || leaf.label === '') continue
      seen.add(id)
      commands.push({ id, kind: 'screen', label: leaf.label, context: group.label, path: leaf.path })
    }
  }
  for (const capability of PALETTE_CREATE_CAPABILITIES) {
    if (!hasCapability(state, capability)) continue
    commands.push({ id: `create:${capability}`, kind: 'create', label: createLabel[capability], context: '', path: CAPABILITY_ROUTE[capability] })
  }
  return commands
}

/** Whether the global search may be offered: the same rule the toolbar search box renders on. */
export function searchAvailable(state: ShellState): boolean {
  return SEARCHABLE_LIST_CAPABILITIES.some((capability) => hasCapability(state, capability))
}

/** The search command for a typed text, or null when the text is not a valid search term or search is not granted. */
export function searchCommand(state: ShellState, raw: string, label: (term: string) => string): PaletteCommand | null {
  if (!searchAvailable(state)) return null
  const term = normalizeTerm(raw)
  if (term === null) return null
  return { id: 'search', kind: 'search', label: label(term), context: '', path: searchPath(term) }
}

const TURKISH_FOLD: Readonly<Record<string, string>> = { ç: 'c', ğ: 'g', ı: 'i', ö: 'o', ş: 's', ü: 'u', â: 'a', î: 'i', û: 'u' }

/**
 * Turkish-insensitive comparison form: Turkish lower case first (so `I` → `ı` and `İ` → `i`), then every Turkish letter
 * folded to its ASCII base, then any remaining combining mark dropped. "ÜRÜN", "urun" and "Ürün" compare equal, and so do
 * "ISKONTO", "ıskonto" and "iskonto".
 */
export function foldTurkish(text: string): string {
  return text
    .toLocaleLowerCase('tr-TR')
    .replace(/[çğıöşüâîû]/g, (letter) => TURKISH_FOLD[letter] ?? letter)
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

/** Commands whose label or work area contains every typed word (Turkish-insensitive); all of them for an empty text. */
export function filterCommands(commands: readonly PaletteCommand[], raw: string): PaletteCommand[] {
  const words = foldTurkish(raw).split(' ').filter((word) => word !== '')
  if (words.length === 0) return [...commands]
  return commands.filter((command) => {
    const haystack = foldTurkish(`${command.label} ${command.context}`)
    return words.every((word) => haystack.includes(word))
  })
}
