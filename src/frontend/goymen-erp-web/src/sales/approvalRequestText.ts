/**
 * WP4 (SAT-D475 / SAT-D476) — every user-facing word of the Sales approval request panels: the quote's conversion requests
 * and the order's over-delivery requests, their statuses, the commands and the server refusals they can meet.
 */

import { APPROVAL_REQUEST_STATUS, type ApprovalRequestStatus } from './approvalRequests'

export const APPROVAL_REQUEST_STATUS_TEXT: Readonly<Record<ApprovalRequestStatus, string>> = {
  [APPROVAL_REQUEST_STATUS.pending]: 'Onay bekliyor',
  [APPROVAL_REQUEST_STATUS.approved]: 'Onaylandı',
  [APPROVAL_REQUEST_STATUS.rejected]: 'Reddedildi',
  [APPROVAL_REQUEST_STATUS.stale]: 'Geçersiz (teklif değişti)',
}

/** The server refusals of the request commands (their problem codes) in Turkish; an unknown code falls to the generic text. */
export const APPROVAL_REQUEST_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  sales_approval_request_duplicate: 'Bu teklif revizyonu için zaten bekleyen bir dönüşüm talebi var.',
  sales_approval_request_quote_not_expired: 'Teklifin geçerlilik süresi dolmamış; dönüşüm onayı gerekmez.',
  sales_approval_request_quote_already_approved: 'Teklifin bu revizyonu için süre onayı zaten verilmiş.',
  sales_approval_request_self_decision: 'Sevki deneyen kişi kendi fazla teslim talebini onaylayamaz ya da reddedemez.',
  sales_approval_request_stale:
    'Talep artık sorulduğu haliyle geçerli değil (sipariş revize edildi, kapandı ya da sevk toplamı onay bandında değil).',
  sales_approval_request_blocked: 'Talep edilen sevk toplamı blok bandında; onaylanamaz.',
  sales_approval_request_reason_required: 'Ret gerekçe ister.',
  sales_approval_request_text_too_long: 'Metin izin verilen uzunluğu aşıyor.',
  version_conflict: 'Kayıt siz bakarken değişti; liste yeniden okundu.',
  state_conflict: 'Kaydın durumu bu işleme izin vermiyor; liste yeniden okundu.',
  concurrent_change: 'Aynı anda başka bir değişiklik kaydedildi; liste yeniden okundu.',
  same_key_payload_mismatch: 'Aynı deneme farklı içerikle gönderildi; işlem yapılmadı.',
}

export const approvalRequestProblemText = (code: string | null | undefined): string =>
  (code !== null && code !== undefined && Object.hasOwn(APPROVAL_REQUEST_PROBLEM_TEXT, code)
    ? APPROVAL_REQUEST_PROBLEM_TEXT[code]
    : null) ?? 'İşlem tamamlanamadı. Tekrar deneyin.'

export const APPROVAL_REQUEST_TEXT = {
  loading: 'Talepler okunuyor…',
  forbidden: 'Talepleri görme yetkiniz yok.',
  unavailable: 'Talepler şu anda okunamadı.',
  retry: 'Yeniden oku',
  requestedBy: 'Talep eden',
  requestedAt: 'Talep zamanı',
  decidedBy: 'Karar veren',
  decisionReason: 'Gerekçe',
  status: 'Durum',
  reasonLabel: 'Ret gerekçesi',
  reject: 'Reddet',
  sending: 'Gönderiliyor…',
  unknownOutcome: 'Sonuç bilinmiyor; aynı deneme yeniden gönderilebilir.',
  retrySame: 'Aynı denemeyi yeniden gönder',
  quote: {
    heading: 'Dönüşüm onayı talepleri',
    intro:
      'Süresi dolmuş teklif ancak yönetici onayıyla siparişe dönüştürülür. Talep iş kutusunda bekler; onay teklifin kendi süre onayıyla verilir.',
    revision: (revision: string) => `Rev.${revision}`,
    request: 'Dönüşüm onayı talep et',
    noteLabel: 'Not (isteğe bağlı)',
    requested: 'Dönüşüm onayı talebi açıldı; iş kutusunda onay bekliyor.',
    rejected: 'Talep reddedildi.',
    empty: 'Bu teklif için dönüşüm onayı talebi yok.',
  },
  overDelivery: {
    heading: 'Fazla teslim talepleri',
    intro:
      'Onay bandında bir sevk denendiğinde talep açılır; talep onaylanmadan sevk kesinleşmez. Sevki deneyen kişi kendi talebine karar veremez.',
    line: (requestNo: number) => `Talep #${requestNo}`,
    ordered: 'Sipariş miktarı',
    attempted: 'Denenen sevk',
    requestedTotal: 'Talep edilen sevk toplamı',
    approve: 'Onayla',
    approved: 'Fazla teslim talebi onaylandı; sevk yeniden denenebilir.',
    rejected: 'Fazla teslim talebi reddedildi.',
  },
} as const
