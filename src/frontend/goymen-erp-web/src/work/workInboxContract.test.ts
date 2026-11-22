/** W1-A Work inbox decoder: exact members, fail-closed shapes, unknown rule codes tolerated. Synthetic data (SENTETIK). */

import { describe, expect, it } from 'vitest'
import { decodeWorkInbox, decodeWorkInboxEventRecorded } from './workInboxContract'
import {
  formatInvariantDecimal,
  formatWithUnit,
  inboxPermissionText,
  inboxRoleText,
  inboxRuleText,
  shortDocumentLabel,
} from './workInboxText'

const COMPANY = '10000000-0000-4000-8000-000000000001'
const OTHER = '10000000-0000-4000-8000-000000000002'
const ITEM = 'd1000000-0000-4000-8000-000000000001'
const KIND = 'finance.party-credit-limit.v1'
const PERMISSION = 'finance.credit-limits.approve'

const defined = { class: 'BusinessDays', state: 'Defined', limit: 2, dueAtUtc: '2026-11-04T07:00:00+00:00', isOverdue: true, policyVersion: 'work.approval-duration-business-days:1' }
const undefinedDuration = { class: 'BusinessDays', state: 'Undefined', limit: null, dueAtUtc: null, isOverdue: null, policyVersion: null }
const rule = { ruleCode: 'finance.credit-limit-change', threshold: '1000.5', actual: '1500', deviation: '499.5', unit: 'TRY' }
const reference = { role: 'party', label: 'SENTETIK Cari', recordId: 'e1000000-0000-4000-8000-000000000001', link: '/cari/e1000000-0000-4000-8000-000000000001' }
const item = (overrides: Record<string, unknown> = {}) => ({
  module: 'finance', kind: KIND, itemId: ITEM, documentLabel: 'LIM-0001', link: `/finans/kredi-limitleri?oneri=${ITEM}`,
  waitingSinceUtc: '2026-11-01T07:00:00+00:00', awaitingPermission: PERMISSION, duration: defined,
  explanation: { rules: [rule], references: [reference] }, ...overrides,
})
const source = (overrides: Record<string, unknown> = {}) => ({
  module: 'finance', kind: KIND, awaitingPermission: PERMISSION, durationClass: 'BusinessDays', state: 'Ready', hasMore: false, ...overrides,
})
const inbox = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY, view: 'awaiting-my-decision', generatedAtUtc: '2026-11-05T07:00:00Z', items: [item()], sources: [source()], ...overrides,
})

describe('decodeWorkInbox', () => {
  it('decodes a whole answer, keeping decimals as text', () => {
    const decoded = decodeWorkInbox(inbox(), COMPANY)
    expect(decoded?.items).toHaveLength(1)
    expect(decoded?.items[0].explanation.rules[0]).toEqual(rule)
    expect(decoded?.items[0].duration).toEqual(defined)
    expect(decoded?.sources[0].state).toBe('Ready')
  })

  it('tolerates an unknown rule code and role of the item module (words are the screen\'s)', () => {
    const decoded = decodeWorkInbox(inbox({ items: [item({ explanation: {
      rules: [{ ruleCode: 'finance.some-future-rule', threshold: null, actual: null, deviation: null, unit: null }],
      references: [{ role: 'future-role', label: 'SENTETIK', recordId: null, link: null }],
    } })] }), COMPANY)
    expect(decoded?.items[0].explanation.rules[0].ruleCode).toBe('finance.some-future-rule')
    expect(decoded?.items[0].explanation.references[0].role).toBe('future-role')
  })

  it('accepts Undefined / Unavailable durations only without numbers', () => {
    expect(decodeWorkInbox(inbox({ items: [item({ duration: undefinedDuration })] }), COMPANY)).not.toBeNull()
    expect(decodeWorkInbox(inbox({ items: [item({ duration: { ...undefinedDuration, state: 'Unavailable' } })] }), COMPANY)).not.toBeNull()
    expect(decodeWorkInbox(inbox({ items: [item({ duration: { ...undefinedDuration, limit: 2 } })] }), COMPANY)).toBeNull()
    expect(decodeWorkInbox(inbox({ items: [item({ duration: { ...defined, isOverdue: null } })] }), COMPANY)).toBeNull()
    expect(decodeWorkInbox(inbox({ items: [item({ duration: { ...defined, limit: 0 } })] }), COMPANY)).toBeNull()
    expect(decodeWorkInbox(inbox({ items: [item({ duration: { ...defined, state: 'Late' } })] }), COMPANY)).toBeNull()
  })

  it.each([
    ['extra root member', inbox({ extra: true })],
    ['missing root member', (() => { const { generatedAtUtc: _, ...rest } = inbox(); return rest })()],
    ['another company', inbox({ companyId: OTHER })],
    ['another view', inbox({ view: 'all' })],
    ['non-UTC instant', inbox({ generatedAtUtc: '2026-11-05T10:00:00+03:00' })],
    ['extra item member', inbox({ items: [{ ...item(), extra: 1 }] })],
    ['item kind owned by another module', inbox({ items: [item({ module: 'sales' })] })],
    ['absolute link', inbox({ items: [item({ link: 'https://example.invalid/x' })] })],
    ['blank document label', inbox({ items: [item({ documentLabel: '  ' })] })],
    ['bad permission', inbox({ items: [item({ awaitingPermission: 'Finance Approve' })] })],
    ['permission differs from its source', inbox({ items: [item({ awaitingPermission: 'finance.other.approve' })] })],
    ['duration class differs from its source', inbox({ items: [item({ duration: { ...defined, class: 'Hours' } })] })],
    ['no rule', inbox({ items: [item({ explanation: { rules: [], references: [] } })] })],
    ['nine rules', inbox({ items: [item({ explanation: { rules: Array(9).fill(rule), references: [] } })] })],
    ['thirteen references', inbox({ items: [item({ explanation: { rules: [rule], references: Array(13).fill(reference) } })] })],
    ['rule code of another module', inbox({ items: [item({ explanation: { rules: [{ ...rule, ruleCode: 'sales.price-below-floor' }], references: [] } })] })],
    ['decimal as number', inbox({ items: [item({ explanation: { rules: [{ ...rule, actual: 1500 }], references: [] } })] })],
    ['decimal with comma', inbox({ items: [item({ explanation: { rules: [{ ...rule, actual: '1500,5' }], references: [] } })] })],
    ['unit without a number', inbox({ items: [item({ explanation: { rules: [{ ruleCode: rule.ruleCode, threshold: null, actual: null, deviation: null, unit: 'kg' }], references: [] } })] })],
    ['extra rule member', inbox({ items: [item({ explanation: { rules: [{ ...rule, text: 'x' }], references: [] } })] })],
    ['extra explanation member', inbox({ items: [item({ explanation: { rules: [rule], references: [], ai: true } })] })],
    ['empty reference id', inbox({ items: [item({ explanation: { rules: [rule], references: [{ ...reference, recordId: '00000000-0000-0000-0000-000000000000' }] } })] })],
    ['bad reference link', inbox({ items: [item({ explanation: { rules: [rule], references: [{ ...reference, link: '//evil' }] } })] })],
    ['item of an unlisted source', inbox({ sources: [] })],
    ['item of an unavailable source', inbox({ sources: [source({ state: 'Unavailable' })] })],
    ['unavailable source claiming more', inbox({ items: [], sources: [source({ state: 'Unavailable', hasMore: true })] })],
    ['duplicate source', inbox({ sources: [source(), source()] })],
    ['duplicate item', inbox({ items: [item(), item()] })],
    ['enum as integer', inbox({ sources: [source({ durationClass: 1 })] })],
  ])('refuses %s', (_, body) => {
    expect(decodeWorkInbox(body, COMPANY)).toBeNull()
  })

  it('decodes the event answer only for the event that was sent', () => {
    const id = 'f1000000-0000-4000-8000-000000000001'
    expect(decodeWorkInboxEventRecorded({ clientEventId: id, replayed: false }, id)).toEqual({ replayed: false })
    expect(decodeWorkInboxEventRecorded({ clientEventId: ITEM, replayed: false }, id)).toBeNull()
    expect(decodeWorkInboxEventRecorded({ clientEventId: id, replayed: false, x: 1 }, id)).toBeNull()
  })
})

describe('inbox text', () => {
  it('formats invariant decimal text for tr-TR by string handling, without float loss', () => {
    expect(formatInvariantDecimal('1234567.125')).toBe('1.234.567,125')
    expect(formatInvariantDecimal('-1000')).toBe('-1.000')
    expect(formatInvariantDecimal('0.1')).toBe('0,1')
    expect(formatInvariantDecimal('12345678901234567890.123456789')).toBe('12.345.678.901.234.567.890,123456789')
    expect(formatWithUnit('12.5', '%')).toBe('%12,5')
    expect(formatWithUnit('1500', 'kg')).toBe('1.500 kg')
  })

  it('names known codes and leaves unknown ones to the generic text', () => {
    expect(inboxRuleText('finance.credit-limit-new')).toBe('İlk kez limit tanımlama önerisi')
    expect(inboxRuleText('finance.unknown')).toBeNull()
    expect(inboxRoleText('company-account')).toBe('Cari hesap')
    expect(inboxRoleText('nope')).toBeNull()
    expect(inboxPermissionText('finance.credit-limits.approve')).toBe('Cari kredi limiti onayı')
    expect(inboxPermissionText('x.unknown.approve')).toBe('x.unknown.approve')
  })

  it('shortens a bare GUID document label to its last 8 characters', () => {
    expect(shortDocumentLabel('d1000000-0000-4000-8000-0000abcdef12')).toBe('…abcdef12')
    expect(shortDocumentLabel('IRS-2026-0001')).toBe('IRS-2026-0001')
  })
})
