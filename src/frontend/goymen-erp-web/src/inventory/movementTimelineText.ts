/** ST-04 company movement timeline: every user-facing Turkish text of the page, in one localizable source. */
import type { InventoryMovementKind } from './movementTimelineContract'

export const MOVEMENT_TIMELINE_TEXT = {
  eyebrow: 'ST-04',
  title: 'Şirket stok hareketleri',
  scope: 'Bu liste seçili şirketin tüm etiketlerindeki kayıtlı fiziksel stok hareketlerini en yeniden eskiye gösterir; ' +
    'geldiğiniz etikete göre filtrelenmez.',
  immutable: 'Hareketler değiştirilemez kayıtlardır. Bu ekran ters kayıt, düzeltme ya da kaynak belge bilgisi çıkarmaz; ' +
    'yalnız Stok modülünün sakladığı hareketi gösterir.',
  noCompany: 'Yetkili tek şirket bağlamı seçilmeden stok hareketleri görüntülenemez.',
  subject: 'stok hareketleri',
  empty: 'Bu şirkette kayıtlı stok hareketi yok.',
  loading: 'Stok hareketleri yükleniyor…',
  loadingMore: 'Daha eski hareketler yükleniyor…',
  loadMore: 'Daha eski hareketleri yükle',
  end: 'Bu şirketin kayıtlı en eski stok hareketine ulaşıldı.',
  returnToLabel: '← Etikete dön',
  entryLink: 'Şirket stok hareketlerini aç',
  entryNote: 'Şirketin tüm etiketlerindeki hareketleri gösterir; bu etikete göre filtrelenmez.',
  shownCount: (movements: number) => `${movements} hareket gösteriliyor.`,
  cursorInvalid: 'Devam bağlantısının süresi doldu veya bu oturum için geçerli değil. Gösterilen hareketler korunmuştur; ' +
    'listeyi baştan yükleyin.',
  restart: 'Baştan yükle',
  invalidQuery: 'Stok hareketleri isteği geçersiz bulundu. Listeyi baştan yükleyin.',
  continuationUnavailable: 'Daha eski hareketler şu anda okunamadı. Gösterilen hareketler korunmuştur.',
  continuationMalformed: 'Daha eski hareketlerin yanıtı doğrulanamadı; gösterilen listeyi devam ettirmiyor. ' +
    'Listeyi baştan yükleyin.',
  retry: 'Tekrar dene',
  headers: {
    time: 'Zaman (UTC)',
    movement: 'Hareket',
    source: 'Kaynak',
    label: 'Etiket',
    direction: 'Yön',
    party: 'Sahiplik / gözetim',
    location: 'Lokasyon',
    block: 'Durum',
    quantity: 'Miktar',
  },
  tableCaption: 'Şirket genelindeki stok hareket satırları, en yeniden eskiye',
  sourceLine: 'satır',
  sourceWhole: 'belgenin tamamı',
  ownedByCompany: 'Şirket malı',
  ownedByCounterparty: 'Harici taraf malı',
  ownerUnverified: 'Sahiplik doğrulanamadı',
  heldByCompany: 'Şirket gözetiminde',
  heldByCounterparty: 'Harici taraf gözetiminde',
  custodyUnverified: 'Gözetim doğrulanamadı',
  kg: (quantity: string) => `${quantity} kg`,
} as const

export const MOVEMENT_KIND_TEXT: Readonly<Record<InventoryMovementKind, string>> = {
  Receipt: 'Giriş',
  Issue: 'Çıkış',
  Block: 'Blokaj',
  Release: 'Serbest bırakma',
  Return: 'İade',
  CountAdjustment: 'Sayım farkı',
  Scrap: 'Hurda çıkışı',
  TransferDispatch: 'Transfer çıkışı',
  TransferAccept: 'Transfer kabulü',
}

export const MOVEMENT_DIRECTION_TEXT = { In: 'Giriş', Out: 'Çıkış' } as const
export const MOVEMENT_BLOCK_TEXT = { Free: 'Serbest', Blocked: 'Bloklu' } as const
