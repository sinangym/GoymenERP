# Sinan Kararları — 2026-11-12 (Lojistik: F-012 fason teslim sevk türü, F-020 yükleme talimatı iptali)

Bu belge önceki karar dosyalarını silmez; LOJ-D1…LOJ-D8'i ekler (Lojistik için yeni kimlik serisi; önceki dosyalarda
`LOJ-D` kimliği yoktur). URT-D15, URT-D22, SAT-D175…SAT-D178, SAT-D209 ve Sinan'ın Issue #66 yorumu
[`6818788693`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6818788693) madde 2 (tartı farkı yalnız
kaydedilir ve raporlanır, sevk durmaz) geçerli kalır. `KARAR_SICILI` satırı eklemez.

**Kaynak:** Sinan'ın 12 Kasım 2026 7. tur `AskUserQuestion` cevapları; koordinatör Issue #66 yorumu
[`6819908312`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819908312) ("SINAN-KARARI 2026-11-12
(7. tur)", 2026-11-12T09:30:03Z) olarak kaydetti; Lojistik şeridi yorumu `gh api` ile birincil kaynaktan okudu. Her iki
cevapta da soranın "(Önerilen)" işaretli seçeneği seçildi; "(Önerilen)" işaretleri soranındır. Yorum, bu cevapların
`OPEN-*` kapatma, grant/rol veya production aktivasyonu olmadığını belirtir.

Cevaplar aynen:

- Madde 3 (F-012, Lojistik 6819532191 m.1): "Ayrı **"fason teslim"** sevk türü; mal yalnız sahibi olan cariye gider,
  satış sayılmaz; hizmet bedeli işlenen kg'a göre ayrı satırla faturalanır. (Önerilen.)"
- Madde 4 (F-020, Lojistik 6819532191 m.2): "Sevk kesinleşmediyse yetkili kişi **gerekçeyle iptal** eder; tartı kaydı
  silinmez, iptal işaretiyle saklanır; sevk kesinleştiyse talimat iptal edilemez. (Önerilen.) Yetki adı kaydedilir,
  grant insan kapısıdır."

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Fason işlenmiş müşteri malı müşteriye nasıl geri sevk edilsin (F-012; 409 `inventory_definite_shipment_owner_not_company` kapısı bugün bunu engelliyor)? | **Ayrı "fason teslim" sevk türü (Önerilen)** | Mal yalnız sahibi olan cariye gider, satış sayılmaz; hizmet bedeli işlenen kg'a göre ayrı satırla faturalanır. Seçilmeyen seçenek: satış sevk akışında sahiplik istisnası (Fable Soru 4 yol b). |
| 2 | Yükleme talimatı iptal edilebilsin mi, kim, hangi durumda, tartı kaydı ne olsun (F-020)? | **Sevk kesinleşmediyse yetkili kişi gerekçeyle iptal (Önerilen)** | Tartı kaydı silinmez, iptal işaretiyle saklanır; sevk kesinleştiyse talimat iptal edilemez; yetki adı kaydedilir, grant insan kapısıdır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D1 | Fason teslim sevk türü | Satış sevkinden ayrı bir **"fason teslim"** sevk türü eklenir. | Lojistik sevk gerçekleşmesi, sevk türü sözleşmesi |
| LOJ-D2 | Fason teslimde alıcı | Mal yalnız **sahibi olan cariye** gider; başka cariye veya şirket sahipliğindeki mal bu türle gitmez. | Fason teslim kuralı (D1 kapısının yerine geçen kural) |
| LOJ-D3 | Fason teslim satış değildir | Fason teslim satış sayılmaz; satış sevkinin tahsis/ciro/maliyet sonuçlarını üretmez. URT-D22 (satış tahsisi için kapalı) geçerlidir. | Lojistik, Stok, Satış sözleşmesi |
| LOJ-D4 | Fason hizmet bedeli | Hizmet bedeli **işlenen kg**'a göre **ayrı satırla** faturalanır; mal bedeli satırı yoktur. Fason hizmet siparişi normal satış siparişidir, yalnız hizmet satırı (SAT-D175…D178). | Satış/Finans faturalama (Lojistik yazmaz) |
| LOJ-D5 | Mevcut kapının kaderi | 409 `inventory_definite_shipment_owner_not_company` kapısı **kaldırılmaz**; fason teslim türü için sahiplik **kuralıyla değiştirilir** (D2). Satış sevkinde kapı aynen sürer. | `InventoryDefiniteShipmentParticipant`, `InventoryPhysicalAllocationHostPorts` |
| LOJ-D6 | Yükleme talimatı iptali | Sevk **kesinleşmediyse** yetkili kişi talimatı **gerekçeyle** iptal eder. | `LoadingInstruction` durum geçişi, uç nokta |
| LOJ-D7 | İptalde tartı kaydı | Tartı kaydı **silinmez**; iptal işaretiyle (kim, ne zaman, gerekçe) saklanır. | `VehicleWeighing` / talimat iptal alanları |
| LOJ-D8 | Kesinleşmiş sevkte iptal yok; yetki | Sevk kesinleştiyse talimat **iptal edilemez**. İptal yetkisinin **adı** kodda ve kayıtta tutulur; grant/rol ataması insan kapısıdır. | İzin kaydı (`permission-capability-registration`), grant yok |

Uygulama ayrıntısı (geri alınabilir, Sinan'ın kararı değildir): "kesinleşmiş sevk"in teknik karşılığı, durum/alan adları,
izin adının metni, hata kodları ve ekran düzeni spec'te (`docs/delivery/FABLE_5_2_ANALIZLERI/04_F012_F020_SPEC_2026-11-12.md`)
öneri olarak yazılır; uygulama sırasında düzeltilebilir.

## Sınırlar

Bu belge şunları **karara bağlamaz** (`NEEDS-SINAN` olarak spec'te listelenir, tahminle etkinleştirilmez):

- Fason teslimde hangi sahiplik kaydı "sahibi olan cari" sayılır ve bu sahiplik stokta nasıl tutulur (Stok/Üretim sözleşmesi).
- İşlenen kg'ın hangi kaynaktan (üretim gerçekleşmesi mi, teslim tartısı mı) alınacağı ve faturalama bağının hangi modülde
  kurulacağı.
- İptal yetkisini kimlerin alacağı (rol/grant ataması).
- Talimatı iptal edilen sevk için daha önce ayrılmış tahsis ve stok rezervinin serbest bırakılma politikasının ayrıntıları,
  iptalin bildirim/rapor etkisi.
- Talimat iptalinin, onaylı irsaliye ve tartı sonrası aşamalardaki sınırı (D8'in "kesinleşme" tanımı).

İnsan kapıları değişmez: deployment, environment, secret, Settings'ten workflow etkinleştirme, grant/rol/Critical,
production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` somut kullanıcı kararı ister. Bu belge hiçbirini
vermez.
