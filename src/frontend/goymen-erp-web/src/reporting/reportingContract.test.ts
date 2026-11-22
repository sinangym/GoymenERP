import { describe, expect, it } from 'vitest'
import { decodeCatalog, decodeSavedViews, decodeTable, problemCodeOf, type ReportColumn } from './reportingContract'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER = 'bbbbbbbb-0000-4000-8000-000000000002'
const LABEL = '1a000000-0000-4000-8000-000000000001'
const columns: ReportColumn[] = [{ id: 'labelCode', kind: 'text' }, { id: 'labelId', kind: 'identifier' }, { id: 'physicalKg', kind: 'number' }, { id: 'otif', kind: 'flag' }]
const catalog = {
  companyId: COMPANY, canBuildAuditorPackage: false,
  reports: [{ id: 'stock-aging', ownerModule: 'inventory', canExport: true,
    parameters: [{ id: 'asOf', kind: 'date', required: true, choices: [], minimum: null, maximum: null }],
    columns: [{ id: 'labelCode', kind: 'text' }] }],
}
const table = {
  companyId: COMPANY, reportId: 'stock-aging', parameters: { asOf: '2026-11-22' }, columns: columns.map((column) => column.id),
  rows: [['SENTETIK-1', LABEL, '1250.5', 'true'], [null, null, null, null]], page: 1, pageSize: 50, hasMore: false,
  summary: [{ id: 'bucketKg:0-30', value: '1250.5' }], sourceStatuses: [], generatedAtUtc: '2026-11-22T09:00:00+00:00',
}

describe('report centre decoders', () => {
  it('decodes the filtered catalogue and refuses another company, an extra key or an unknown kind', () => {
    expect(decodeCatalog(catalog, COMPANY)?.reports[0].id).toBe('stock-aging')
    expect(decodeCatalog({ ...catalog, companyId: OTHER }, COMPANY)).toBeNull()
    expect(decodeCatalog({ ...catalog, extra: 1 }, COMPANY)).toBeNull()
    expect(decodeCatalog({ ...catalog, reports: [{ ...catalog.reports[0], columns: [{ id: 'x', kind: 'money' }] }] }, COMPANY)).toBeNull()
    expect(decodeCatalog({ ...catalog, reports: [catalog.reports[0], catalog.reports[0]] }, COMPANY)).toBeNull()
  })

  it('decodes a table whose cells match their column kinds and keeps numbers as text', () => {
    const decoded = decodeTable(table, COMPANY, 'stock-aging', columns)
    expect(decoded?.rows[0][2]).toBe('1250.5')
    expect(decoded?.rows[1]).toEqual([null, null, null, null])
  })

  it.each([
    ['another company', { ...table, companyId: OTHER }],
    ['another report', { ...table, reportId: 'otif' }],
    ['a reordered column', { ...table, columns: ['labelId', 'labelCode', 'physicalKg', 'otif'] }],
    ['a non-number in a number column', { ...table, rows: [['A', LABEL, '1,5', 'true']] }],
    ['a flag that is not true/false', { ...table, rows: [['A', LABEL, '1', 'yes']] }],
    ['an empty id', { ...table, rows: [['A', '00000000-0000-0000-0000-000000000000', '1', 'true']] }],
    ['a short row', { ...table, rows: [['A', LABEL]] }],
    ['more rows than the page size', { ...table, pageSize: 1 }],
    ['a missing key', (({ summary: _summary, ...rest }) => rest)(table)],
  ])('refuses %s', (_label, raw) => {
    expect(decodeTable(raw, COMPANY, 'stock-aging', columns)).toBeNull()
  })

  it('decodes only active saved views of the asked report and company', () => {
    const view = { id: 'cccccccc-0000-4000-8000-000000000001', companyId: COMPANY, reportId: 'stock-aging', name: 'SENTETIK görünüm',
      parameters: { bucketDays: '30,60' }, columns: ['labelCode'], version: '2', isActive: true,
      createdAtUtc: '2026-11-22T09:00:00Z', updatedAtUtc: '2026-11-22T09:05:00Z' }
    expect(decodeSavedViews({ companyId: COMPANY, reportId: 'stock-aging', items: [view] }, COMPANY, 'stock-aging')?.items).toHaveLength(1)
    expect(decodeSavedViews({ companyId: COMPANY, reportId: 'stock-aging', items: [{ ...view, isActive: false }] }, COMPANY, 'stock-aging')).toBeNull()
    expect(decodeSavedViews({ companyId: COMPANY, reportId: 'stock-aging', items: [{ ...view, version: '0' }] }, COMPANY, 'stock-aging')).toBeNull()
    expect(decodeSavedViews({ companyId: OTHER, reportId: 'stock-aging', items: [] }, COMPANY, 'stock-aging')).toBeNull()
    expect(problemCodeOf({ code: 'reporting_source_not_connected' })).toBe('reporting_source_not_connected')
    expect(problemCodeOf({ title: 'x' })).toBeNull()
  })
})
