import type { AiOpenRecordContext, AiToolCapability } from './aiContract'

export interface AiOpenRecordScreen {
  readonly context: AiOpenRecordContext
  readonly detailCapability: string
  readonly toolId: string
  readonly toolVersion: string
}

// Only these two owner READ families are supported. A route is a reference, never authorization or owner data.
export function openRecordScreen(pathname: string): AiOpenRecordScreen | null {
  const party = /^\/cari\/([0-9a-f-]+)$/i.exec(pathname)
  const instruction = /^\/lojistik\/yukleme-talimatlari\/([0-9a-f-]+)$/i.exec(pathname)
  const id = (party ?? instruction)?.[1]
  if (!id || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)
    || id === '00000000-0000-0000-0000-000000000000') return null
  return party ? {
    context: { targetKind: 'counterparty.party.summary', targetId: id },
    detailCapability: 'cari.parties.detail', toolId: 'counterparty.party-summary.v1', toolVersion: '1',
  } : {
    context: { targetKind: 'logistics.loading-instruction.shipment-status', targetId: id },
    detailCapability: 'logistics.instructions.detail', toolId: 'logistics.instruction-shipment-status.v1', toolVersion: '1',
  }
}

export function hasOpenRecordTool(screen: AiOpenRecordScreen, tools: readonly AiToolCapability[]): boolean {
  return tools.some(tool => tool.toolId === screen.toolId && tool.version === screen.toolVersion)
}
