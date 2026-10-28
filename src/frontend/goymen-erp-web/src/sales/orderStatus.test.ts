/**
 * DEC-SAL-001 / API-08: the status-axes decoder reads `SalesOrderDraftStatusResponse` exactly, only as the status of the
 * detail on screen, and refuses every malformed or contradictory answer (null → the card shows no axis). The OpenAPI pin
 * holds the decoder's member lists to the canonical document the backend CI regenerates.
 */

import { describe, expect, it } from 'vitest'
import detailResponseRaw from './examples/detail-response.json?raw'
import statusResponseRaw from './examples/status-response.json?raw'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { decodeOrderStatus, salesDraftStatusPath } from './orderStatus'

type Body = Record<string, any>

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE_1 = '66666666-6666-6666-6666-666666666666'
const LINE_2 = '66666666-6666-4666-8666-666666666667'
const KG = '44444444-4444-4444-4444-444444444444'
const TON = '77777777-7777-4777-8777-777777777777'
const OTHER = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'

const APPROVED = {
  submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

/** The pinned detail example, approved at version 4, with a second line in another unit. */
function detailBody(overrides: Body = {}): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, approvalState: 'Approved', version: '4', lineCount: 2, ...overrides }
  body.approval = APPROVED
  body.lines = [
    body.lines[0],
    { ...body.lines[0], lineId: LINE_2, lineNo: 2, orderUnitId: TON, quantity: '25.5', requestedDeliveryDate: null, requestedDeliverySlices: [] },
  ]
  return body
}
const decodeDetail = (body: Body) => decodeDraftDetail(body, DRAFT) as DraftDetail

/** The pinned status example (line 1 completed with the SAT-D29 warning) plus line 2 not started. */
function status(): Body {
  const body = JSON.parse(statusResponseRaw) as Body
  body.realization.lines.push({
    lineId: LINE_2, lineNo: 2, orderUnitId: TON, ordered: '25.5', shipped: '0', delivered: '0', state: 'NotStarted',
    direction: 'Under', deviation: '25.5', band: null, warning: false, approvedOverDelivery: false, closedRemainder: null,
    approvedReturned: '0',
  })
  body.realization.state = 'Partial'
  return body
}

const detail = decodeDetail(detailBody())

/** Rev.1 back in preparation and submitted again: the last approved revision (Rev.0) is the measurement basis. */
const pendingDetail = () =>
  decodeDetail({ ...detailBody({ approvalState: 'PendingApproval', revision: 'Rev.1' }), approval: { ...APPROVED, decidedBy: null, decidedAtUtc: null } })

describe('decodeOrderStatus', () => {
  it('builds the route under the draft detail', () => {
    expect(salesDraftStatusPath('a/b')).toBe('/sales/drafts/a%2Fb/status')
  })

  it('reads every axis, the tolerance and each line verbatim, in the detail’s line order', () => {
    expect(detail).not.toBeNull()
    const body = status()
    body.realization.lines.reverse()
    body.realization.lines[0].lineId = LINE_2.toUpperCase()
    const decoded = decodeOrderStatus(body, detail)
    expect(decoded).not.toBeNull()
    expect(decoded).toMatchObject({
      draftVersion: '4', revision: 'Rev.0', lifecycle: 'Active', internalApproval: 'Approved',
      customerAcceptance: { state: 'NotRequired', acceptedRevision: null },
      source: 'NoSource', invoicing: 'NoSource', financialClosure: 'NoSource', readyToClose: false,
      invoicingGate: { gate: 'Open', priceBasisRevision: 'Rev.0' },
    })
    expect(decoded?.realization).toMatchObject({
      state: 'Partial', warning: true, basisRevision: 'Rev.0',
      tolerance: {
        profileId: 'eeeeeeee-1111-4111-8111-000000000401', profileVersion: 3, warnPercent: '1', approvalPercent: '2',
        blockPercent: '3', effectiveFrom: '2026-01-01', effectiveThrough: null, evaluatedOn: '2026-09-13',
      },
    })
    expect(decoded?.realization.lines.map((line) => [line.lineNo, line.state, line.shipped, line.deviation, line.band, line.warning]))
      .toEqual([[1, 'Completed', '1015', '15', 'Warn', true], [2, 'NotStarted', '0', '25.5', null, false]])
    expect(decoded?.realization.lines[0].approvedReturned).toBe('2.5')
  })

  it('reads an approved approval-band over-delivery (SAT-D30) and an awaiting one', () => {
    const approved = status()
    Object.assign(approved.realization.lines[0], { shipped: '1025', deviation: '25', band: 'Approval', warning: false, approvedOverDelivery: true })
    approved.realization.warning = false
    expect(decodeOrderStatus(approved, detail)?.realization.lines[0]).toMatchObject({ state: 'Completed', approvedOverDelivery: true })

    const awaiting = status()
    Object.assign(awaiting.realization.lines[0], { state: 'AwaitingApproval', shipped: '1025', deviation: '25', band: 'Approval', warning: false })
    Object.assign(awaiting.realization, { state: 'AwaitingApproval', warning: false })
    expect(decodeOrderStatus(awaiting, detail)?.realization.state).toBe('AwaitingApproval')
  })

  it('reads a force-closed warn-band shortfall as Completed with the warning (SAT-D31) on a Closed order', () => {
    const closed = decodeDetail(detailBody({ state: 'Closed' }))
    const body = status()
    body.lifecycle.state = 'Closed'
    Object.assign(body.realization.lines[0], { shipped: '990', delivered: '990', direction: 'Under', deviation: '10', closedRemainder: '10' })
    Object.assign(body.realization.lines[1], { state: 'ClosedWithRemainder', direction: 'Under', band: 'Block', closedRemainder: '25.5' })
    body.realization.state = 'ClosedWithRemainder'
    expect(decodeOrderStatus(body, closed)?.realization.lines.map((line) => [line.state, line.warning, line.closedRemainder]))
      .toEqual([['Completed', true, '10'], ['ClosedWithRemainder', false, '25.5']])
  })

  it('reads the realization measured against the last approved revision while a revision awaits re-approval', () => {
    const body = status()
    body.revision = 'Rev.1'
    body.internalApproval.state = 'PendingApproval'
    body.invoicing = { state: 'NoSource', gate: 'AwaitingReapproval', priceBasisRevision: null }
    body.realization.basisRevision = 'Rev.0'
    // The basis quantity may differ from the current (unapproved) one.
    body.realization.lines[1].ordered = '20'
    body.realization.lines[1].deviation = '20'
    const decoded = decodeOrderStatus(body, pendingDetail())
    expect(decoded?.realization.basisRevision).toBe('Rev.0')
    expect(decoded?.realization.lines[1].ordered).toBe('20')
  })

  it('reads the SAT-D103 invoicing gate bound to the detail: open, waiting for re-approval, open at the last approved after a rejection', () => {
    const waiting = status()
    Object.assign(waiting, { revision: 'Rev.1' })
    waiting.internalApproval.state = 'PendingApproval'
    waiting.realization.basisRevision = 'Rev.0'
    waiting.invoicing = { state: 'NoSource', gate: 'AwaitingReapproval', priceBasisRevision: null }
    expect(decodeOrderStatus(waiting, pendingDetail())?.invoicingGate).toEqual({ gate: 'AwaitingReapproval', priceBasisRevision: null })
    // While waiting, no price basis may be answered — not even the last approved one.
    expect(decodeOrderStatus({ ...waiting, invoicing: { state: 'NoSource', gate: 'Open', priceBasisRevision: 'Rev.0' } }, pendingDetail())).toBeNull()

    const rejectedDetail = decodeDetail({
      ...detailBody({ approvalState: 'Rejected', revision: 'Rev.3' }),
      approval: { ...APPROVED, rejectionReason: 'SENTETIK' },
    })
    const rejected = status()
    Object.assign(rejected, { revision: 'Rev.3' })
    rejected.internalApproval.state = 'Rejected'
    rejected.realization.basisRevision = 'Rev.2'
    rejected.invoicing = { state: 'NoSource', gate: 'Open', priceBasisRevision: 'Rev.2' }
    expect(decodeOrderStatus(rejected, rejectedDetail)?.invoicingGate).toEqual({ gate: 'Open', priceBasisRevision: 'Rev.2' })
    expect(decodeOrderStatus({ ...rejected, invoicing: { state: 'NoSource', gate: 'Open', priceBasisRevision: 'Rev.3' } }, rejectedDetail)).toBeNull()

    const preparing = decodeDetail({ ...detailBody({ approvalState: 'Preparing' }), approval: { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null } })
    const never = status()
    never.lifecycle.state = 'Draft'
    never.internalApproval.state = 'Preparing'
    never.invoicing = { state: 'NoSource', gate: 'NotApproved', priceBasisRevision: null }
    expect(decodeOrderStatus(never, preparing)?.invoicingGate).toEqual({ gate: 'NotApproved', priceBasisRevision: null })
  })

  it('reads the SAT-D114 gate: an approved revision the customer has not accepted keeps invoicing waiting, with no basis', () => {
    const notAccepted = decodeDetail({
      ...detailBody(),
      customerAcceptance: { required: true, acceptedRevision: null, acceptedBy: null, acceptedAtUtc: null, isCurrentRevisionAccepted: false },
    })
    const waiting = status()
    waiting.customerAcceptance = { state: 'Pending', acceptedRevision: null }
    waiting.invoicing = { state: 'NoSource', gate: 'AwaitingCustomerAcceptance', priceBasisRevision: null }
    expect(decodeOrderStatus(waiting, notAccepted)?.invoicingGate).toEqual({ gate: 'AwaitingCustomerAcceptance', priceBasisRevision: null })
    // Open at an unaccepted revision is a contradiction and refused.
    expect(decodeOrderStatus({ ...waiting, invoicing: { state: 'NoSource', gate: 'Open', priceBasisRevision: 'Rev.0' } }, notAccepted)).toBeNull()

    // Rejected after an unaccepted last approved revision: still waiting for the customer, never Open at it.
    const rejectedUnaccepted = decodeDetail({
      ...detailBody({ approvalState: 'Rejected', revision: 'Rev.2' }),
      approval: { ...APPROVED, rejectionReason: 'SENTETIK' },
      customerAcceptance: { required: true, acceptedRevision: 'Rev.0', acceptedBy: 'musteri', acceptedAtUtc: '2026-09-19T10:00:00Z', isCurrentRevisionAccepted: false },
    })
    const rejected = status()
    Object.assign(rejected, { revision: 'Rev.2' })
    rejected.internalApproval.state = 'Rejected'
    rejected.customerAcceptance = { state: 'Pending', acceptedRevision: 'Rev.0' }
    rejected.realization.basisRevision = 'Rev.1'
    rejected.invoicing = { state: 'NoSource', gate: 'AwaitingCustomerAcceptance', priceBasisRevision: null }
    expect(decodeOrderStatus(rejected, rejectedUnaccepted)?.invoicingGate.gate).toBe('AwaitingCustomerAcceptance')
    expect(decodeOrderStatus({ ...rejected, invoicing: { state: 'NoSource', gate: 'Open', priceBasisRevision: 'Rev.1' } }, rejectedUnaccepted)).toBeNull()
  })

  it('reads PendingReapproval, Unreadable and PolicyUnavailable in their one shape', () => {
    const unjudged = (state: string, basis: string | null): Body => {
      const body = status()
      Object.assign(body.realization, { state, warning: false, basisRevision: basis, tolerance: null })
      for (const line of body.realization.lines) {
        Object.assign(line, {
          state, shipped: null, delivered: null, direction: null, deviation: null, band: null, warning: false,
          approvedOverDelivery: false, closedRemainder: null, approvedReturned: null,
        })
      }
      return body
    }
    const pending = unjudged('PendingReapproval', null)
    pending.revision = 'Rev.1'
    pending.internalApproval.state = 'PendingApproval'
    pending.invoicing = { state: 'NoSource', gate: 'AwaitingReapproval', priceBasisRevision: null }
    expect(decodeOrderStatus(pending, pendingDetail())?.realization.state).toBe('PendingReapproval')
    expect(decodeOrderStatus(unjudged('Unreadable', 'Rev.0'), detail)?.realization.state).toBe('Unreadable')

    const policy = status()
    Object.assign(policy.realization, { state: 'PolicyUnavailable', warning: false, tolerance: null })
    Object.assign(policy.realization.lines[0], { state: 'PolicyUnavailable', direction: null, deviation: null, band: null, warning: false })
    expect(decodeOrderStatus(policy, detail)?.realization.lines.map((line) => line.state)).toEqual(['PolicyUnavailable', 'NotStarted'])
  })

  it.each<[string, (body: Body) => unknown]>([
    ['a non-object body', () => null],
    ['an array body', () => []],
    ['an unknown root member', (b) => { b.summary = 'Active' }],
    ['a missing root member', (b) => { delete b.readyToClose }],
    ['a numeric version', (b) => { b.draftVersion = 4 }],
    ['another version', (b) => { b.draftVersion = '5' }],
    ['another revision', (b) => { b.revision = 'Rev.1' }],
    ['a string readyToClose', (b) => { b.readyToClose = 'false' }],
    ['readyToClose beside owner-less axes (SAT-D32)', (b) => { b.readyToClose = true }],
    ['an unknown lifecycle state', (b) => { b.lifecycle.state = 'Open' }],
    ['a lifecycle that is not the stored one', (b) => { b.lifecycle.state = 'Closed' }],
    ['an extra lifecycle member', (b) => { b.lifecycle.since = '2026-09-13' }],
    ['another approval state', (b) => { b.internalApproval.state = 'Rejected' }],
    ['an unknown acceptance state', (b) => { b.customerAcceptance.state = 'Declined' }],
    ['an acceptance that is not the detail’s', (b) => { b.customerAcceptance.state = 'Accepted' }],
    ['an accepted revision the detail does not carry', (b) => { b.customerAcceptance.acceptedRevision = 'Rev.0' }],
    ['a missing accepted revision', (b) => { delete b.customerAcceptance.acceptedRevision }],
    ['an owner-less axis with a state this screen does not name', (b) => { b.source.state = 'FullySourced' }],
    ['a null invoicing axis', (b) => { b.invoicing = null }],
    ['an invoicing axis without its gate (SAT-D103)', (b) => { delete b.invoicing.gate }],
    ['an unknown invoicing gate', (b) => { b.invoicing.gate = 'Blocked' }],
    ['a waiting gate on an approved revision', (b) => { b.invoicing.gate = 'AwaitingReapproval'; b.invoicing.priceBasisRevision = null }],
    ['a price basis other than the approved revision', (b) => { b.invoicing.priceBasisRevision = 'Rev.1' }],
    ['a financial closure with a number', (b) => { b.financialClosure.amount = '0' }],
    ['an unknown realization state', (b) => { b.realization.state = 'Done' }],
    ['a realization without lines', (b) => { delete b.realization.lines }],
    ['a missing basis revision', (b) => { b.realization.basisRevision = null }],
    ['a malformed basis revision', (b) => { b.realization.basisRevision = 'Rev.-1' }],
    ['an axis warning no line carries', (b) => { b.realization.lines[0].warning = false; b.realization.lines[0].band = 'Within' }],
    ['a line warning the axis does not carry', (b) => { b.realization.warning = false }],
    ['a tolerance of another date (SAT-D29)', (b) => { b.realization.tolerance.evaluatedOn = '2026-09-14' }],
    ['a tolerance outside its window', (b) => { b.realization.tolerance.effectiveFrom = '2026-10-01' }],
    ['a tolerance that ended before the date', (b) => { b.realization.tolerance.effectiveThrough = '2026-09-12' }],
    ['a tolerance percent as a number', (b) => { b.realization.tolerance.warnPercent = 1 }],
    ['a tolerance percent with a sign', (b) => { b.realization.tolerance.blockPercent = '-3' }],
    ['a tolerance version as text', (b) => { b.realization.tolerance.profileVersion = '3' }],
    ['a zero tolerance version', (b) => { b.realization.tolerance.profileVersion = 0 }],
    ['an unsafe tolerance version', (b) => { b.realization.tolerance.profileVersion = 2 ** 53 }],
    ['a malformed tolerance profile id', (b) => { b.realization.tolerance.profileId = 'profile' }],
    ['an unknown tolerance member', (b) => { b.realization.tolerance.overridePercent = '5' }],
    ['a judged band without the tolerance', (b) => { b.realization.tolerance = null }],
    ['an unknown line member', (b) => { b.realization.lines[0].returnNo = 'IA-1' }],
    ['a missing line member', (b) => { delete b.realization.lines[0].approvedReturned }],
    ['a number for a quantity', (b) => { b.realization.lines[0].shipped = 1015 }],
    ['an exponent', (b) => { b.realization.lines[0].deviation = '1.5e1' }],
    ['a negative quantity', (b) => { b.realization.lines[0].approvedReturned = '-1' }],
    ['a comma decimal', (b) => { b.realization.lines[0].delivered = '1010,5' }],
    ['a zero ordered quantity', (b) => { b.realization.lines[0].ordered = '0' }],
    ['another ordered quantity on the current basis', (b) => { b.realization.lines[0].ordered = '999' }],
    ['an unknown line state', (b) => { b.realization.lines[0].state = 'Shipped' }],
    ['an unknown direction', (b) => { b.realization.lines[0].direction = 'Above' }],
    ['an unknown band', (b) => { b.realization.lines[0].band = 'Red' }],
    ['a warning on a line that is not Completed', (b) => { b.realization.lines[0].state = 'Partial' }],
    ['a warning outside the warn band', (b) => { b.realization.lines[0].band = 'Within' }],
    ['an approved over-delivery on an under-delivery', (b) => {
      Object.assign(b.realization.lines[0], { warning: false, approvedOverDelivery: true, direction: 'Under', band: 'Approval' })
      b.realization.warning = false
    }],
    ['a Completed warn-band line without the warning (SAT-D29)', (b) => {
      b.realization.lines[0].warning = false
      b.realization.warning = false
    }],
    ['an approved over-delivery on a line within the tolerance (SAT-D30)', (b) => {
      Object.assign(b.realization.lines[0], { band: 'Within', warning: false, approvedOverDelivery: true })
      b.realization.warning = false
    }],
    ['a Completed approval-band over-delivery not marked approved (SAT-D30: it awaits approval)', (b) => {
      Object.assign(b.realization.lines[0], { shipped: '1025', deviation: '25', band: 'Approval', warning: false, approvedOverDelivery: false })
      b.realization.warning = false
    }],
    ['lifecycle Draft beside an approved order (SAT-D32: it is Active)', (b) => { b.lifecycle.state = 'Draft' }],
    ['a NotStarted line with a band', (b) => { b.realization.lines[1].band = 'Within' }],
    ['a NotStarted line deviating by less than ordered', (b) => { b.realization.lines[1].deviation = '20' }],
    ['a NotStarted line without the delivery facts', (b) => { b.realization.lines[1].shipped = null }],
    ['a judged line without its deviation', (b) => { b.realization.lines[0].deviation = null }],
    ['an Unreadable line in a readable axis', (b) => {
      Object.assign(b.realization.lines[1], { state: 'Unreadable', shipped: null, delivered: null, direction: null, deviation: null, approvedReturned: null })
    }],
    ['a PolicyUnavailable line in a judged axis', (b) => {
      Object.assign(b.realization.lines[1], { state: 'PolicyUnavailable', direction: null, deviation: null })
    }],
    ['an Unreadable axis with readable lines', (b) => { Object.assign(b.realization, { state: 'Unreadable', warning: false, tolerance: null }) }],
    ['a PendingReapproval axis with a basis', (b) => {
      Object.assign(b.realization, { state: 'PendingReapproval', warning: false, tolerance: null })
    }],
    ['a PolicyUnavailable axis carrying a tolerance', (b) => {
      b.realization.state = 'PolicyUnavailable'
      b.realization.warning = false
      Object.assign(b.realization.lines[0], { state: 'PolicyUnavailable', direction: null, deviation: null, band: null, warning: false })
    }],
    ['a non-integer line number', (b) => { b.realization.lines[0].lineNo = 1.5 }],
    ['a string line number', (b) => { b.realization.lines[0].lineNo = '1' }],
    ['another line number', (b) => { b.realization.lines[0].lineNo = 3 }],
    ['a malformed line id', (b) => { b.realization.lines[0].lineId = 'line-1' }],
    ['another line', (b) => { b.realization.lines[0].lineId = OTHER }],
    ['a repeated line', (b) => { b.realization.lines[1] = { ...b.realization.lines[0] } }],
    ['another unit', (b) => { b.realization.lines[0].orderUnitId = TON }],
    ['a missing line', (b) => { b.realization.lines.pop() }],
    ['an extra line', (b) => { b.realization.lines.push({ ...b.realization.lines[1], lineId: OTHER, lineNo: 3 }) }],
  ])('refuses %s', (_, mutate) => {
    const body = status()
    const replaced = mutate(body)
    expect(decodeOrderStatus(replaced === undefined ? body : replaced, detail)).toBeNull()
  })

  it('reads a NotStarted deviation equal to ordered by value, whatever scale the server writes it with', () => {
    const body = status()
    body.realization.lines[1].deviation = '25.500'
    expect(decodeOrderStatus(body, detail)?.realization.lines[1].deviation).toBe('25.500')
  })

  it('keeps the unit identities case-insensitively bound to the detail', () => {
    const body = status()
    body.realization.lines[0].orderUnitId = KG.toUpperCase()
    body.realization.lines[0].lineId = LINE_1.toUpperCase()
    expect(decodeOrderStatus(body, detail)).not.toBeNull()
  })
})

// Read the canonical contract with Node's own fs (it lives outside the web package). Vitest runs from the web package.
const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const nodePath = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }

interface Schema { readonly required?: readonly string[]; readonly properties?: Record<string, Record<string, any>> }

describe('API-08 OpenAPI pin', () => {
  const document = JSON.parse(nodeFs.readFileSync(
    nodePath.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8')) as {
    readonly paths: Record<string, Record<string, { readonly operationId?: string; readonly responses?: Record<string, any> }>>
    readonly components: { readonly schemas: Record<string, Schema> }
  }
  const schema = (name: string) => document.components.schemas[name]
  const members = (name: string) => [...(schema(name).required ?? [])].sort()
  const declared = (name: string) => Object.keys(schema(name).properties ?? {}).sort()

  it('declares the read this card calls, with its 404 and 503', () => {
    const get = document.paths['/api/v1/sales/drafts/{id}/status']?.get
    expect(get?.operationId).toBe('GetSalesOrderDraftStatusV1')
    expect(get?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe('#/components/schemas/SalesOrderDraftStatusResponse')
    expect(Object.keys(get?.responses ?? {})).toEqual(expect.arrayContaining(['200', '404', '503']))
  })

  it.each([
    ['SalesOrderDraftStatusResponse', ['customerAcceptance', 'draftVersion', 'financialClosure', 'internalApproval', 'invoicing',
      'lifecycle', 'readyToClose', 'realization', 'revision', 'source']],
    ['SalesDraftStatusAxisV1', ['state']],
    ['SalesDraftInvoicingAxisV1', ['gate', 'priceBasisRevision', 'state']],
    ['SalesDraftCustomerAcceptanceAxisV1', ['acceptedRevision', 'state']],
    ['SalesDraftRealizationAxisV1', ['basisRevision', 'lines', 'state', 'tolerance', 'warning']],
    ['SalesDraftDeliveryToleranceV1', ['approvalPercent', 'blockPercent', 'effectiveFrom', 'effectiveThrough', 'evaluatedOn',
      'profileId', 'profileVersion', 'warnPercent']],
    ['SalesDraftLineRealizationV1', ['approvedOverDelivery', 'approvedReturned', 'band', 'closedRemainder', 'delivered',
      'deviation', 'direction', 'lineId', 'lineNo', 'orderUnitId', 'ordered', 'shipped', 'state', 'warning']],
  ])('%s: the decoder reads exactly the members the contract declares, all required', (name, keys) => {
    expect(members(name)).toEqual([...keys].sort())
    expect(declared(name)).toEqual([...keys].sort())
  })

  it('pins the example the component tests decode, and the wire types the decoder insists on', () => {
    const example = JSON.parse(statusResponseRaw) as Body
    const fits = (value: Body, name: string) => {
      expect(Object.keys(value).sort(), name).toEqual(declared(name))
    }
    fits(example, 'SalesOrderDraftStatusResponse')
    fits(example.lifecycle, 'SalesDraftStatusAxisV1')
    fits(example.invoicing, 'SalesDraftInvoicingAxisV1')
    fits(example.customerAcceptance, 'SalesDraftCustomerAcceptanceAxisV1')
    fits(example.realization, 'SalesDraftRealizationAxisV1')
    fits(example.realization.tolerance, 'SalesDraftDeliveryToleranceV1')
    fits(example.realization.lines[0], 'SalesDraftLineRealizationV1')
    const line = schema('SalesDraftLineRealizationV1').properties ?? {}
    expect(line.ordered.type).toBe('string')
    expect(line.shipped.type).toEqual(['null', 'string'])
    expect(line.warning.type).toBe('boolean')
    expect(line.approvedOverDelivery.type).toBe('boolean')
    const tolerance = schema('SalesDraftDeliveryToleranceV1').properties ?? {}
    expect(tolerance.warnPercent.type).toBe('string')
    expect(tolerance.profileVersion.format).toBe('int64')
    expect(schema('SalesOrderDraftStatusResponse').properties?.readyToClose.type).toBe('boolean')
  })
})
