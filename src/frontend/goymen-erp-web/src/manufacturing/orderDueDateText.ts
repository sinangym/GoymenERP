/**
 * URT-D66 order termin text, in Turkish and in its own file. The order opens with the plan's termin; a change here moves
 * the order's termin only, never the plan's. "Bugün" is the server's Europe/Istanbul business day: nothing on this screen
 * computes or compares a day, the server refuses a past one.
 */

import { ORDER_DUE_DATE_CHANGED, type FactFailure } from './commandApi'
import { failureText } from './manufacturingText'

export const ORDER_DUE_DATE_TEXT = {
  heading: 'Emir termini',
  label: 'Emir termini',
  planLabel: 'Plan termini',
  none: 'Termin yok',
  revision: 'termin revizyonu',
  differsFromPlan: 'Emir termini plandan farklı',
  help: 'Emir açılırken planın termini kopyalanır. Burada yapılan değişiklik yalnız emrin terminini değiştirir; planın termini aynı kalır.',
  inputLabel: 'Yeni emir termini',
  inputHint: 'Gün olarak girin (saat yok). Bugünkü iş gününden (İstanbul) önceki bir gün sunucu tarafından reddedilir.',
  invalid: 'Geçerli bir takvim günü girin.',
  unchanged: 'Girilen termin emrin termini ile aynı.',
  edit: 'Emir terminini değiştir',
  save: 'Emir terminini kaydet',
  saving: 'Emir termini kaydediliyor…',
  dismiss: 'Vazgeç',
  resend: 'Aynı isteği yeniden gönder',
  closedReadOnly: 'Kapalı emrin termini değiştirilemez.',
  appliedTitle: 'Emir termini kaydedildi',
  appliedDetail: 'Emir yeniden okundu; planın termini değişmedi.',
  replayedTitle: 'Bu değişiklik daha önce kaydedilmişti',
  replayedDetail: 'Sunucu ilk cevabını tekrarladı; ikinci bir değişiklik yapılmadı. Emir yeniden okundu.',
  refusedTitle: 'Emir termini kaydedilmedi',
  unknownTitle: 'Emir termini değişikliğinin sonucu bilinmiyor',
  unknownHelp: 'Aynı isteği aynı işlem anahtarıyla yeniden gönderin: uygulandıysa ikinci bir değişiklik olmaz.',
} as const

/** The order termin change's refusal, in the user's terms; every other code keeps its one meaning from `failureText`. */
export function orderDueDateFailureText(failure: FactFailure): string {
  switch (failure.kind) {
    case 'forbidden':
      return 'Emir terminini değiştirme yetkiniz yok.'
    case 'not-found':
      return 'Emir bu şirket bağlamında bulunamadı.'
    case 'validation':
      return 'Termin veya termin revizyonu sunucu tarafından kabul edilmedi.'
    case 'concurrent-change':
      return 'Aynı anda başka bir kayıt işlendi; termin kaydedilmedi. Emir yeniden okundu; yeniden deneyin.'
    case 'rule':
      if (failure.code === ORDER_DUE_DATE_CHANGED) {
        return 'Emrin termini siz okuduktan sonra başka bir işlemle değişti; termin kaydedilmedi. Emir yeniden okundu.'
      }
      if (failure.code === 'manufacturing_due_date_in_past') {
        return 'Termin bugünkü iş gününden (İstanbul) önce olamaz; termin kaydedilmedi.'
      }
      if (failure.code === 'manufacturing_order_due_date_order_closed') {
        return 'Emir kapanmış; termini artık değiştirilemez.'
      }
      if (failure.code === 'manufacturing_order_due_date_no_change') {
        return 'İstek emrin terminini değiştirmiyor; kayıt yapılmadı.'
      }
      return failureText(failure)
    default:
      return failureText(failure)
  }
}
