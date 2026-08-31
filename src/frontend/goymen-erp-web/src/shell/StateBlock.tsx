/**
 * Renders a shared RemoteViewState as an accessible status block. Each §12 state
 * gets distinct text — empty, forbidden, not-found and service-error are never
 * the same message (ERP-000K_SHELL_CONTRACT §12).
 */

import type { RemoteViewState } from './uiState'
import styles from './StateBlock.module.css'

const MESSAGES: Record<string, { title: string; detail: string }> = {
  'initial-loading': { title: 'Yükleniyor', detail: 'Bilgi getiriliyor.' },
  'no-authorized-company': {
    title: 'Yetkili şirket yok',
    detail: 'Hesabınız için yetkilendirilmiş bir şirket bulunamadı.',
  },
  'empty-no-data': { title: 'Kayıt yok', detail: 'Bu görünümde henüz kayıt yok.' },
  'empty-filtered': { title: 'Sonuç yok', detail: 'Filtreye uyan kayıt bulunamadı.' },
  unauthenticated: {
    title: 'Oturum sonlandı',
    detail: 'Devam etmek için yeniden giriş yapın.',
  },
  forbidden: { title: 'Yetkiniz yok', detail: 'Bu içeriğe erişim yetkiniz bulunmuyor.' },
  'not-found': { title: 'Bulunamadı', detail: 'İstenen kayıt bulunamadı.' },
  conflict: {
    title: 'Çakışma',
    detail: 'Kayıt başka bir işlemle değişti; farkı gözden geçirin.',
  },
  offline: { title: 'Bağlantı yok', detail: 'İnternet bağlantısı geldiğinde tekrar deneyin.' },
  'service-unavailable': {
    title: 'Servis geçici olarak kullanılamıyor',
    detail: 'Kısa süre sonra tekrar deneyin.',
  },
  'malformed-response': {
    title: 'Beklenmeyen yanıt',
    detail: 'Sunucu yanıtı beklenen biçimde değil. İşlem güvenli biçimde durduruldu.',
  },
  'stale-rejected': {
    title: 'Eski bağlam yanıtı yok sayıldı',
    detail: 'Şirket bağlamı değişti; eski yanıt uygulanmadı.',
  },
  ready: { title: 'Hazır', detail: '' },
}

export function StateBlock({
  state,
  subject,
}: {
  readonly state: RemoteViewState<unknown>
  readonly subject: string
}) {
  const message = MESSAGES[state.kind] ?? MESSAGES['malformed-response']
  const isBusy = state.kind === 'initial-loading'
  return (
    <div
      className={styles.block}
      role="status"
      aria-live={isBusy ? 'polite' : 'assertive'}
      data-state={state.kind}
    >
      <p className={styles.title}>
        {message.title}
        {subject ? ` — ${subject}` : ''}
      </p>
      {message.detail ? <p className={styles.detail}>{message.detail}</p> : null}
    </div>
  )
}
