/**
 * MFG-PLAN-LIST user text, in Turkish and in its own file (the order list's texts stay in `./manufacturingText.ts`).
 * Column names are the summary's own fields; no figure, state or conversion is invented for a column.
 */

import type { PlanStatus } from './contract'

export const PLAN_STATUS_LABEL: Record<PlanStatus, string> = {
  Draft: 'Taslak',
  Approved: 'Onaylı',
}

export const PLAN_LIST_TEXT = {
  heading: 'Üretim planları',
  caption: 'Şirketin kayıtlı üretim planları, en yeniden eskiye.',
  filter: 'Durum',
  filterAll: 'Hepsi',
  plan: 'Plan (teknik referans)',
  createdAt: 'Oluşturma',
  status: 'Durum',
  approvedAt: 'Onay',
  process: 'Süreç',
  rollCount: 'Ana rulo',
  plannedInput: 'Planlanan girdi (kg)',
  target: 'Hedef',
  targetNone: 'Hedef yok',
  open: 'Planı aç',
  loadMore: 'Sonraki sayfayı yükle',
  loadingMore: 'Yükleniyor…',
  nextPageFailed: 'Sonraki sayfa okunamadı; yukarıdaki satırlar yerinde kaldı.',
  end: 'Liste sonu.',
  labelNote:
    'Plan ve rulo etiketleri ürün/etiket okumasıyla gelecek; burada teknik kimlik gösterilir. ' +
    'Onaylı bir planın emirleri Üretim emirleri listesindedir.',
  newPlan: 'Yeni üretim planı',
  orders: 'Üretim emirleri',
  plans: 'Üretim planları',
  subject: 'üretim planları',
  contextSubject: 'üretim planları için tek şirket bağlamı',
} as const
