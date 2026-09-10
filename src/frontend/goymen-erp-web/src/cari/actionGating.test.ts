/**
 * The rule itself, pinned. These cases exist so a later screen cannot quietly pick
 * the other behaviour for its own action (UX review §8.7/64, §12/27).
 */

import { describe, expect, it } from 'vitest'
import {
  allowed,
  blocked,
  isEnabled,
  isRendered,
  notEntitled,
  reasonOf,
  type ActionGate,
} from './actionGating'

describe('Cari action gating — one rule (§8.7/64, §12/10, §12/27)', () => {
  it('an unentitled action is not rendered at all', () => {
    // Not "rendered disabled with an explanation". A disabled control plus a reason
    // confirms the feature exists and that this record is a valid target for it —
    // the ERP-000K §10.2 disclosure.
    expect(isRendered(notEntitled)).toBe(false)
    expect(isEnabled(notEntitled)).toBe(false)
    expect(reasonOf(notEntitled)).toBeNull()
  })

  it('an unentitled gate cannot carry a reason at all', () => {
    // Structural, not a convention: there is no field to put one in, so a future
    // caller cannot add "yetkiniz yok" to it without changing this type on purpose.
    expect(Object.keys(notEntitled)).toEqual(['kind'])
  })

  it('a business-rule block stays on screen, disabled, with its reason', () => {
    const gate = blocked('Bu cari bu şirkete zaten bağlı.')
    expect(isRendered(gate)).toBe(true)
    expect(isEnabled(gate)).toBe(false)
    expect(reasonOf(gate)).toBe('Bu cari bu şirkete zaten bağlı.')
  })

  it('an allowed action is rendered and interactive', () => {
    expect(isRendered(allowed)).toBe(true)
    expect(isEnabled(allowed)).toBe(true)
    expect(reasonOf(allowed)).toBeNull()
  })

  it('every gate resolves — a new kind cannot fall through as "enabled"', () => {
    const gates: readonly ActionGate[] = [allowed, notEntitled, blocked('x')]
    for (const gate of gates) {
      expect(typeof isRendered(gate)).toBe('boolean')
      expect(typeof isEnabled(gate)).toBe('boolean')
      // Enabled implies rendered; the reverse does not hold.
      if (isEnabled(gate)) expect(isRendered(gate)).toBe(true)
    }
  })
})
