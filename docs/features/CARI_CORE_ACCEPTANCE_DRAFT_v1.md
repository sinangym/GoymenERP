# Cari Core — Acceptance Draft v1.0

Bu belge `CARI_CORE_READINESS_v1.md` için task-local kabul taslağıdır. Kanonik `AT-*` kimliği üretmez ve `docs/acceptance/ACCEPTANCE_MATRIX.csv` dosyasını değiştirmez.

## A. Tenant / CompanyId / görünürlük

### CARI-AC-001 — Yetkili şirket listesi
Kullanıcı A şirketine yetkiliyse C-01 listesinde yalnız A şirketiyle CariHesap bağı bulunan Party kayıtlarını görür. B şirketine özel Party/CariHesap kaydı sonuç, count, facet veya hata üzerinden sızmaz.

### CARI-AC-002 — Aynı Party, iki şirket
Aynı hukuki Party A ve B Göymen şirketleriyle ilişkilendirildiğinde tek Party kimliği korunur; A ve B CariHesap kayıtları ayrı CompanyId ile çalışır.

### CARI-AC-003 — Grup görünümü
Birden fazla şirkete yetkili kullanıcı grup görünümünde aynı Party'yi kopya kart olarak görmez; şirket ilişkileri ayrı badge/alt kayıt olarak görünür ve hesap değerleri netleştirilmez.

### CARI-AC-004 — Yetkisiz bilinen ID
Kullanıcı başka şirkete bağlı bir PartyId/CariHesapId değerini doğrudan çağırdığında varlık bilgisini açığa çıkarmayan fail-closed 404/403 politikası uygulanır.

## B. Kimlik ve roller

### CARI-AC-005 — Tek kart, çok rol
Aynı CompanyPartyAccount müşteri ve tedarikçi rollerini birlikte taşıyabilir; ikinci Party veya ikinci hukuki kart oluşturulmaz.

### CARI-AC-006 — Fason/hizmet rolleri
Fason ve hizmet rolleri müşteri/tedarikçi rolünden bağımsız eklenip kaldırılabilir; rol değişikliği audit üretir.

### CARI-AC-007 — Görünen cari kodu
CARI-GATE-03 ile kabul edilen kod politikası uygulanır; kod benzersizliği/kapsamı gerçek SQL constraint veya eşdeğer deterministic guard ile kanıtlanır.

### CARI-AC-008 — Mükerrer vergi/sicil sinyali
Kabul edilen kimlik alanında exact eşleşme yeni kart oluşturma sırasında kullanıcıya mükerrer aday olarak gösterilir; sistem otomatik merge yapmaz.

## C. Adres / kişi / iletişim

### CARI-AC-009 — Çoklu adres
Bir Party/CariHesap birden fazla adres taşıyabilir; adresin kullanım amacı ve aktif/pasif durumu açıkça izlenir. Silme geçmişi yok etmez.

### CARI-AC-010 — Muhatap
Birden fazla kişi/muhatap kaydı eklenebilir; kullanıcı izin kapsamı dışında kişi verisi açılmaz.

### CARI-AC-011 — İletişim validasyonu
E-posta/telefon değerleri yalnız biçimsel ve veri-kayıpsız normalize edilir; ülke/operatör veya ticari doğruluk varsayımı uydurulmaz.

## D. Vergi / sicil / banka hassas verisi

### CARI-AC-012 — Kritik alan farkı
Unvan, vergi/kimlik no, MERSİS/resmi sicil veya IBAN değişikliği kaydetmeden önce eski/yeni farkı ve etki önizlemesini gösterir.

### CARI-AC-013 — Kritik alan approval
CARI-GATE-04 ile kabul edilen approval politikası server-side uygulanır. UI butonu veya AI önerisi approval'ı atlayamaz.

### CARI-AC-014 — IBAN geçmişi
IBAN/banka master değişikliği eski değeri fiziksel olarak yok etmez; tarihçe ve audit üzerinden kim/ne zaman/neden değiştiği görülebilir.

### CARI-AC-015 — Hassas veri log sızıntısı
VKN/TCKN/IBAN gibi hassas alanlar log/metric/trace label'larına ham değer olarak yazılmaz; güvenli correlation/record kimliği kullanılır.

## E. Yaşam döngüsü

### CARI-AC-016 — Pasife alma
Kullanılmış cari fiziksel silinmez. Pasife alma reason + authorization + expected version + idempotency + audit ile gerçekleşir.

### CARI-AC-017 — Pasif kayıt geçmişi
Pasif Party/CariHesap geçmiş sipariş/fatura/stok/finans referanslarında okunabilir kalır; yeni işlem kullanımı policy tarafından engellenebilir.

### CARI-AC-018 — Yeniden aktifleştirme
Yeniden aktifleştirme ayrı yetkili command ve audit olayıdır; tarihçe üstüne yazılmaz.

## F. Idempotency / concurrency / atomiklik

### CARI-AC-019 — Aynı create replay
Aynı idempotency key + actor + payload iki kez gönderilirse yalnız bir Party/CariHesap etkisi oluşur ve ilk kalıcı sonuç replay edilir.

### CARI-AC-020 — Aynı key farklı payload
Aynı idempotency key farklı payload veya actor ile kullanılırsa 409 conflict; ikinci business etkisi/audit etkisi oluşmaz.

### CARI-AC-021 — Stale edit
ExpectedVersion eskiyse edit 409 conflict verir; mevcut değer sessizce ezilmez.

### CARI-AC-022 — Atomik hata
Business write, audit ve kalıcı idempotency sonucu aynı transaction'da başarısız olursa hiçbir kısmi business etkisi kalmaz.

## G. Mükerrer aday ve merge sınırı

### CARI-AC-023 — Aday karşılaştırma
C-11 iki kayıt için kimlik, adres, iletişim, vergi/sicil ve banka alanlarının yetki dahilindeki farklarını gösterir; hiçbir alan otomatik olarak hedefe yazılmaz.

### CARI-AC-024 — Cross-module merge yasak
ERP-000F/outbox ve owner-module merge sözleşmesi yokken Cari modülü başka modül tablosundaki FK'yi doğrudan değiştiremez.

### CARI-AC-025 — Kaynak kart kaybolmaz
Gelecekte merge kabul edildiğinde kaynak Party teknik kimliği alias/redirect/history ile korunmadan silinemez.

## H. Cari 360 sınırı

### CARI-AC-026 — Core 360 gerçek veri
C-03 yalnız Cari-owned gerçek veriyi gösterir. Finans, risk, stok veya sipariş owner contract'ı yoksa mock toplam gösterilmez.

### CARI-AC-027 — Para birimi netleştirme yok
Farklı para birimi veya müşteri/tedarikçi hesapları tek belirsiz bakiye olarak toplanmaz.

### CARI-AC-028 — Sipariş cari hareket değildir
Sipariş ve irsaliye risk/ticari görünüm girdisi olabilir fakat Cari ledger hareketi olarak yazılmaz.

## I. UI durumları

### CARI-AC-029 — Liste durumları
C-01 loading, gerçek boş, filtre sonucu boş, unauthorized, service error ve cursor/page durumlarını ayrı gösterir.

### CARI-AC-030 — Form durumları
C-02 validation, saving, approval pending, concurrency conflict, idempotent replay ve successful result durumlarını ayırır.

### CARI-AC-031 — Responsive/erişilebilirlik
Klavye erişimi, görünür focus, label/aria, semantik hata bağlantısı ve mobil görev akışı gerçek browser testine sahiptir; eski prototipteki `min-width: 900px` üretim kabulü sayılmaz.

## J. AI sınırı

### CARI-AC-032 — AI taslak
AI foto/PDF/metinden Party/Cari taslağı çıkarabilir; kullanıcı doğrulaması olmadan ana kayda yazamaz.

### CARI-AC-033 — AI yetki kapsamı
AI yalnız mevcut kullanıcının Company/permission kapsamındaki Party/Cari verisini okuyabilir; hassas alan değişikliğini onaylayamaz.

### CARI-AC-034 — Sicil farkı
Dış sicil verisi varsa AI/esleştirme eski-yeni farkı hazırlar; kaynak tarama sonucu tek başına master değişikliği değildir.

## K. Kabul kapısı

`CARI-CORE-1` kod PR'ı ancak şu dört blocker kapanınca production schema/write davranışı içerebilir:

- CARI-GATE-01 Party tenancy/access modeli
- CARI-GATE-02 Party türü kapsamı
- CARI-GATE-03 görünen CariCode politikası
- CARI-GATE-04 kritik alan approval matrisi

CARI-GATE-05 C-12 takip görevi ilk core kod PR'ını bloke etmez.
