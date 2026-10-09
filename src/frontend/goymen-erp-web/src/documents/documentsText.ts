/** User-visible Turkish text of the document panel (AT-065), in one place. */

import { DOCUMENT_PROBLEM } from './contract'

export const DOCUMENTS_TEXT = {
  heading: 'Belgeler',
  note: 'Bu kayda bu şirkette eklenen belgeler. Belgeler silinmez ve üzerine yazılmaz; her yükleme ayrı bir kayıttır.',
  caption: 'Kayda bağlı belgeler',
  columnFile: 'Dosya',
  columnType: 'Tür',
  columnSize: 'Boyut',
  columnUploaded: 'Yüklenme',
  columnState: 'Güvenlik taraması',
  columnAction: 'İşlem',
  none: 'Bu kayda bu şirkette henüz belge eklenmedi.',
  scanClean: 'Tarandı',
  scanPending: 'Taranmadı — indirilemez',
  download: (fileName: string) => `${fileName} dosyasını indir`,
  downloadLabel: 'İndir',
  downloading: 'İndiriliyor…',
  preview: (fileName: string) => `${fileName} dosyasını önizle`,
  previewLabel: 'Önizle',
  previewing: 'Önizleme hazırlanıyor…',
  previewTitle: (fileName: string) => `${fileName} önizlemesi`,
  previewAlt: (fileName: string) => `${fileName} belge önizlemesi`,
  closePreview: 'Önizlemeyi kapat',
  previewRefused: 'Belge önizlenemedi.',
  previewTypeRefused: 'Belge güvenli bir PNG veya JPEG görseli olarak doğrulanamadı.',
  uploadLegend: 'Belge ekle',
  fileLabel: 'Dosya seç',
  allowed: (extensions: readonly string[], maximumBytes: number) =>
    `İzin verilen türler: ${extensions.map((e) => e.slice(1).toUpperCase()).join(', ')}. En fazla ${formatSize(maximumBytes)}.`,
  upload: 'Yükle',
  uploading: 'Yükleniyor…',
  uploaded: (fileName: string) => `${fileName} yüklendi.`,
  replayed: 'Bu yükleme daha önce kaydedilmişti; aynı kayıt gösteriliyor.',
  duplicateTitle: 'Aynı dosya bu şirkette daha önce yüklenmiş',
  duplicateDetail:
    'Yeni belge ayrıca kaydedildi. Aynı içerik sistemde tek kez saklanır; aşağıdaki kayıtlarla aynı dosya olup olmadığını kontrol edin.',
  duplicateRow: (fileName: string, uploadedAt: string) => `${fileName} — ${uploadedAt}`,
  unknownTitle: 'Yüklemenin sonucu bilinmiyor',
  unknownDetail:
    'Sunucudan yanıt alınamadı; belge kaydedilmiş olabilir. Aynı dosyayla tekrar denemek ikinci bir kayıt oluşturmaz.',
  retry: 'Aynı dosyayla tekrar dene',
  abandon: 'Vazgeç',
  loading: 'Belgeler getiriliyor…',
  readFailed: 'Belgeler getirilemedi.',
  readAgain: 'Belgeleri yeniden getir',
  tooLargeLocal: (maximumBytes: number) => `Dosya çok büyük. En fazla ${formatSize(maximumBytes)} yüklenebilir.`,
  typeLocal: 'Bu dosya türü eklenemez. Yalnız PDF, PNG veya JPEG dosyası seçin.',
  downloadRefused: 'Belge indirilemedi.',
  notScanned: 'Bu belge güvenlik taramasından geçmediği için indirilemez.',
} as const

/** Why an upload was refused, by its stable code; an unknown code gets the generic text. */
export function refusalText(status: number, code: string | null): string {
  switch (code) {
    case DOCUMENT_PROBLEM.empty:
      return 'Dosya boş. İçeriği olan bir dosya seçin.'
    case DOCUMENT_PROBLEM.tooLarge:
      return 'Dosya izin verilen boyuttan büyük.'
    case DOCUMENT_PROBLEM.typeNotAllowed:
      return 'Bu dosya türü eklenemez. Yalnız PDF, PNG veya JPEG dosyası yüklenebilir.'
    case DOCUMENT_PROBLEM.contentMismatch:
      return 'Dosyanın içeriği uzantısıyla uyuşmuyor. Dosya reddedildi.'
    case DOCUMENT_PROBLEM.contentRejected:
      return 'Güvenlik taraması dosyada zararlı içerik buldu. Dosya kaydedilmedi.'
    case DOCUMENT_PROBLEM.sameKeyPayloadMismatch:
      return 'Bu deneme başka bir dosyayla karıştı. Dosyayı yeniden seçip tekrar yükleyin.'
    case DOCUMENT_PROBLEM.concurrentChange:
      return 'Aynı anda başka bir yükleme yapıldı. Tekrar deneyin.'
    default:
      if (status === 403) return 'Bu kayda belge ekleme yetkiniz yok.'
      if (status === 404) return 'Kayıt bu şirkette bulunamadı.'
      if (status === 413) return 'Dosya izin verilen boyuttan büyük.'
      return 'Belge yüklenemedi.'
  }
}

const SIZE = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 })

/** Bytes as the user reads them: "812 B", "12,5 KB", "3,2 MB". */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${SIZE.format(bytes / 1024)} KB`
  return `${SIZE.format(bytes / (1024 * 1024))} MB`
}

const WHEN = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function formatInstant(iso: string): string {
  return WHEN.format(new Date(iso))
}

/** The kind as a short label, from the server's canonical type. */
export function typeLabel(contentType: string): string {
  switch (contentType) {
    case 'application/pdf':
      return 'PDF'
    case 'image/png':
      return 'PNG'
    case 'image/jpeg':
      return 'JPEG'
    default:
      return contentType
  }
}
