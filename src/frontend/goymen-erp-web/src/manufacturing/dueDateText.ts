/**
 * F-010a (URT-D29) plan termin text, in Turkish and in its own file. The termin is entered by hand: the system never
 * computes it from line speed, a calendar or capacity, and these words never suggest it does.
 */

export const DUE_DATE_TEXT = {
  label: 'Termin',
  none: 'Termin girilmedi',
  revision: 'termin revizyonu',
  manualNote: 'Termin elle girilir; hat hızından veya takvimden hesaplanmaz.',
  inputLabel: 'Termin tarihi',
  inputHint: 'Gün olarak girin (saat yok). Boş bırakıp kaydederseniz termin kaldırılır.',
  invalid: 'Geçerli bir takvim günü girin.',
  unchanged: 'Girilen termin plandaki terminle aynı.',
  edit: 'Termini değiştir',
  save: 'Termini kaydet',
  saving: 'Termin kaydediliyor…',
  remove: 'Termini kaldır',
  dismiss: 'Vazgeç',
  cancelledReadOnly: 'İptal edilmiş planın termini değiştirilemez.',
  setRefreshed: 'Termin kaydedildi ve güncel plan yeniden okundu.',
  clearedRefreshed: 'Termin kaldırıldı ve güncel plan yeniden okundu.',
  refreshPending: 'Termin değişikliği uygulandı; güncel plan henüz yeniden okunamadı.',
  unknown: 'Termin değişikliğinin sonucu bilinmiyor. Aynı istek ve anahtarla yeniden gönderin.',
} as const

/** The termin change's refusal, in the user's terms. */
export function dueDateFailureText(status: number, code: string | null): string {
  if (status === 401 || status === 403) return 'Termin değiştirme yetkiniz artık geçerli değil.'
  if (status === 404) return 'Plan bu şirket bağlamında bulunamadı.'
  if (status === 409 && code === 'manufacturing_plan_due_date_changed') {
    return 'Planın termini siz okuduktan sonra başka bir işlemle değişti; termin kaydedilmedi.'
  }
  if (status === 409 && code === 'manufacturing_version_conflict') {
    return 'Plan siz okuduktan sonra değişti; termin kaydedilmedi.'
  }
  if (status === 409) return `Termin kaydedilmedi (${code ?? 'conflict'}).`
  if (status === 422 && code === 'manufacturing_plan_due_date_order_closed') {
    return 'Planın üretim emri kapanmış; termin artık değiştirilemez.'
  }
  if (status === 422 && code === 'manufacturing_plan_cancelled') {
    return 'Plan iptal edilmiş; termin değiştirilemez.'
  }
  if (status === 422 && code === 'manufacturing_plan_due_date_no_change') {
    return 'İstek plandaki termini değiştirmiyor; kayıt yapılmadı.'
  }
  if (status === 400) return 'Termin veya sürüm sunucu tarafından kabul edilmedi.'
  if (status === 422) return `Termin iş kuralı tarafından reddedildi (${code ?? 'rule_refusal'}).`
  return `Termin kaydedilmedi (${code ?? status}).`
}
