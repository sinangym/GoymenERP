import { describe, expect, it } from 'vitest'
import { RECENT_LIMIT, decodeRecent, readRecent, recentKey, rememberRecent, type StorageAccessor } from './recentScreens'

// SENTETIK: synthetic actor, company and command ids only.
const ACTOR = '11111111-1111-1111-1111-11111111111a'
const COMPANY = 'aaaaaaaa-0000-0000-0000-00000000000a'
const KEY = recentKey(3, ACTOR, COMPANY)

function memory(): { accessor: StorageAccessor; items: Map<string, string> } {
  const items = new Map<string, string>()
  return {
    items,
    accessor: () => ({
      getItem: (key: string) => items.get(key) ?? null,
      setItem: (key: string, value: string) => void items.set(key, value),
    }),
  }
}

const refusing: StorageAccessor = () => {
  throw new Error('SecurityError: storage blocked (SENTETIK)')
}

describe('recentKey', () => {
  it('binds the list to the session epoch, the actor and the company', () => {
    expect(KEY).toBe(`goymen.shell.recent-screens.v1:3:${ACTOR}:${COMPANY}`)
    expect(recentKey(4, ACTOR, COMPANY)).not.toBe(KEY)
    expect(recentKey(3, null, COMPANY)).toBeNull()
    expect(recentKey(3, ACTOR, null)).toBeNull()
  })
})

describe('decodeRecent', () => {
  it('accepts an absent value as an empty list and a well-formed one as it is', () => {
    expect(decodeRecent(null)).toEqual([])
    expect(decodeRecent('["screen:cari.parties.list","create:sales.drafts.create"]')).toEqual([
      'screen:cari.parties.list',
      'create:sales.drafts.create',
    ])
  })

  it('rejects anything else as a whole (fail-closed)', () => {
    for (const raw of [
      'not json',
      '{"0":"screen:a"}',
      '"screen:a"',
      '[1]',
      '[null]',
      '["search"]',
      '["screen:a","screen:a"]',
      '["/cari"]',
      '["screen:a?q=gizli"]',
      '["screen:ÇARI"]',
      JSON.stringify(Array.from({ length: RECENT_LIMIT + 1 }, (_, index) => `screen:x${index}`)),
    ]) {
      expect(decodeRecent(raw), raw).toBeNull()
    }
  })
})

describe('readRecent / rememberRecent', () => {
  it('puts the newest first, drops the older copy and keeps ten', () => {
    const { accessor } = memory()
    for (let index = 0; index < 12; index += 1) rememberRecent(accessor, KEY, `screen:s${index}`)
    rememberRecent(accessor, KEY, 'screen:s5')
    const list = readRecent(accessor, KEY)
    expect(list).toHaveLength(RECENT_LIMIT)
    expect(list[0]).toBe('screen:s5')
    expect(list.filter((id) => id === 'screen:s5')).toHaveLength(1)
    expect(list).not.toContain('screen:s0')
  })

  it('reads a tampered value as empty and overwrites it with a clean list', () => {
    const { accessor, items } = memory()
    items.set(KEY ?? '', '["screen:a", {"x":1}]')
    expect(readRecent(accessor, KEY)).toEqual([])
    rememberRecent(accessor, KEY, 'screen:b')
    expect(items.get(KEY ?? '')).toBe('["screen:b"]')
  })

  it('is silent and empty when the host refuses storage', () => {
    expect(readRecent(refusing, KEY)).toEqual([])
    expect(() => rememberRecent(refusing, KEY, 'screen:a')).not.toThrow()
  })

  it('writes nothing without a key or for an id it would not read back', () => {
    const { accessor, items } = memory()
    expect(rememberRecent(accessor, null, 'screen:a')).toEqual([])
    expect(rememberRecent(accessor, KEY, 'search')).toEqual([])
    expect(items.size).toBe(0)
  })
})
