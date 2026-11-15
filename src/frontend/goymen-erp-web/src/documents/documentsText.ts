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
  scanSystemGenerated: 'Sistem üretimi',
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
  typeLocal: 'Bu dosya türü bu kayda eklenemez. İzin verilen türlerden birini seçin.',
  downloadRefused: 'Belge indirilemedi.',
  notScanned: 'Bu belge güvenlik taramasından geçmediği için indirilemez.',
  versionsShow: (fileName: string) => `${fileName} sürümlerini göster`,
  versionsHide: (fileName: string) => `${fileName} sürümlerini gizle`,
  versionsLabel: 'Sürümler',
  versionsHeading: (fileName: string) => `${fileName} — sürümler`,
  versionsNote: 'Her sürüm ayrı saklanır; yeni sürüm eskisini değiştirmez. En yeni sürüm en üsttedir.',
  versionsCaption: (fileName: string) => `${fileName} sürüm geçmişi`,
  versionsLoading: 'Sürümler getiriliyor…',
  versionsFailed: 'Sürüm geçmişi getirilemedi.',
  versionsAgain: 'Sürümleri yeniden getir',
  versionsGone: 'Bu belgenin sürüm geçmişi bu şirkette görüntülenemiyor.',
  versionsMore: 'Daha eski sürümleri getir',
  versionsMoreLoading: 'Daha eski sürümler getiriliyor…',
  versionsMoreFailed: 'Daha eski sürümler getirilemedi. Gösterilen sürümler yerinde duruyor.',
  versionsMoreAgain: 'Kaldığı yerden tekrar dene',
  versionsEnd: 'Bu belgenin tüm sürümleri gösteriliyor.',
  versionsClose: 'Sürümleri kapat',
  columnVersion: 'Sürüm',
  versionNo: (versionNo: number) => `Sürüm ${versionNo}`,
  versionDownload: (fileName: string, versionNo: number) => `${fileName} sürüm ${versionNo} içeriğini indir`,
  versionDownloadRefused: 'Bu sürüm indirilemedi.',
  versionNotScanned: 'Bu sürüm güvenlik taramasından geçmediği için indirilemez.',
  appendLegend: 'Yeni sürüm ekle',
  appendFileLabel: 'Yeni sürüm dosyası',
  appendNote: (versionNo: number) => `Dosya bu belgenin ${versionNo}. sürümü olarak eklenir; önceki sürümler değişmez.`,
  append: 'Sürümü ekle',
  appending: 'Sürüm ekleniyor…',
  appended: (fileName: string, versionNo: number) => `${fileName}, sürüm ${versionNo} olarak eklendi.`,
  appendReplayed: 'Bu sürüm daha önce kaydedilmişti; aynı sürüm gösteriliyor.',
  appendUnknownTitle: 'Sürüm eklemenin sonucu bilinmiyor',
  appendUnknownDetail:
    'Sunucudan yanıt alınamadı; sürüm kaydedilmiş olabilir. Aynı denemeyi tekrarlamak ikinci bir sürüm oluşturmaz.',
  appendRetry: 'Aynı sürüm denemesini tekrarla',
  linkProbeLoading: 'Bu dosyanın bu şirkette daha önce yüklenip yüklenmediği denetleniyor…',
  linkProbeNone: 'Bu kayda bağlanabilecek var olan bir belge bulunamadı; dosyayı yükleyebilirsiniz.',
  linkProbeUnavailable: 'Var olan belgeler denetlenemedi; dosyayı yine de yükleyebilirsiniz.',
  linkTitle: 'Bu dosya bu şirkette daha önce yüklenmiş',
  linkDetail:
    'Aynı dosyayı ikinci kez yüklemek yerine var olan belgeyi bu kayda bağlayabilirsiniz. Belge tek kez saklanır; bağlantı belgeyi ve sürümlerini değiştirmez.',
  linkRow: (fileName: string, versionNo: number, uploadedAt: string) => `${fileName} — sürüm ${versionNo}, ${uploadedAt}`,
  linkAction: (fileName: string) => `${fileName} belgesini bu kayda bağla`,
  linkLabel: 'Var olan belgeyi bu kayda bağla',
  linking: 'Bağlanıyor…',
  uploadAnyway: 'Yine de yeni bir belge olarak kaydetmek için “Yükle” düğmesini kullanın.',
  linked: (fileName: string) => `${fileName} bu kayda bağlandı.`,
  linkReplayed: 'Bu bağlantı daha önce kurulmuştu; aynı kayıt gösteriliyor.',
  linkUnknownTitle: 'Bağlamanın sonucu bilinmiyor',
  linkUnknownDetail:
    'Sunucudan yanıt alınamadı; belge bu kayda bağlanmış olabilir. Aynı denemeyi tekrarlamak ikinci bir bağlantı oluşturmaz.',
  linkRetry: 'Aynı bağlama denemesini tekrarla',
} as const

/** Why a link of an existing document was refused, by its stable code. Nothing was linked in any of these cases. */
export function linkRefusalText(status: number, code: string | null): string {
  switch (code) {
    case DOCUMENT_PROBLEM.alreadyLinked:
      return 'Bu belge bu kayda zaten bağlı.'
    case DOCUMENT_PROBLEM.concurrentChange:
      return 'Bu belgeye siz bakarken yeni bir sürüm eklendi. Var olan belgeler yeniden denetlendi; tekrar seçin.'
    case DOCUMENT_PROBLEM.linkNotAccepted:
      return 'Bu kayıt türüne var olan bir belge bağlanamaz. Dosyayı yükleyebilirsiniz.'
    case DOCUMENT_PROBLEM.sameKeyPayloadMismatch:
      return 'Bu deneme başka bir bağlamayla karıştı. Dosyayı yeniden seçip tekrar deneyin.'
    default:
      if (status === 403) return 'Bu belgeyi bu kayda bağlama yetkiniz yok.'
      if (status === 404) return 'Belge veya kayıt bu şirkette bulunamadı.'
      return 'Belge bu kayda bağlanamadı.'
  }
}

/** Why a new version was refused, by its stable code. No version was added in any of these cases. */
export function versionRefusalText(status: number, code: string | null): string {
  switch (code) {
    case DOCUMENT_PROBLEM.concurrentChange:
      return 'Bu belgeye siz bakarken başka bir sürüm eklendi. Sürüm geçmişi yenilendi; dosyayı yeniden seçip tekrar deneyin.'
    case DOCUMENT_PROBLEM.sameKeyPayloadMismatch:
      return 'Bu deneme başka bir dosyayla karıştı. Dosyayı yeniden seçip tekrar deneyin.'
    case DOCUMENT_PROBLEM.contentRejected:
      return 'Güvenlik taraması dosyada zararlı içerik buldu. Yeni sürüm oluşmadı.'
    case DOCUMENT_PROBLEM.empty:
    case DOCUMENT_PROBLEM.tooLarge:
    case DOCUMENT_PROBLEM.typeNotAllowed:
    case DOCUMENT_PROBLEM.contentMismatch:
      return `${refusalText(status, code)} Yeni sürüm oluşmadı.`
    default:
      if (status === 403) return 'Bu belgeye yeni sürüm ekleme yetkiniz yok.'
      if (status === 404) return 'Belge bu şirkette bulunamadı.'
      if (status === 413) return 'Dosya izin verilen boyuttan büyük. Yeni sürüm oluşmadı.'
      return 'Yeni sürüm eklenemedi.'
  }
}

/** Why an upload was refused, by its stable code; an unknown code gets the generic text. */
export function refusalText(status: number, code: string | null): string {
  switch (code) {
    case DOCUMENT_PROBLEM.empty:
      return 'Dosya boş. İçeriği olan bir dosya seçin.'
    case DOCUMENT_PROBLEM.tooLarge:
      return 'Dosya izin verilen boyuttan büyük.'
    case DOCUMENT_PROBLEM.typeNotAllowed:
      return 'Bu dosya türü bu kayda eklenemez. Yalnız bu kayıt için izin verilen türler yüklenebilir.'
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
    // B-26 / FNS-D52 bank-statement import kinds (only on a record whose owner opted in).
    case 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet':
      return 'XLSX'
    case 'text/plain':
      return 'MT940'
    case 'text/csv':
      return 'CSV'
    default:
      return contentType
  }
}
