/**
 * Every user-facing string of the Manufacturing order screen, in one place.
 *
 * Class and position names are DEC-PRDCT-006's own words (KILITLI): iyi ürün, ikinci
 * kalite, geri kazanılabilir artık, hurda, proses kaybı, WIP. The unexplained difference
 * is "açıklanamayan fark" and is never called fire (plan v2 §7.3).
 *
 * Refusals are chosen by the server's stable CODE; a code not listed here is shown as a
 * generic refusal WITH the code, never guessed into a nearby meaning.
 */

import type { FactFailure, FactUnknown } from './commandApi'
import type { FactKind, OrderStatus, OutputClass } from './contract'

export const OUTPUT_CLASS_LABEL: Record<OutputClass, string> = {
  GoodProduct: 'İyi ürün',
  SecondQuality: 'İkinci kalite',
  UsableRemnant: 'Geri kazanılabilir artık',
  Scrap: 'Hurda',
  ProcessLoss: 'Proses kaybı',
}

export const ORDER_STATUS_LABEL: Record<OrderStatus, string> = {
  Open: 'Açık',
  Closed: 'Kapalı',
}

export const TARGET_BASIS_LABEL: Record<string, string> = {
  InputMass: 'Girdi kütlesi',
  GoodOutputMass: 'İyi ürün kütlesi',
}

export const FACT_KIND_LABEL: Record<FactKind, string> = {
  'input-issues': 'Girdi çıkışı',
  'input-returns': 'Kullanılmayan girdi iadesi',
  'wip-measurements': 'WIP ölçümü',
  outputs: 'Çıktı bildirimi',
}

export const FACT_KIND_HELP: Record<FactKind, string> = {
  'input-issues': 'Ana rulodan üretime verilen kütle.',
  'input-returns': 'Çıkıp kullanılmayan ve geri dönen kütle; çıkandan fazla olamaz.',
  'wip-measurements': 'Rulonun üzerindeki yarı işlenmiş kütlenin son okuması; öncekinden sonra olmalı.',
  outputs: 'Rulodan çıkan kütle, beş sınıftan biriyle.',
}

export const COST_STATUS_TEXT = 'Hesaplanmadı'

/** GDC-BAL/1 panel (BAL slice 2). The engine's input names, in the user's words; an unknown name is shown as it came. */
export const ENGINE_PARAMETER_LABEL: Record<string, string> = {
  fireKg: 'fire (kg)',
  rawMaterialKg: 'hammadde (kg)',
  actualKg: 'gerçek kg',
  actualCount: 'gerçek adet',
}

export function engineParameterLabel(name: string): string {
  return ENGINE_PARAMETER_LABEL[name] ?? name
}

export const MASS_BALANCE_HEADING = 'Kütle dengesi (GDC-BAL/1)'
export const MASS_BALANCE_HELP =
  'Emrin kapanışının kullandığı hesap: fiilî girdi − (iyi ürün + ikinci kalite + kullanılabilir artık + hurda + ' +
  'proses kaybı + kalan WIP + onaylı ölçüm düzeltmesi). Fark işaretiyle gösterilir ve hiçbir sınıfa yazılmaz.'
export const ENGINE_UNAVAILABLE = 'Hesaplanamıyor — eksik girdi:'
export const ENGINE_REFUSED = 'Hesap reddedildi:'
/** BAL-004 / BAL-006: why the order cannot supply their inputs yet (no number is guessed). */
export const SCRAP_PERCENT_NOTE =
  'Fire yüzdesinde hangi sınıfların fire sayılacağı ve hangi kütlenin taban olacağı iş kararı bekliyor.'
export const ACTUAL_UNIT_KG_NOTE = 'Fiilî birim kg için emirde adet kaydı yok; adet, adet üreten süreçlerle gelecek.'
export const REMAINING_UNAVAILABLE = 'Planda beyan edilmiş bir hedef yok; kalan hedef hesaplanmaz.'
export const KG_FORMAT_HELP = 'Nokta ile ondalık, işaretsiz (ör. 1250.5). Ondalık sınırı şirketin kg profilinden gelir.'
export const KG_FORMAT_ERROR = 'Kilogram biçimi geçersiz: işaretsiz, nokta ile ondalık bir sayı girin.'
export const ROLL_REQUIRED = 'Bir ana rulo seçin.'
export const TIME_REQUIRED = 'Olgunun zamanını girin.'
export const CLASS_REQUIRED = 'Bir çıktı sınıfı seçin.'
export const CLOSED_ORDER_TEXT = 'Emir kapalı; yeni gerçekleşme kaydedilmez.'

export const APPLIED_TITLE = 'Kaydedildi'
export const APPLIED_DETAIL = 'Olgu kaydedildi. Tablodaki değerler emrin yeniden okunmuş hâlidir.'
export const REPLAYED_TITLE = 'Daha önce kaydedilmiş'
export const REPLAYED_DETAIL =
  'Bu işlem sunucuda zaten kayıtlıydı; ikinci bir kayıt oluşmadı. Tablo emrin güncel hâlidir.'

const RULE_TEXT: Record<string, string> = {
  manufacturing_order_closed: CLOSED_ORDER_TEXT,
  manufacturing_roll_not_in_order: 'Bu ana rulo emirde yok.',
  manufacturing_roll_not_issued: 'Bu ana rulodan henüz girdi çıkışı yapılmadı.',
  manufacturing_input_already_issued: 'Bu olgu kimliğiyle bir girdi çıkışı zaten var.',
  manufacturing_return_already_recorded: 'Bu olgu kimliğiyle bir iade zaten var.',
  manufacturing_return_exceeds_issued: 'İade, bu rulodan çıkan kütleyi aşamaz.',
  manufacturing_output_already_declared: 'Bu olgu kimliğiyle bir çıktı zaten bildirildi.',
  manufacturing_wip_already_measured: 'Bu olgu kimliğiyle bir WIP ölçümü zaten var.',
  manufacturing_wip_measurement_not_latest: 'WIP ölçümü, rulonun son ölçümünden sonra olmalı.',
  manufacturing_mass_scale_exceeded: 'Kütle, şirketin kg profilinin izin verdiğinden fazla ondalık içeriyor; yuvarlanmadı.',
  manufacturing_kilogram_precision_exceeded: 'Kütle en fazla üç ondalık olabilir; yuvarlanmadı.',
  manufacturing_mass_negative: 'Kütle negatif olamaz.',
  manufacturing_value_does_not_fit: 'Kütle kaydedilebilir aralığın dışında.',
  manufacturing_kilogram_profile_not_in_effect: 'Olgu tarihinde geçerli bir kg profili yok.',
  manufacturing_kilogram_profile_invalid: 'Şirketin kg profili geçersiz.',
  manufacturing_kilogram_profile_company_mismatch: 'kg profili bu şirkete ait değil.',
  settings_unit_profile_not_found: 'Şirketin onaylı bir kg profili yok.',
}

export function failureText(failure: FactFailure): string {
  switch (failure.kind) {
    case 'validation':
      return failure.field === null
        ? 'Sunucu isteği geçersiz buldu; değerleri kontrol edin.'
        : `Sunucu isteği geçersiz buldu (alan: ${failure.field}).`
    case 'unauthenticated':
      return 'Oturumunuz sona erdi; yeniden giriş yapın.'
    case 'forbidden':
      return 'Bu emre olgu kaydetme yetkiniz yok.'
    case 'not-found':
      return 'Emir bulunamadı.'
    case 'version-conflict':
      return 'Emir siz formu doldururken değişti. Güncel hâli yeniden okundu; değerleri kontrol edip yeniden gönderin.'
    case 'idempotency-conflict':
      return 'Bu işlem anahtarı başka bir istekle kullanılmış. Yeni bir gönderim olarak yeniden deneyin.'
    case 'concurrent-change':
      return 'Aynı anda başka bir kayıt işlendi; bu olgu kaydedilmedi. Emir yeniden okundu; yeniden gönderin.'
    case 'id-conflict':
      return `Bu olgu kimliği başka bir içerikle kayıtlı (${failure.code}). Yeni bir gönderim olarak yeniden deneyin.`
    case 'rule':
      return RULE_TEXT[failure.code] ?? `İşlem reddedildi (${failure.code}).`
    case 'unsupported-media':
      return 'İstek biçimi sunucu tarafından kabul edilmedi.'
  }
}

export function unknownText(unknown: FactUnknown): string {
  switch (unknown.kind) {
    case 'offline':
      return 'Bağlantı yok. Olgunun kaydedilip kaydedilmediği bilinmiyor.'
    case 'network-error':
      return 'Sunucudan cevap gelmedi. Olgunun kaydedilip kaydedilmediği bilinmiyor.'
    case 'service-unavailable':
      return `Sunucu şu an cevap veremedi (${unknown.status}). Olgunun kaydedilip kaydedilmediği bilinmiyor.`
    case 'unexpected-success':
      return `Sunucu beklenmeyen bir cevap verdi (${unknown.status}). Olgunun kaydedilip kaydedilmediği bilinmiyor.`
    case 'undecodable':
      return `Sunucunun cevabı okunamadı (${unknown.status}). Olgunun kaydedilip kaydedilmediği bilinmiyor.`
    case 'aborted':
      return 'İstek yarıda kaldı. Olgunun kaydedilip kaydedilmediği bilinmiyor.'
  }
}

export const UNKNOWN_ACTION_HELP =
  'Aynı olguyu aynı işlem anahtarıyla yeniden gönderin: kaydedildiyse ikinci bir kayıt oluşmaz. Yeni bir olgu, bu çözülene kadar gönderilemez.'
