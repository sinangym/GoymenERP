import { describe, expect, it } from 'vitest'
import { decodeShortageWarnings } from './shortageWarningContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const PRODUCT = '44444444-4444-4444-8444-444444444444'
const SIMILAR = '45454545-4545-4545-8545-454545454545'
const ORDER = '55555555-5555-4555-8555-555555555555'
const LINE = '66666666-6666-4666-8666-666666666666'
const LINE_2 = '67676767-6767-4767-8767-676767676767'

type Json = Record<string, unknown>

function line(overrides: Json = {}): Json {
  return {
    orderId: ORDER, documentNo: 'SENTETIK-SO-1', lineId: LINE, lineNo: 1, dueDate: '2026-11-20', openKg: '300',
    reservedKg: '0', fromStockKg: '100', fromSupplyKg: '150', uncoveredKg: '50', coverage: 'Uncovered',
    latestSupplyDate: null, ...overrides,
  }
}

function product(overrides: Json = {}): Json {
  return {
    productId: PRODUCT, productCode: 'SENTETIK-P1', productName: 'Sentetik ürün', status: 'Shortage',
    openDemandKg: '300', reservedKg: '0', netDemandKg: '300', freeStockKg: '100', openSupplyKg: '150', shortageKg: '50',
    earliestDueDate: '2026-11-20', lines: [line()], similarStock: { status: 'NotConfigured', items: [] }, ...overrides,
  }
}

function body(overrides: Json = {}): Json {
  return { companyId: COMPANY, supplyDates: 'NotAnswered', items: [product()], ...overrides }
}

describe('KLN-D42 eksik uyarısı sözleşmesi', () => {
  it('kural 1 eksik ürününü olduğu gibi okur', () => {
    const decoded = decodeShortageWarnings(body(), COMPANY)
    expect(decoded?.items[0].status).toBe('Shortage')
    expect(decoded?.items[0].shortageKg).toBe('50')
    expect(decoded?.items[0].lines[0].coverage).toBe('Uncovered')
  })

  it('boş listeyi ve sıfır kilogramı kabul eder', () => {
    expect(decodeShortageWarnings(body({ items: [] }), COMPANY)?.items).toEqual([])
    const covered = product({
      status: 'Covered', freeStockKg: '300', openSupplyKg: '0', shortageKg: '0',
      lines: [line({ fromStockKg: '300', fromSupplyKg: '0', uncoveredKg: '0', coverage: 'Covered' })],
    })
    expect(decodeShortageWarnings(body({ items: [covered] }), COMPANY)?.items[0].status).toBe('Covered')
  })

  it('başka şirketin yanıtını reddeder', () => {
    expect(decodeShortageWarnings(body(), '99999999-9999-4999-8999-999999999999')).toBeNull()
  })

  it('fazla veya eksik anahtarı, kapalı listede olmayan durumu reddeder', () => {
    expect(decodeShortageWarnings({ ...body(), extra: 1 }, COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ items: [product({ status: 'Minimum' })] }), COMPANY)).toBeNull()
    const missing = product()
    delete missing.shortageKg
    expect(decodeShortageWarnings(body({ items: [missing] }), COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ supplyDates: 'Maybe' }), COMPANY)).toBeNull()
  })

  it('kanonik olmayan kilogramı ve geçersiz tarihi reddeder', () => {
    expect(decodeShortageWarnings(body({ items: [product({ shortageKg: '50.0' })] }), COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ items: [product({ freeStockKg: '-1' })] }), COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ items: [product({ freeStockKg: '1,5' })] }), COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ items: [product({ earliestDueDate: '2026-02-30' })] }), COMPANY)).toBeNull()
  })

  it('eksik durumu ile eksik kilogramı birbirini tutmalıdır', () => {
    expect(decodeShortageWarnings(body({ items: [product({ shortageKg: '0' })] }), COMPANY)).toBeNull()
    const coveredWithShortage = product({
      status: 'Covered', lines: [line({ uncoveredKg: '0', coverage: 'Covered' })],
    })
    expect(decodeShortageWarnings(body({ items: [coveredWithShortage] }), COMPANY)).toBeNull()
  })

  it('hesaplanamayan ürün rakam ve satır taşımaz', () => {
    const unconvertible = product({
      status: 'Unconvertible', openDemandKg: null, reservedKg: null, netDemandKg: null, freeStockKg: null,
      openSupplyKg: null, shortageKg: null, lines: [],
    })
    expect(decodeShortageWarnings(body({ items: [unconvertible] }), COMPANY)?.items[0].freeStockKg).toBeNull()
    expect(decodeShortageWarnings(body({ items: [{ ...unconvertible, freeStockKg: '0' }] }), COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ items: [{ ...unconvertible, lines: [line()] }] }), COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ items: [product({ freeStockKg: null })] }), COMPANY)).toBeNull()
  })

  it('geç karşılanan satır terminden sonraki satın alma tarihini taşır; tarih yanıtlanmadıysa reddedilir', () => {
    const late = product({
      status: 'LateCovered', shortageKg: '0', openSupplyKg: '200',
      lines: [line({ fromSupplyKg: '200', uncoveredKg: '0', coverage: 'LateCovered', latestSupplyDate: '2026-11-25' })],
    })
    expect(decodeShortageWarnings(body({ supplyDates: 'Answered', items: [late] }), COMPANY)?.items[0].lines[0].coverage)
      .toBe('LateCovered')
    expect(decodeShortageWarnings(body({ supplyDates: 'NotAnswered', items: [late] }), COMPANY)).toBeNull()
    const notLate = product({
      ...late, lines: [line({ fromSupplyKg: '200', uncoveredKg: '0', coverage: 'LateCovered', latestSupplyDate: '2026-11-20' })],
    })
    expect(decodeShortageWarnings(body({ supplyDates: 'Answered', items: [notLate] }), COMPANY)).toBeNull()
  })

  it('tarih bilinmeyen karşılama satın almadan gelmelidir', () => {
    const unknown = product({
      status: 'CoverageDateUnknown', shortageKg: '0', openSupplyKg: '200',
      lines: [line({ fromSupplyKg: '200', uncoveredKg: '0', coverage: 'CoverageDateUnknown' })],
    })
    expect(decodeShortageWarnings(body({ items: [unknown] }), COMPANY)?.items[0].status).toBe('CoverageDateUnknown')
    const noSupply = product({
      ...unknown, lines: [line({ fromStockKg: '300', fromSupplyKg: '0', uncoveredKg: '0', coverage: 'CoverageDateUnknown' })],
    })
    expect(decodeShortageWarnings(body({ items: [noSupply] }), COMPANY)).toBeNull()
  })

  it('tekrarlanan satırı veya ürünü reddeder', () => {
    expect(decodeShortageWarnings(body({ items: [product({ lines: [line(), line()] })] }), COMPANY)).toBeNull()
    expect(decodeShortageWarnings(body({ items: [product(), product()] }), COMPANY)).toBeNull()
    const twoLines = product({ lines: [line(), line({ lineId: LINE_2, lineNo: 2, uncoveredKg: '0', coverage: 'Covered' })] })
    expect(decodeShortageWarnings(body({ items: [twoLines] }), COMPANY)?.items[0].lines).toHaveLength(2)
  })

  it('benzer ürün stoku yalnız yanıtlandığında kalem taşır ve ürünün kendisi olamaz', () => {
    const answered = { status: 'Answered', items: [{ productId: SIMILAR, productCode: 'SENTETIK-P2', productName: 'Benzer', freeStockKg: '12.50' }] }
    expect(decodeShortageWarnings(body({ items: [product({ similarStock: answered })] }), COMPANY)?.items[0].similarStock.items)
      .toHaveLength(1)
    expect(decodeShortageWarnings(body({ items: [product({ similarStock: { ...answered, status: 'NotConfigured' } })] }), COMPANY))
      .toBeNull()
    const self = { status: 'Answered', items: [{ ...answered.items[0], productId: PRODUCT }] }
    expect(decodeShortageWarnings(body({ items: [product({ similarStock: self })] }), COMPANY)).toBeNull()
  })
})
