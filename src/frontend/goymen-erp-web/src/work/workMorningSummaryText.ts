import { COLLABORATION_TEXT as N } from './collaborationText'
import type { MorningDigestKind } from './workMorningSummaryApi'
export const MORNING_SUMMARY_TEXT = {
 heading:'Yayımlanmış sabah özetim',scope:'Yalnız bu şirkette size yayımlanmış sabah bildirim özetinin kayıtlı sayıları gösterilir.',
 empty:'Bu sabah dönemi için size yayımlanmış bildirim özeti bulunmuyor.',emptyCounts:'Yayımlanan özette gösterilecek bildirim türü bulunmuyor.',
 unavailable:'Sabah özeti şu anda bütünüyle doğrulanamıyor; kısmi sayılar gösterilmez.',
 source:'Kaynak bilgisi',slot:'Özet dönemi (UTC)',schedule:'Zamanlama kaynağı sürümü',digest:'Özet kaydı',kind:'Bildirim türü',count:'Kayıtlı bildirim sayısı',retry:'Özeti yeniden oku',
} as const
export const MORNING_KIND_TEXT:Readonly<Record<MorningDigestKind,string>>={
 'task-assigned':N.kindAssigned,'allocation-released':N.kindAllocationReleased,'purchase-supply-revived':N.kindSupplyRevived,'accounting-cost-proposal-stale':N.kindStaleCost,
}
