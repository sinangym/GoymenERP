/**
 * WP4: the Sales approval kinds of the Work inbox — every kind the Sales sources return has its Turkish name, every owner rule
 * code and reference role of their explanation cards has its sentence, the owner permission ("kimde bekliyor") its label, and
 * each kind opens the inbox only under the owner's EXISTING approve control (none is new). Synthetic codes only.
 */

import { describe, expect, it } from 'vitest'
import {
  SALES_OVER_DELIVERY_APPROVE_CAPABILITY,
  SALES_POOL_CAPABILITY_ID,
  SALES_WORK_DECISION_CAPABILITY,
} from '../../sales/capabilities'
import { WORK_INBOX_CAPABILITIES, WORK_INBOX_KINDS } from '../workInboxKinds'
import {
  formatWithUnit,
  inboxKindText,
  inboxPermissionText,
  inboxRoleText,
  inboxRuleText,
} from '../workInboxText'
import { SALES_INBOX_KINDS } from './sales'

const WP4_KINDS = {
  'sales.request.v1': SALES_WORK_DECISION_CAPABILITY.request,
  'sales.invoice.v1': SALES_WORK_DECISION_CAPABILITY.invoice,
  'sales.subcontract-invoice.v1': SALES_WORK_DECISION_CAPABILITY.subcontractInvoice,
  'sales.customer-return.v1': SALES_WORK_DECISION_CAPABILITY.customerReturn,
  'sales.contract-discount.v1': SALES_WORK_DECISION_CAPABILITY.contractDiscount,
  // SAT-D476 / SAT-D475: the request-based kinds.
  'sales.quote-conversion-request.v1': SALES_WORK_DECISION_CAPABILITY.quoteConversion,
  'sales.over-delivery-request.v1': SALES_WORK_DECISION_CAPABILITY.overDeliveryRequest,
} as const

describe('WP4 Sales inbox kinds', () => {
  it('lists each remaining Sales kind once, under the owner\'s existing approve control', () => {
    for (const [kind, capability] of Object.entries(WP4_KINDS)) {
      const entries = WORK_INBOX_KINDS.filter((entry) => entry.kind === kind)
      expect(entries).toHaveLength(1)
      expect(entries[0]).toEqual({ kind, module: 'sales', capability })
      expect(SALES_INBOX_KINDS).toContain(entries[0])
    }
    expect(WORK_INBOX_CAPABILITIES).toEqual(expect.arrayContaining([
      SALES_WORK_DECISION_CAPABILITY.request, SALES_WORK_DECISION_CAPABILITY.invoice, SALES_WORK_DECISION_CAPABILITY.subcontractInvoice, SALES_WORK_DECISION_CAPABILITY.customerReturn,
    ]))
    expect(WORK_INBOX_CAPABILITIES).toContain(SALES_WORK_DECISION_CAPABILITY.overDeliveryRequest)
    expect(WORK_INBOX_CAPABILITIES).not.toContain(SALES_OVER_DELIVERY_APPROVE_CAPABILITY)
    expect(WORK_INBOX_CAPABILITIES).not.toContain(SALES_POOL_CAPABILITY_ID.quotesApproveExpired)
    expect(WORK_INBOX_CAPABILITIES).not.toContain(SALES_POOL_CAPABILITY_ID.requestsDecide)
    expect(WORK_INBOX_KINDS).toHaveLength(70)
    expect(SALES_INBOX_KINDS).toHaveLength(12)
    // S-11 is not routed: holding only the contract discount permission does not open the inbox route.
    expect(WORK_INBOX_CAPABILITIES).not.toContain('sales.contracts.discount-approve')
  })

  it('names every Sales kind in Turkish (never the technical code)', () => {
    for (const { kind } of SALES_INBOX_KINDS) expect(inboxKindText(kind)).not.toBe(kind)
    expect(inboxKindText('sales.contract-discount.v1')).toBe('Sözleşme iskontosu onayı')
  })

  it('explains every rule code and reference role the WP4 sources send', () => {
    for (const code of [
      'sales.request-approval-required', 'sales.invoice-approval-required', 'sales.subcontract-invoice-approval-required',
      'sales.customer-return-approval-required', 'sales.contract-discount-over-limit', 'sales.contract-discount-limit-undefined',
      'sales.quote-expired-conversion-requested', 'sales.over-delivery-requested',
    ]) expect(inboxRuleText(code)).toEqual(expect.any(String))
    for (const role of ['sales-request', 'sales-invoice', 'sales-subcontract-invoice', 'sales-customer-return', 'sales-contract', 'sales-quote'])
      expect(inboxRoleText(role)).toEqual(expect.any(String))
    // The original invoice of a return and a subcontract invoice's order reuse the existing roles.
    expect(inboxRoleText('sales-order-draft')).toBe('Satış siparişi')
  })

  it('labels the owners\' approve permissions and keeps the contract figures as the server sent them', () => {
    for (const permission of ['sales.requests.approve', 'sales.invoices.approve', 'sales.returns.approve', 'sales.contracts.discount.approve',
      'sales.quotes.expired-conversion.approve', 'sales.orders.over-delivery.approve'])
      expect(inboxPermissionText(permission)).not.toBe(permission)
    expect([formatWithUnit('10', '%'), formatWithUnit('12.5', '%'), formatWithUnit('2.5', '%')]).toEqual(['%10', '%12,5', '%2,5'])
  })
})


it('publishes Floor under its standalone purpose while leaving the native detail control independent',()=>{
 const floor=WORK_INBOX_KINDS.filter(x=>x.kind==='sales.price-floor-exception.v1')
 expect(floor).toEqual([{kind:'sales.price-floor-exception.v1',module:'sales',capability:SALES_WORK_DECISION_CAPABILITY.priceFloor}])
 expect(WORK_INBOX_CAPABILITIES).toContain(SALES_WORK_DECISION_CAPABILITY.priceFloor)
 expect(WORK_INBOX_CAPABILITIES).not.toContain('sales.orders.price-floor-exception.approve')
})


it('replaces the Discount inbox capability with its standalone purpose without duplicating a Sales row',()=>{
 const discount=WORK_INBOX_KINDS.filter(x=>x.kind==='sales.discount-exception.v1')
 expect(discount).toEqual([{kind:'sales.discount-exception.v1',module:'sales',capability:SALES_WORK_DECISION_CAPABILITY.discount}])
 expect(WORK_INBOX_CAPABILITIES).toContain(SALES_WORK_DECISION_CAPABILITY.discount)
 expect(WORK_INBOX_CAPABILITIES).not.toContain('sales.orders.discount-exception.approve')
 expect(WORK_INBOX_KINDS).toHaveLength(70);expect(SALES_INBOX_KINDS).toHaveLength(12)
})

it('replaces the native Risk control with exactly one standalone price-free purpose inbox row',()=>{
 expect(WORK_INBOX_KINDS.filter(x=>x.kind==='sales.risk-exception.v1')).toEqual([{kind:'sales.risk-exception.v1',module:'sales',capability:SALES_WORK_DECISION_CAPABILITY.riskException}])
 expect(WORK_INBOX_CAPABILITIES).toContain(SALES_WORK_DECISION_CAPABILITY.riskException);expect(WORK_INBOX_CAPABILITIES).not.toContain('sales.orders.risk-exception.approve')
 expect(WORK_INBOX_KINDS).toHaveLength(70);expect(SALES_INBOX_KINDS).toHaveLength(12)
})

it('adds the native remainder purpose once without granting order browsing or prices',()=>{
 expect(WORK_INBOX_KINDS.filter(x=>x.kind==='sales.remainder-change.v1')).toEqual([{kind:'sales.remainder-change.v1',module:'sales',capability:SALES_WORK_DECISION_CAPABILITY.remainderChange}])
 expect(inboxKindText('sales.remainder-change.v1')).toBe('Satır kalanı değişikliği')
 expect(inboxRuleText('sales.remainder-change-requested')).not.toBe('sales.remainder-change-requested')
 expect(inboxRoleText('sales-order-line')).toBe('Sipariş satırı')
 expect(WORK_INBOX_CAPABILITIES).toContain(SALES_WORK_DECISION_CAPABILITY.remainderChange)
})
