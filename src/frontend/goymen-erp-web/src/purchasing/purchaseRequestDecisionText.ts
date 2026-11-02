import type { PurchaseRequestStatus } from './contract'

/** A1-1: request status labels shared by the P-01 list and detail. */
export const PURCHASE_REQUEST_STATUS_TEXT: Record<PurchaseRequestStatus, string> = {
  Draft: 'Taslak',
  Submitted: 'Gönderildi',
  Approved: 'Onaylandı',
  Rejected: 'Reddedildi',
}

/** A1-1 (plan API-01; ALS-D2, No.161 §1): the request decision control's user texts. */
export const PURCHASE_REQUEST_DECISION_TEXT = {
  heading: 'Talep kararı',
  note: 'Onay veya ret yalnız talep durumunu değiştirir; sipariş, fiyat veya stok hareketi üretmez. Talebi hazırlayan veya gönderen karar veremez.',
  approve: 'Talebi onayla',
  approving: 'Onaylanıyor…',
  reject: 'Talebi reddet',
  rejecting: 'Reddediliyor…',
  reasonLabel: 'Ret gerekçesi',
  reasonHint: 'Ret için gerekçe zorunludur (en fazla 4000 karakter).',
  reasonRequired: 'Ret gerekçesi boş olamaz.',
  reasonTooLong: 'Ret gerekçesi 4000 karakteri aşamaz; kısaltın.',
  unknown: 'Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar, sürüm ve gerekçeyle yeniden gönderin.',
  retryApprove: 'Aynı onayı yeniden gönder',
  retryReject: 'Aynı reddi yeniden gönder',
  approved: 'Talep onaylandı ve yeniden okundu.',
  rejected: 'Talep reddedildi ve yeniden okundu.',
  replayed: 'Önceki karar yanıtı yeniden gösterildi.',
  failed: 'Karar kaydedilmedi',
  reread: 'Kayıt yeniden okundu.',
  approvalFacts: 'Onay',
  rejectionFacts: 'Ret',
  reasonFacts: 'Ret gerekçesi',
} as const

/** Known refusal codes in user words; an unknown code is shown as-is beside the generic text. */
export const PURCHASE_REQUEST_DECISION_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  version_conflict: 'Talep bu sırada değişti; güncel sürüm yeniden okundu.',
  purchasing_request_decision_state: 'Talep bu karar için uygun durumda değil.',
  purchasing_request_same_actor: 'Talebi hazırlayan veya gönderen aynı talep için karar veremez.',
  purchasing_request_maker_unknown: 'Talebi hazırlayan veya gönderen bilinmiyor; karar verilemez.',
  purchasing_request_has_approved_order: 'Talebe bağlı onaylı sipariş var; önce bağlı sipariş iptal edilmelidir.',
  purchasing_request_rejection_reason_required: 'Ret gerekçesi zorunludur.',
  purchasing_request_rejection_reason_too_long: 'Ret gerekçesi 4000 karakteri aşamaz.',
  concurrent_change: 'Talep aynı anda başka bir işlemle değişti; kayıt yeniden okundu.',
  same_key_payload_mismatch: 'Aynı işlem anahtarı başka bir içerikle kullanıldı.',
}
