import { describe, expect, it } from 'vitest'
import { isValidElement, type ReactNode } from 'react'
import { matchRoutes } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE } from '../capabilities'
import { fixtureContext } from '../fixtures'
import { drawnGroups } from '../navEntries'
import { shellRoutes } from '../router'
import { initialShellState, type ShellState } from '../shellState'
import { SEARCHABLE_LIST_CAPABILITIES } from '../../search/searchCapabilities'
import {
  PALETTE_CREATE_CAPABILITIES,
  filterCommands,
  foldTurkish,
  grantedCommands,
  searchAvailable,
  searchCommand,
  type PaletteCommand,
} from './paletteCommands'
import { PALETTE_CREATE_LABEL, PALETTE_TEXT } from './paletteText'

// SENTETIK: synthetic company and capability snapshots only.
const COMPANY = 'aaaaaaaa-0000-0000-0000-00000000000a'

function bound(capabilities: readonly string[], generation: string | null = 'sentetik-gen'): ShellState {
  return { ...initialShellState, phase: 'idle', activeContext: fixtureContext(COMPANY, generation, capabilities) }
}

const ALL = Object.values(CAPABILITY)

describe('foldTurkish', () => {
  it('compares Turkish letters and their ASCII bases equal, in either case', () => {
    expect(foldTurkish('ÜRÜN')).toBe('urun')
    expect(foldTurkish('Ürünler')).toBe(foldTurkish('urunler'))
    expect(foldTurkish('İSKONTO')).toBe('iskonto')
    expect(foldTurkish('ISKONTO')).toBe('iskonto')
    expect(foldTurkish('Çağrı Şöför')).toBe('cagri sofor')
    expect(foldTurkish('  Satış   siparişi ')).toBe('satis siparisi')
  })
})

describe('filterCommands', () => {
  const commands: PaletteCommand[] = [
    { id: 'screen:a', kind: 'screen', label: 'Cari Kartlar', context: 'Cari', path: '/cari' },
    { id: 'screen:b', kind: 'screen', label: 'Satış Siparişleri', context: 'Satış', path: '/satis' },
    { id: 'create:c', kind: 'create', label: 'Yeni ürün', context: '', path: '/urun/yeni' },
  ]

  it('returns everything for an empty text, in the given order', () => {
    expect(filterCommands(commands, '   ').map((command) => command.id)).toEqual(['screen:a', 'screen:b', 'create:c'])
  })

  it('matches every typed word, Turkish-insensitively, on the label or the work area', () => {
    expect(filterCommands(commands, 'satis sip').map((command) => command.id)).toEqual(['screen:b'])
    expect(filterCommands(commands, 'URUN').map((command) => command.id)).toEqual(['create:c'])
    expect(filterCommands(commands, 'cari').map((command) => command.id)).toEqual(['screen:a'])
    expect(filterCommands(commands, 'cari yeni')).toEqual([])
  })
})

describe('grantedCommands', () => {
  it('offers nothing while the snapshot is unresolved or unbound (fail-closed)', () => {
    expect(grantedCommands(bound(ALL, null), PALETTE_CREATE_LABEL)).toEqual([])
    expect(grantedCommands(initialShellState, PALETTE_CREATE_LABEL)).toEqual([])
    expect(grantedCommands({ ...bound(ALL), phase: 'switching' }, PALETTE_CREATE_LABEL)).toEqual([])
  })

  it('offers exactly the sidebar entries as screens, in the sidebar order', () => {
    // The palette must never offer a screen the sidebar hides, nor drop one it draws: compare id, path and label.
    const state = bound([CAPABILITY.salesDraftsList, CAPABILITY.cariPartiesList, CAPABILITY.productItemsDetail])
    const expected = drawnGroups(state).flatMap((group) => group.leaves.map((leaf) => `screen:${leaf.key}|${leaf.path}|${leaf.label}`))
    const screens = grantedCommands(state, PALETTE_CREATE_LABEL)
      .filter((command) => command.kind === 'screen')
      .map((command) => `${command.id}|${command.path}|${command.label}`)
    expect(screens).toEqual(expected)
    expect(screens).toHaveLength(2)
  })

  it('offers a create screen only for its own granted create capability', () => {
    const state = bound([CAPABILITY.cariPartiesList, CAPABILITY.salesDraftsCreate])
    const creates = grantedCommands(state, PALETTE_CREATE_LABEL).filter((command) => command.kind === 'create')
    expect(creates.map((command) => `${command.id}|${command.path}|${command.label}`)).toEqual([
      `create:${CAPABILITY.salesDraftsCreate}|/satis/siparisler/yeni|${PALETTE_CREATE_LABEL[CAPABILITY.salesDraftsCreate]}`,
    ])
  })

  it('never offers the context-bound create routes', () => {
    const ids = grantedCommands(bound(ALL), PALETTE_CREATE_LABEL).map((command) => command.id)
    expect(ids).not.toContain(`create:${CAPABILITY.financeReceiptsDraft}`)
    expect(ids).not.toContain(`create:${CAPABILITY.financeManualBankPaymentsCreate}`)
  })

  it('has unique ids and never an empty label', () => {
    const commands = grantedCommands(bound(ALL), PALETTE_CREATE_LABEL)
    expect(new Set(commands.map((command) => command.id)).size).toBe(commands.length)
    expect(commands.every((command) => command.label.trim() !== '')).toBe(true)
  })
})

/** Whether an element (or anything it wraps) is a route gate on exactly this capability. */
function gatedOn(node: ReactNode, capability: string): boolean {
  if (!isValidElement(node)) return false
  const props = node.props as { readonly capability?: unknown; readonly children?: ReactNode }
  if (props.capability === capability) return true
  const children = Array.isArray(props.children) ? props.children : [props.children]
  return children.some((child: ReactNode) => gatedOn(child, capability))
}

describe('create routes', () => {
  it('are static, carry a Yeni … label and are mounted behind their own capability', () => {
    for (const capability of PALETTE_CREATE_CAPABILITIES) {
      const path = CAPABILITY_ROUTE[capability]
      expect(path, capability).not.toContain(':')
      expect(PALETTE_CREATE_LABEL[capability].startsWith('Yeni '), capability).toBe(true)
      const matches = matchRoutes(shellRoutes, path) ?? []
      const leaf = matches[matches.length - 1]?.route
      expect(leaf?.path, `${capability} must not fall to the catch-all`).not.toBe('*')
      expect(gatedOn(leaf?.element, capability), `${capability} route gate`).toBe(true)
      // The walk is not vacuous: the same route is not read as gated on an unrelated capability.
      expect(gatedOn(leaf?.element, CAPABILITY.identityAuditList), `${capability} unrelated gate`).toBe(false)
    }
  })
})

describe('search command', () => {
  it('is offered only where the toolbar search is, and only for a valid term', () => {
    const withSearch = bound([SEARCHABLE_LIST_CAPABILITIES[0]])
    expect(searchAvailable(withSearch)).toBe(true)
    expect(searchCommand(withSearch, 'a', PALETTE_TEXT.searchFor)).toBeNull()
    expect(searchCommand(withSearch, ' çelik ', PALETTE_TEXT.searchFor)).toEqual({
      id: 'search',
      kind: 'search',
      label: PALETTE_TEXT.searchFor('çelik'),
      context: '',
      path: `/arama?q=${encodeURIComponent('çelik')}`,
    })
    const withoutSearch = bound([CAPABILITY.salesDraftsCreate])
    expect(searchAvailable(withoutSearch)).toBe(false)
    expect(searchCommand(withoutSearch, 'çelik', PALETTE_TEXT.searchFor)).toBeNull()
  })
})
