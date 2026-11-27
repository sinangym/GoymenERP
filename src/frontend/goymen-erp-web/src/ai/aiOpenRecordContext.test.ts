import { describe, expect, it } from 'vitest'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { hasOpenRecordTool, openRecordScreen } from './aiOpenRecordContext'

const id = 'cccccccc-0000-0000-0000-000000000003'

describe('B1 exact supported record routes', () => {
  it('matches the actual native detail capability and route registry without changing native routes', () => {
    for (const [capability, parameter] of [
      [CAPABILITY.cariPartiesDetail, ':partyId'],
      [CAPABILITY.logisticsInstructionsDetail, ':instructionId'],
      [CAPABILITY.salesDraftsDetail, ':draftId'],
    ] as const) {
      const path = CAPABILITY_ROUTE[capability].replace(parameter, id)
      expect(openRecordScreen(path)?.detailCapability).toBe(capability)
    }
  })
  it.each([
    [`/cari/${id}`, 'counterparty.party.summary', 'cari.parties.detail', 'counterparty.party-summary.v1'],
    [`/lojistik/yukleme-talimatlari/${id}`, 'logistics.loading-instruction.shipment-status', 'logistics.instructions.detail', 'logistics.instruction-shipment-status.v1'],
    [`/satis/siparisler/${id}`, 'sales.order.draft-summary', 'sales.drafts.detail', 'sales.draft-summary.v1'],
  ])('binds only the native record reference for %s', (path, targetKind, detailCapability, toolId) => {
    const screen = openRecordScreen(path)
    expect(screen).toEqual({ context: { targetKind, targetId: id }, detailCapability, toolId, toolVersion: '1' })
    expect(hasOpenRecordTool(screen!, [{ toolId, version: '1', ownerModule: 'test', dataClass: 'Internal' }])).toBe(true)
    expect(hasOpenRecordTool(screen!, [{ toolId, version: '2', ownerModule: 'test', dataClass: 'Internal' }])).toBe(false)
    expect(hasOpenRecordTool(screen!, [])).toBe(false)
  })
  it.each(['/', '/cari', '/cari/yeni', `/cari/${id}/bilgiler`, `/cari/${id}/extra`,
    '/cari/00000000-0000-0000-0000-000000000000', '/cari/not-a-guid',
    '/lojistik/yukleme-talimatlari', '/lojistik/yukleme-talimatlari/yeni', `/onaylarim/counterparty.tax-identity-change.v1/${id}`,
    '/satis/siparisler', '/satis/siparisler/yeni', '/satis/siparisler/00000000-0000-0000-0000-000000000000',
    `/satis/siparisler/${id}/kontrol`, `/satis/siparisler/${id}/extra`, `/satis/siparisler/${id}\n`,
    `/cari/${id}\n`, `/lojistik/yukleme-talimatlari/${id}\r`])
  ('preserves legacy mode outside exact supported record paths: %s', path => {
    expect(openRecordScreen(path)).toBeNull()
  })
})
