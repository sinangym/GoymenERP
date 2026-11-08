/**
 * API-09 / API-36 (PR #986) wire decoders, SENTETIK data: the synthetic bodies fit the canonical OpenAPI schemas the backend CI
 * regenerates (`contracts/openapi/goymen-erp-v1.json`), the client calls only declared routes, and the decoders refuse every
 * missing, extra, mistyped or null member, another invoice's body and a ledger whose own invariants do not hold.
 */

import { describe, expect, it } from 'vitest'
import {
  FINANCE_STAGE,
  INVOICE_FINALIZATION_KIND,
  SALES_DRAFT_INVOICES_FIELDS,
  SALES_DRAFT_INVOICE_ITEM_FIELDS,
  SALES_INVOICE_DETAIL_FIELDS,
  SALES_INVOICE_FINALIZATION_FIELDS,
  SALES_INVOICE_FINAL_INSTALMENT_FIELDS,
  SALES_INVOICE_MATCH_FIELDS,
  decodeDraftInvoices,
  decodeSalesInvoiceDetail,
  financeStage,
  invoiceFinalizationCommandPath,
  parseLastOutcome,
  salesDraftInvoicesPath,
  salesInvoicePath,
  type SalesInvoiceDetail,
} from './invoiceFinalizationContract'
import { offeredInvoiceCommand } from './invoiceFinalization'
import { SALES_PROBLEM_CODE } from './contract'
import { problemCodeText } from './salesText'
import {
  INVOICE_ID,
  OTHER_INVOICE_ID,
  admitted,
  draftInvoiceItem,
  finalizationOf,
  finalizedDetail,
  invoiceDetail,
  matchOf,
  sentWaiting,
} from './invoiceTestData'

interface Schema {
  readonly properties?: Record<string, unknown>
  readonly required?: readonly string[]
}

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
const document = JSON.parse(
  fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8'),
) as {
  readonly paths: Record<string, Record<string, {
    readonly parameters?: readonly { readonly name: string; readonly in: string }[]
    readonly requestBody?: { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> }
    readonly responses: Record<string, { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> }>
  }>>
  readonly components: { readonly schemas: Record<string, Schema> }
}

function declared(name: string): readonly string[] {
  const found = document.components.schemas[name]
  if (found === undefined) throw new Error(`OpenAPI has no schema ${name}`)
  return Object.keys(found.properties ?? {})
}

type Wire = Record<string, unknown>
const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const detailOf = (body: unknown) => decodeSalesInvoiceDetail(body, INVOICE_ID)

describe('API-36 client ↔ canonical OpenAPI', () => {
  it('reads and commands only the declared routes, with the declared headers and request body', () => {
    for (const [route, method, response] of [
      [salesInvoicePath(INVOICE_ID), 'get', 'SalesInvoiceDetailView'],
      [invoiceFinalizationCommandPath(INVOICE_FINALIZATION_KIND.finalize, INVOICE_ID), 'post', 'SalesInvoiceDetailView'],
      [invoiceFinalizationCommandPath(INVOICE_FINALIZATION_KIND.resend, INVOICE_ID), 'post', 'SalesInvoiceDetailView'],
      [salesDraftInvoicesPath(INVOICE_ID), 'get', 'SalesDraftInvoicesResponse'],
    ] as const) {
      const template = `/api/v1${route.replace(INVOICE_ID, '{id}')}`
      const operation = document.paths[template]?.[method]
      expect(operation, template).toBeDefined()
      expect(operation?.responses['200']?.content?.['application/json']?.schema?.$ref).toBe(`#/components/schemas/${response}`)
      const headerNames = operation?.parameters?.filter((parameter) => parameter.in === 'header').map((parameter) => parameter.name)
      expect(headerNames).toContain('X-Company-Id')
      if (method === 'post') {
        expect(headerNames).toContain('Idempotency-Key')
        expect(operation?.requestBody?.content?.['application/json']?.schema?.$ref).toBe('#/components/schemas/SalesDraftStateCommandRequest')
        expect(operation?.responses).toHaveProperty('409')
      }
    }
    expect(declared('SalesDraftStateCommandRequest')).toContain('expectedVersion')
  })

  it('pins every decoded member list to the schema exactly', () => {
    expect([...SALES_INVOICE_DETAIL_FIELDS].sort()).toEqual([...declared('SalesInvoiceDetailView')].sort())
    expect([...SALES_INVOICE_FINALIZATION_FIELDS].sort()).toEqual([...declared('SalesInvoiceFinalizationView')].sort())
    expect([...SALES_INVOICE_FINAL_INSTALMENT_FIELDS].sort()).toEqual([...declared('SalesInvoiceFinalInstalmentView')].sort())
    expect([...SALES_INVOICE_MATCH_FIELDS].sort()).toEqual([...declared('SalesInvoiceMatchView')].sort())
    expect([...SALES_DRAFT_INVOICES_FIELDS].sort()).toEqual([...declared('SalesDraftInvoicesResponse')].sort())
    expect([...SALES_DRAFT_INVOICE_ITEM_FIELDS].sort()).toEqual([...declared('SalesDraftInvoiceItemView')].sort())
    expect(Object.keys(invoiceDetail()).sort()).toEqual([...declared('SalesInvoiceDetailView')].sort())
    expect(Object.keys(finalizationOf()).sort()).toEqual([...declared('SalesInvoiceFinalizationView')].sort())
    expect(Object.keys(matchOf()).sort()).toEqual([...declared('SalesInvoiceMatchView')].sort())
  })
})

describe('API-36 invoice detail decoder', () => {
  it('decodes an approved and a finalized invoice, keeping every amount as the server text', () => {
    const approved = detailOf(invoiceDetail())
    expect(approved?.state).toBe('Approved')
    expect(approved?.totals.payableAmount).toBe('1180.40')
    expect(approved?.instalments.map((row) => [row.sequenceNo, row.sharePercent, row.amount])).toEqual([
      [1, '33.3333', '393.47'],
      [2, '66.6667', '786.93'],
    ])
    const finalized = detailOf(finalizedDetail(sentWaiting('refused:denied')))
    expect(finalized?.finalization?.lastOutcomeParsed).toEqual({ category: 'refused', reason: 'denied' })
    expect(finalized?.finalization?.instalments.map((row) => row.functionalAmount)).toEqual([null, '25210.77'])
    expect(detailOf(invoiceDetail({ id: INVOICE_ID.toUpperCase() }))).not.toBeNull()
  })

  it('refuses a missing, an extra, a mistyped or a nulled member', () => {
    for (const field of SALES_INVOICE_DETAIL_FIELDS) {
      const missing = clone(invoiceDetail())
      delete missing[field]
      expect(detailOf(missing), `missing ${field}`).toBeNull()
    }
    expect(detailOf({ ...invoiceDetail(), companyId: '99999999-9999-9999-9999-999999999999' })).toBeNull()
    for (const [field, value] of [
      ['version', 4], ['version', '0'], ['state', 'Open'], ['businessDate', '2026-02-30'], ['amountScale', '2'],
      ['isReplay', 'false'], ['totals', null], ['instalments', null], ['documentNo', null], ['createdAtUtc', '2026-11-01'],
    ] as const) {
      expect(detailOf(invoiceDetail({ [field]: value })), `${field}=${String(value)}`).toBeNull()
    }
    expect(detailOf(invoiceDetail({ totals: { ...invoiceDetail().totals as Wire, payableAmount: 1180.4 } }))).toBeNull()
    expect(detailOf(invoiceDetail({ instalments: [{ sequenceNo: 1, dueDate: '2026-12-01', sharePercent: '100', amount: '1180.40', extra: 1 }] }))).toBeNull()
    expect(detailOf(invoiceDetail({ match: { ...matchOf(), tolerance: null } }))).toBeNull()
    expect(detailOf(invoiceDetail({ match: matchOf({ difference: '-0.10' }) }))?.match?.difference).toBe('-0.10')
    for (const field of SALES_INVOICE_FINALIZATION_FIELDS) {
      const finalization = clone(finalizationOf())
      delete finalization[field]
      expect(detailOf(finalizedDetail(finalization)), `finalization missing ${field}`).toBeNull()
    }
    for (const field of SALES_INVOICE_FINAL_INSTALMENT_FIELDS) {
      const finalization = clone(finalizationOf())
      delete (finalization.instalments as Wire[])[0][field]
      expect(detailOf(finalizedDetail(finalization)), `instalment missing ${field}`).toBeNull()
    }
  })

  it('refuses another invoice and a ledger whose invariants do not hold', () => {
    expect(decodeSalesInvoiceDetail(invoiceDetail(), OTHER_INVOICE_ID)).toBeNull()
    // Finalized ⇔ a finalization.
    expect(detailOf(invoiceDetail({ state: 'Finalized' }))).toBeNull()
    expect(detailOf(invoiceDetail({ finalization: finalizationOf() }))).toBeNull()
    // An attempt has a time and an outcome; admitted ⇔ an admission time.
    expect(detailOf(finalizedDetail(finalizationOf({ attemptCount: 1 })))).toBeNull()
    expect(detailOf(finalizedDetail(finalizationOf({ attemptCount: 1, lastAttemptAtUtc: '2026-11-03T09:05:00Z' })))).toBeNull()
    expect(detailOf(finalizedDetail(finalizationOf({ lastOutcome: 'unavailable' })))).toBeNull()
    expect(detailOf(finalizedDetail(finalizationOf({ financeStatus: 'admitted' })))).toBeNull()
    expect(detailOf(finalizedDetail(finalizationOf({ admittedAtUtc: '2026-11-03T09:06:00Z' })))).toBeNull()
    expect(detailOf(finalizedDetail(finalizationOf({ financeStatus: 'Admitted' })))).toBeNull()
    expect(detailOf(finalizedDetail(finalizationOf({ accountingStatus: 'posted' })))).toBeNull()
    expect(detailOf(finalizedDetail(sentWaiting('Refused: Denied')))).toBeNull()
    expect(detailOf(finalizedDetail(sentWaiting('accepted')))).toBeNull()
  })
})

describe('API-09 order invoices decoder', () => {
  it('decodes the list and refuses a stray member, a mistyped count, a repeated invoice and an unknown state', () => {
    expect(decodeDraftInvoices({ items: [draftInvoiceItem()], hasMore: true })).toEqual({
      items: [{
        id: INVOICE_ID, documentNo: 'SF-2026-0007', state: 'Approved', businessDate: '2026-11-01', orderLineCount: 3,
        invoiceLineCount: 2, holdsShares: true, createdAtUtc: '2026-11-01T08:00:00Z',
      }],
      hasMore: true,
    })
    expect(decodeDraftInvoices({ items: [] })).toBeNull()
    expect(decodeDraftInvoices({ items: [], hasMore: false, nextCursor: null })).toBeNull()
    expect(decodeDraftInvoices({ items: [draftInvoiceItem({ orderLineCount: '3' })], hasMore: false })).toBeNull()
    expect(decodeDraftInvoices({ items: [draftInvoiceItem({ state: 'Open' })], hasMore: false })).toBeNull()
    expect(decodeDraftInvoices({ items: [draftInvoiceItem(), draftInvoiceItem({ id: INVOICE_ID.toUpperCase() })], hasMore: false })).toBeNull()
    for (const field of SALES_DRAFT_INVOICE_ITEM_FIELDS) {
      const item = draftInvoiceItem()
      delete item[field]
      expect(decodeDraftInvoices({ items: [item], hasMore: false }), `missing ${field}`).toBeNull()
    }
  })
})

describe('Finance stage and the offered command (server state only)', () => {
  const decoded = (body: Wire): SalesInvoiceDetail => {
    const detail = detailOf(body)
    if (detail === null) throw new Error('fixture does not decode')
    return detail
  }

  it('parses the ledger outcome text without guessing', () => {
    expect(parseLastOutcome('unavailable')).toEqual({ category: 'unavailable', reason: null })
    expect(parseLastOutcome('refused:denied')).toEqual({ category: 'refused', reason: 'denied' })
    expect(parseLastOutcome('retry:timeout')).toEqual({ category: 'retry', reason: 'timeout' })
    expect(parseLastOutcome('refused:')).toBeNull()
    expect(parseLastOutcome(`refused:${'x'.repeat(41)}`)).toBeNull()
    expect(parseLastOutcome('Admitted')).toBeNull()
  })

  it('names the stage from the ledger facts', () => {
    const stageOf = (body: Wire) => {
      const finalization = decoded(body).finalization
      if (finalization === null) throw new Error('not finalized')
      return financeStage(finalization)
    }
    expect(stageOf(finalizedDetail())).toBe(FINANCE_STAGE.pending)
    expect(stageOf(finalizedDetail(sentWaiting('unavailable')))).toBe(FINANCE_STAGE.sentWaiting)
    expect(stageOf(finalizedDetail(sentWaiting('keyconflict')))).toBe(FINANCE_STAGE.sentWaiting)
    expect(stageOf(finalizedDetail(sentWaiting('refused:denied')))).toBe(FINANCE_STAGE.refused)
    expect(stageOf(finalizedDetail(admitted()))).toBe(FINANCE_STAGE.accepted)
  })

  it('offers finalize to an approved unfinalized invoice and re-send until Finance accepts, nothing else', () => {
    expect(offeredInvoiceCommand(decoded(invoiceDetail()))).toBe(INVOICE_FINALIZATION_KIND.finalize)
    // The screen does not judge the match or the difference: the server refuses (match_required / match_difference_open).
    expect(offeredInvoiceCommand(decoded(invoiceDetail({ match: null })))).toBe(INVOICE_FINALIZATION_KIND.finalize)
    expect(offeredInvoiceCommand(decoded(invoiceDetail({ match: matchOf({ difference: '3.00' }) })))).toBe(INVOICE_FINALIZATION_KIND.finalize)
    for (const state of ['Draft', 'PendingApproval', 'Rejected', 'Cancelled']) {
      expect(offeredInvoiceCommand(decoded(invoiceDetail({ state }))), state).toBeNull()
    }
    expect(offeredInvoiceCommand(decoded(finalizedDetail()))).toBe(INVOICE_FINALIZATION_KIND.resend)
    expect(offeredInvoiceCommand(decoded(finalizedDetail(sentWaiting())))).toBe(INVOICE_FINALIZATION_KIND.resend)
    expect(offeredInvoiceCommand(decoded(finalizedDetail(admitted())))).toBeNull()
  })

  it('has a Turkish text for every new problem code', () => {
    for (const code of [
      SALES_PROBLEM_CODE.invoiceMatchRequired,
      SALES_PROBLEM_CODE.invoiceMatchDifferenceOpen,
      SALES_PROBLEM_CODE.invoiceInstalmentFunctionalInvalid,
      SALES_PROBLEM_CODE.companyContextInvalid,
      SALES_PROBLEM_CODE.correlationMetadataInvalid,
    ]) {
      expect(problemCodeText(code), code).toMatch(/\S/)
    }
  })
})
