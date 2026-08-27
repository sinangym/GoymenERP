# Domain ve Modül Haritası

| Modül | Sahip olduğu temel veri | Başka modüllere sunduğu sözleşme |
|---|---|---|
| Platform | Şirket, kullanıcı, rol, görev, onay, audit | CompanyContext, Authorization, Approval, Audit |
| Cari | Taraf, CariHesap, adres, kişi, limit, sicil | Party/Account query ve risk girdileri |
| Katalog | Ürün, hizmet, birim, paket, fiyat | Product snapshot ve conversion service |
| Satış | Talep, teklif, satış siparişi, revizyon | Demand/fulfillment ihtiyacı |
| Satın Alma | Talep, sipariş, termin, kabul | Future supply ve accepted receipt |
| Stok | Stok olayı, pozisyon, etiket, blokaj | Available position, movement command |
| Tahsis | Kaynak–ihtiyaç ilişkisi | Allocation lifecycle |
| Üretim | Plan, reçete/formül snapshot, kuyruk, gerçekleşme | Planned/actual output events |
| Lojistik | Yükleme, araç, sevkiyat, tartım, transfer | Shipment/receipt realization |
| Belge | Blob, belge, sürüm, kayıt bağlantısı | Secure document reference |
| Cari Finans | Açık kalem, dağıtım | Open item lifecycle |
| Hazine | Banka/kasa, para kaynağı, çek/senet/POS/DBS | Financial source events |
| Muhasebe | Hesap planı, fiş, dönem, mizan | Accounting classification/status |
| Raporlama | SemanticFieldCatalog, ReportDefinition, ReportVersion, ReportShare, ReportSchedule, ReportRun, ReportArtifact ve türetilmiş semantik projeksiyon | Yetki-kapsamlı read-only analytics ve sürümlü rapor tanımı |
| AI | Tool catalog, prompt/policy, suggestion/audit ve şemalı ReportDefinitionDraft | Yetkili ve şemalı yardım; ham SQL yok |

## Sınır ilkesi

Bir modül başka modülün tablosuna doğrudan yazmaz. Yazma, sahip modülün komut servisi ve transaction/event sözleşmesi üzerinden yapılır.

## Raporlama semantik alt modeli (v1.1)

- `SemanticFieldCatalog`: kullanıcı/AI için izinli veri kümelerini, alanları, ölçüleri, tipleri, ilişkileri ve yetki etiketlerini yayımlar.
- `ReportDefinition`: kullanıcı niyetinin kalıcı semantik tanımıdır; sonuç satırlarını saklamaz.
- `ReportVersion`: tanım değişikliklerini değişmez sürümler halinde tutar.
- `ReportShare`: taslağın kimlerle paylaşılabildiğini tutar; veri satırı yetkisi vermez.
- `ReportSchedule`: zamanlama, sahip/çalıştırma kimliği, şirket kapsamı ve teslimat politikasını tutar.
- `ReportRun`: her çalıştırmanın ReportVersion, veri kesim zamanı, güncel yetki sonucu, parametre ve audit/korelasyon bağını tutar.
- `ReportArtifact`: XLSX/PDF gibi üretilen dosyanın Belge Kasası referansını taşır.

AI `ReportDefinitionDraft` üretebilir. Kalıcı tanım yalnız şema/politika doğrulaması ve kullanıcı eylemi sonrasında oluşturulur. Semantic query compiler, tanımı parametrik ve izinli sorguya çevirir; AI kaynak veritabanı için SQL metni üretmez.
