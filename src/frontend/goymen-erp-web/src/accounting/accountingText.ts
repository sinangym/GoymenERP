/**
 * Accounting user texts (Turkish). Kept in one file so they are localizable; no server prose is
 * shown to the user — the client branches on stable codes and statuses only.
 */

import type { PeriodFailure, PeriodUnknown } from './commandApi'
import type { JournalKind, PeriodStatus } from './contract'

export const PERIODS_TITLE = 'Muhasebe Dönemleri'
export const PERIODS_INTRO =
  'Bir dönem, onu hazırlayandan farklı bir yetkili onaylayana kadar açılmaz ve kayıt almaz. Kapanmış bir döneme yeni kayıt girmez; düzeltme açık bir dönemde yeni kayıtla yapılır.'
export const PROPOSE_LINK = 'Yeni dönem öner'
export const MORE = 'Daha fazla'

export const PERIOD_STATUS_LABEL: Readonly<Record<PeriodStatus, string>> = {
  Proposed: 'Önerildi — onay bekliyor',
  Open: 'Açık',
  Closed: 'Kapalı',
}

export const OWN_PROPOSAL = 'Sizin öneriniz — başka bir yetkili onaylamalı'
export const APPROVE = 'Onayla'
export const APPROVE_CONFIRM_TEXT =
  'Onaydan sonra dönem açılır ve kayıt alır. Onay tektir; dönemi öneren kişi onaylayamaz.'
export const APPROVE_CONFIRM = 'Evet, onayla'
export const CLOSE = 'Kapat'
export const CLOSE_CONFIRM_TEXT = 'Kapanan döneme yeni kayıt girmez. Kapanış geri alınamaz; düzeltme açık bir dönemde yeni kayıtla yapılır.'
export const CLOSE_CONFIRM = 'Evet, kapat'
export const CANCEL = 'Vazgeç'
export const APPROVED_NOTICE = 'Dönem açıldı. Liste sunucudan yeniden okundu.'
export const CLOSED_NOTICE = 'Dönem kapandı. Liste sunucudan yeniden okundu.'
export const REPLAYED_NOTICE = 'Bu işlem daha önce kaydedilmişti; sunucunun ilk cevabı gösterildi. Liste yeniden okundu.'
export const JOURNALS_OF_PERIOD = 'Fişleri'

export const PROPOSE_TITLE = 'Yeni dönem öner'
export const PROPOSE_INTRO =
  'Öneri kaydedilir ancak başka bir yetkili onaylayana kadar açılmaz. Şirketin başka bir dönemiyle çakışan tarih kabul edilmez.'
export const STARTS_ON = 'Başlangıç'
export const ENDS_ON = 'Bitiş'
export const PROPOSE_SUBMIT = 'Öner'
export const PROPOSED_NOTICE = 'Dönem önerildi; açılması için başka bir yetkili onaylamalı.'
export const BACK_TO_PERIODS = 'Dönemlere dön'
export const DATE_REQUIRED = 'Başlangıç ve bitiş tarihini girin.'
export const DATE_ORDER = 'Bitiş tarihi başlangıçtan önce olamaz.'

export const JOURNALS_TITLE = 'Muhasebe Fişleri'
export const JOURNALS_INTRO =
  'Fişler kaynak kayıtlardan türetilir ve burada yalnız okunur. Bir fiş ikinci bir para hareketi değildir.'
export const JOURNAL_KIND_LABEL: Readonly<Record<JournalKind, string>> = {
  Original: 'Asıl',
  Reversal: 'Ters kayıt',
  Correction: 'Düzeltme',
}
export const OPEN_JOURNAL = 'Aç'
export const JOURNAL_DETAIL_TITLE = 'Muhasebe fişi'
export const DERIVED_NOTE = 'Bu fiş bir kaynak kaydının türev muhasebe kaydıdır; ikinci bir para hareketi değildir.'
export const PERIOD_FILTER_NOTE = 'Yalnız bir dönemin fişleri gösteriliyor.'
export const ALL_JOURNALS = 'Tüm fişler'

// Technical references are never presented as names (Business 5745076917). The chart of accounts is an open
// decision (NEEDS-BUSINESS 5740501573), so a journal line carries an account REFERENCE with no account name;
// a source record's name belongs to its owner module, which this screen does not read.
export const ACCOUNT_COLUMN = 'Hesap referansı'
export const UNMAPPED_ACCOUNT = 'Eşlenmemiş hesap'
export const ACCOUNT_REF_NOTE =
  'Hesap planı henüz tanımlı değil: satırlar hesap adı değil, eşlenmemiş bir hesap referansı taşır.'
export const SOURCE_RECORD = 'Kaynak kayıt'
export const OPEN_LINKED_JOURNAL = 'Bağlı fişi aç'
export const REF_PREFIX = 'ref.'

/** The last eight characters of a technical id, labelled as a reference; the full id stays in the title only. */
export const shortRef = (id: string) => `${REF_PREFIX} …${id.slice(-8)}`

export const QUEUES_TITLE = 'Muhasebe Kontrol'
export const EXCEPTIONS_TITLE = 'Eşleme bekleyenler'
export const EXCEPTIONS_NOTE = 'Eksik eşleme hiçbir hesaba tahminle kaydedilmez.'
export const PENDING_TITLE = 'Tutar bekleyenler'
export const PENDING_NOTE = 'Bilinmeyen tutar sıfır sayılmaz.'
export const EMPTY_EXCEPTIONS = 'Eşleme bekleyen kayıt yok.'
export const EMPTY_PENDING = 'Tutar bekleyen kayıt yok.'

export const UNKNOWN_TITLE = 'Sonuç bilinmiyor'
export const UNKNOWN_ACTION_HELP =
  'İşlem kendiliğinden tekrarlanmadı. Aynı işlemi aynı anahtarla yeniden gönderebilirsiniz; sunucu aynı isteğe ilk cevabını verir.'
export const RESEND = 'Aynı işlemi yeniden gönder'
export const FAILED_TITLE = 'Kaydedilmedi'

const CODE_TEXT: Readonly<Record<string, string>> = {
  accounting_period_overlap: 'Bu tarihler şirketin kayıtlı bir dönemiyle çakışıyor.',
  accounting_period_conflict: 'Aynı dönem kimliği başka tarihlerle kayıtlı.',
  accounting_period_preparer_conflict: 'Aynı dönem kimliğini başka bir kişi önerdi.',
  accounting_period_not_proposed: 'Dönem artık öneri değil; liste yeniden okundu.',
  accounting_period_not_open: 'Yalnız açık bir dönem kapatılır.',
  accounting_period_already_closed: 'Dönem zaten kapalı.',
  accounting_period_invalid: 'Dönem tarihleri geçerli değil.',
}

export function failureText(failure: PeriodFailure): string {
  switch (failure.kind) {
    case 'validation':
      return 'İstek geçerli değil.'
    case 'unauthenticated':
      return 'Oturumunuz sona erdi.'
    case 'forbidden':
      return 'Bu işlem için yetkiniz yok. Dönemi öneren kişi kendi önerisini onaylayamaz.'
    case 'not-found':
      return 'Dönem bulunamadı.'
    case 'version-conflict':
      return 'Dönem siz okurken değişti. Liste yeniden okundu; güncel hâline göre yeniden deneyin.'
    case 'idempotency-conflict':
      return 'Bu işlem anahtarı başka bir istekle kullanılmış. Hiçbir şey varsayılmadı.'
    case 'concurrent-change':
      return 'Aynı anda başka bir yazma oldu; hiçbir şey kaydedilmedi. Liste yeniden okundu.'
    case 'period-conflict':
    case 'rule':
      return CODE_TEXT[failure.code] ?? `Kural reddetti (${failure.code}).`
    case 'unsupported-media':
      return 'İstek biçimi desteklenmiyor.'
  }
}

export function unknownText(unknown: PeriodUnknown): string {
  switch (unknown.kind) {
    case 'offline':
      return 'Bağlantı yok; işlemin sonucu bilinmiyor.'
    case 'network-error':
      return 'Sunucudan cevap gelmedi; işlemin sonucu bilinmiyor.'
    case 'service-unavailable':
      return 'Hizmet şu an cevap veremedi; işlemin sonucu bilinmiyor.'
    case 'unexpected-success':
    case 'undecodable':
      return 'Sunucunun cevabı okunamadı; işlemin sonucu bilinmiyor.'
    case 'aborted':
      return 'Önceki işlemin sonucu bilinmiyor.'
  }
}
