# Finans Modülü Hazırlık Planı v1

Tarih: 2026-11-02 (v1.1) · Hazırlayan: Claude (cloud oturumu, Finans şeridi) · Durum: **ÖNERİ** (plan; iş kararı değildir) ·
Kararlar: **`SINAN_KARARLARI_2026-11-02_FINANS.md` (FNS-D1…D13)**

Kaynaklar: `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §7.10, §7.11, §8.1, §8.2, §16 Faz 4, §19.3, §27.11 (F-01…F-17),
§28.14–28.16, §29.6 (AT-058); `docs/decisions/KARAR_SICILI.md` `DEC-FIN-001…016`, `DEC-SET-001`, `DEC-INT-001`,
`DEC-DATA-001`, `DEC-CAR-001`; `ACIK_KARARLAR.md` `OPEN-008…012`, `OPEN-016`, `OPEN-018`, `OPEN-021`;
`SINAN_KARARLARI_2026-10-21.md` §3.4 (No 22, 31, 32, 34, 79, 108, 109, 112–115, 136; K6, K7, K8, K14), No 15/17/26/35/55;
`SINAN_KARARLARI_2026-10-22.md` (Finans STOP kalktı, Dalga D); Satış `SAT-D46…D53`, `SAT-D85…D111`, `SAT-D169…D172`,
`SAT-D183`; Alış `ALS-D6…D8`, `ALS-D14/D15`, `ALS-D23…D28`, `ALS-D31`, `ALS-D38`, `ALS-D41`, `ALS-D42`, **`ALS-D44/D45`**;
`docs/ui/sources/finance/v3/*` (13 ekran, iş kuralları, rol/onay matrisi, kabul senaryoları, geliştirme notları) ve
`v4/README.md` (73 işlem workspace'i); `docs/ui/CANONICAL_SCREEN_STATUS.csv` F-01…F-17;
`docs/backlog/DELIVERY_BACKLOG_v1.json` W2-ACIKKALEM, W4-FINANS, W4-ODEMEARAC, W4-RISK, W4-NAKIT, W4-GRUPFINANS;
`task_manifest.json` ERP-000F; `src/backend/Modules/Finance/README.md`; `ALIS_HAZIRLIK_PLANI_v1.md` §6 MOD-04/MOD-15,
§13 B-1…B-3; `SATIS_HAZIRLIK_PLANI_v1.md` §3.3, MOD-01, MOD-04, MOD-09.

Bu plan kodun 2026-11-02 `main` durumuna (`3cae436`) göre yazıldı. Biçim `SATIS_`, `URETIM_` ve
`ALIS_HAZIRLIK_PLANI_v1.md` ile aynıdır. Hubvogal'in finans bölümü (kasa, banka, çek, kredi, kredi kartı, DBS) bu
oturumdan doğrudan erişilemez; 2026-11-02'de Sinan'ın izniyle ofis bilgisayarı üzerinden incelendi (§15).

Şerit kısaltmaları: **CL** Claude, **GP** ChatGPT (spec/kabul/golden), **SN** Sinan (karar). Seviye: K1/K2/K3
(`GOYMEN_ERP_HIZLI_TESLIM_v4.md`). Bu plandaki `API-`, `MOD-`, `UI-`, `TEK-`, `FZL-`, `N-` numaraları **bu plana
özeldir**; Alış/Satış planındaki aynı numaralarla karıştırılmamak için dışarıdan "Finans API-xx" diye anılır.

---

## 0. Şerit kaydı

- **ALS-D44 (2026-11-02):** Sinan: "finans tarafını ise daha konuşma açmadım açınca finans konuşmasında yazsın".
  Bu oturum o Finans konuşmasıdır. ALS-D44 notu gereği burada yazılacak iki zorunlu iş: **MOD-15 (açık kalemde
  borç/alacak yönü)** ve **MOD-04 (kesinleşen gelen faturanın borç açık kalemine dönüşmesi)**. Alış'ın A5 dalgası
  (gelen fatura kesinleştirme, Alış API-54) bu ikisi gelene kadar fail-closed kalır.
- **FNS-D1 (2026-11-02):** Satış/Üretim/Alış emsaliyle Finans'ın bütün geliştirmesi
  (backend, migration, OpenAPI, ekranlar) Claude'dadır. Eski Codex Finans kartları (FIN-T04…T06, FS0, FI04, FIN-F03)
  merge edilmiş kod olarak devralınır.
- **Modül sınırı:** Satış, Alış ve Üretim dosyalarına bu şerit dokunmaz; onların tarafı görev notuyla istenir
  (`FINANS_SATIS_DEVIR_v1.md`, `FINANS_ALIS_DEVIR_v1.md`). Alış'ın Finans'a bırakacağı `ALIS_FINANS_DEVIR_v1.md`
  henüz yazılmadı; bu plan §3 ve §4'te Finans tarafındaki sözleşmeyi önden tanımlar, not gelince uzlaştırılır.
  Ayarlar, Cari ve Muhasebe'de Finans'ın ihtiyacı olan küçük eklemeler (iki review ile) bu şeritte yapılır (FNS-D1).
- **Ortak altyapı (ALS-D44 notu):** Finans kendi olay kaydını (outbox) ve gelen olaylar için tekrar-engel kaydını
  (inbox) kendi şemasında yazar. Platform dağıtıcısı (ERP-000F) ortak iştir: Alış onu yazma yetkisini aldı; Finans
  yazmaz, yalnız tüketir. ERP-000F gelene kadar No 55 kuralı geçerlidir: **senkron sözleşme + okuma portu**.
- Değişmeyenler: review ve merge kapıları; yazar kendi PR'ını review etmez; K3'te iki bağımsız exact-head review.
  Para, formül, tolerans, yetki, migration ve saklama politikası tahminle etkinleştirilmez. İnsan kapıları (grant/rol,
  production aktivasyonu, deployment, secret, `OPEN-*` kapatma) değişmez.

---

## 1. "Hazır" tanımı

Finans modülü aşağıdakilerin hepsi sağlanınca hazırdır:

1. **Borç/alacak ayrımı:** her açık kalemin yönü vardır (müşteri alacağı / tedarikçi borcu); cari ekstre, cari
   bakiye, risk ve nakit planı yönü kullanır. Aynı firma hem müşteri hem tedarikçi olabilir (DEC-CAR-001); tedarikçi
   borcu satış riskinden düşülmez, mahsup ayrı Finans işlemidir (ALS-D42).
2. **Uçtan uca iki zincir çalışır** (master §8.1, §8.2): satış faturası → alacak açık kalemi → tahsilat (banka, kasa,
   çek/senet, POS, DBS) → dağıtım; gelen fatura → borç açık kalemi → ödeme önerisi → onay → talimat → banka sonucu
   → mutabakat → dağıtım.
3. **Finansal kaynak ile dağıtım ayrıdır** (DEC-FIN-006): banka, kasa, çek, senet, POS, şirket kartı, DBS, avans ve
   mahsup aynı dağıtım motorunu kullanır; dağıtım ikinci para hareketi yaratmaz. Ticari kapama, nakitleşme ve risk
   etkisi araç türüne göre ayrı izlenir.
4. **Kasa, banka, virman:** kasa ve banka hesaplarının ERP defter bakiyesi; kasa sayımı ve gün sonu; hesaplar arası
   virman; banka ekstresi dosyayla içe aktarılır ve 1:1…n:n mutabakat yapılır (OPEN-016: entegrasyon yok, dosya var).
5. **Ödeme araçları:** alınan ve verilen çek/senet (portföy, ciro, bankaya tahsil/teminat, karşılıksız, iade/protesto;
   DEC-FIN-016, No 109); DBS (DEC-FIN-008); müşteri POS (DEC-FIN-014; tedarikçi POS yok); şirket kredi kartı
   (DEC-FIN-015).
6. **Hazine:** krediler (tesis, kullandırım, ödeme planı, taksit ödemesi), banka limitleri (nakdi/gayrinakdi), BTM ve
   teminatlar (DEC-FIN-011), müşteri kredi limiti ve teminat (SAT-D51: sahibi Finans).
7. **Nakit planı:** 13 hafta, ilk 30 gün günlük (DEC-FIN-013); aynı olay iki aşamada sayılmaz.
8. Tutar, kur, kur farkı, risk ve bakiye yalnız backend calculator'dan gelir; ekranda yeniden hesap yok. Para
   birimleri tek sayıya netlenmez (AT-011).
9. Her mali etkili komut: yetki, expected version, idempotency, audit, maker≠checker (DEC-FIN-012 tablosu); kesin
   kayıt silinmez, ters kayıtla düzeltilir; kapalı dönem değişmez (`occurredOn`, No 108).
10. F-01…F-17 ekranları (F-14 hariç, §7) yayında; her birinde yükleniyor, boş, hata, yasak, çakışma ve bilinmeyen
    sonuç durumları var.
11. Açılış: banka/kasa bakiyeleri, açık müşteri/tedarikçi kalemleri, portföydeki çekler, kredi bakiyeleri **elle**
    girilmiştir (FNS-D12, API-14 Açılış Kaydı); açılış kaydı gerçek tarihçe gibi gösterilmez. Dosya göçü (MIG-02) yalnız
    yardımcı/kontrol yoludur, zorunlu değildir.
12. Finans'ı etkileyen açık kararlar ya kapatılmış ya yapılandırılabilir ve fail-closed.

---

## 2. Bugünkü durum

### 2.1 Var olan ve çalışan (kod + test; host'a bağlı)

Finans modülü ~81 C# dosyası, 14 migration (`finance` şeması), 39 birim test dosyası (~247 test), 25 entegrasyon test
dosyası (~116 test, 74'ü gerçek SQL), 4 tarayıcı senaryosu (`tests/e2e/finance-fin01`), 44 frontend test dosyası.
Tutarlar veritabanında tam metin (`varchar`) olarak tutulur; yuvarlama yok.

| Yetenek | Ne yapıyor | Ekran |
|---|---|---|
| Manuel banka tahsilatı (FIN-T04/T05) | Taslak → Kayıt (`MoneyEvents` tür 1); dönem `occurredOn` ile; kayıt değişmez | `/finans/tahsilatlar`, `/yeni`, `/:id` |
| POS tahsilatı (DEC-FIN-014) | Tahsilatın `pos` bloğu: müşteri brütle kapanır; komisyon ve banka neti ayrı | Tahsilat formu |
| Tahsilat ters kaydı (FIN-T06) | Öneri → onay/ret (maker≠checker); karşı para olayı (tür 2) | `/finans/ters-kayit-onaylari` |
| Banka hesabı master (FS0) | Öner → başka kişi onaylar; banka, şube, IBAN, hesap no, tür; IBAN değişikliği öner/onay; **bakiye yok** | `/finans/banka-hesaplari` |
| Manuel banka ödemesi (R47) | Taslak → onay → kayıt (`OutgoingMoneyEvents`); belge bağlanabilir; **açık kaleme dağıtılamaz** | `/finans/odemeler` |
| Alınan müşteri çek/senedi (DEC-FIN-016) | Portföy, bankada tahsilde, karşılıksız, protesto, iade, tahsil; yalnız tahsil para olayı üretir; risk okuması | `/finans/musteri-cek-senetleri` |
| Açık kalem | Tek kaynak türü `official.invoice-installment.v1`; Açık/Kapalı; **yön alanı yok** | `/finans/acik-kalemler` |
| Resmî fatura taksiti içe aktarma (FIN-F03) | Uygulamaya özel CSV şablonu; önizleme → yükleme → onay (maker≠checker) → uygula; **Mikro biçimi değil** | `/finans/onayli-fatura-taksit-kaynagi-yukle`, onaylar |
| Dağıtım (kapama) | Tahsilat kaynağı ↔ açık kalem, tam tutar; geri alma; para hareketi değildir | `/finans/dagitimlar` |
| Cari ekstre | Okuma; satır türünden sabit borç/alacak tarafı; para birimi ayrı; devir bakiyesi yok (OPEN-018) | Tahsilat listesinde gömülü |
| Satış risk okuma portu (Satış MOD-01) | Açık alacak − dağıtılmamış tahsilat − tahsil edilen çek; limit ve teminat `NoSource` | Satış ekranlarında |

Muhasebe modülü ayrıdır: dönem (öner/onay/kapa), fiş, mizan, ters fiş, olay kutusu (inbox + dedup); Finans'ın kayıtlı
tahsilatını fiş taslağı kaynağı olarak okuyabilir. Mikro'ya dışa aktarım yok; yalnız onaylı hesap planı aynası var.

### 2.2 Çalışıyor görünen ama üretimde kullanılamayan

| No | Durum | Kanıt | Sonuç |
|---|---|---|---|
| D-1 | Hiçbir Finans izni kimseye verilmedi (grant insan kapısı) | `ErpPermissions.cs` adları; PERSONA kartı | Gerçek kullanıcı hiçbir işlemi yapamaz |
| D-2 | Ayarlar para profili (T02-b) gelmeden kayıt 503/422 döner | Finance README "Runtime status" | Tahsilat taslağı bile kaydedilemez |
| D-3 | İçe aktarma Mikro dosyasını okumaz; uygulamanın CSV şablonu | `FinanceOfficialInvoiceImportV1.cs:3-15` | Muhasebe elle CSV hazırlamak zorunda |
| D-4 | Ekstrede devir bakiyesi yok; bakiye aralığın ilk günü sıfırdan başlar | `PartyLedgerService`; OPEN-018, No 79/136 | Ekstre bakiyesi yanıltıcı olabilir |
| D-5 | Kredi limiti ve teminat yok (`NoSource`) | `FinanceSalesPartyRiskReadPortV1.cs` | Satış risk kapısı limitle kontrol edemez |
| D-6 | Numara serisi yok; kayıt kimliğiyle görünür | README | Dekont/makbuz numarası yok |
| D-7 | Risk portu aynı hesaba yapılan **çıkan ödemeleri** müşteriye iade sayıyor ve dağıtılmamış tahsilat indiriminden düşüyor | `FinanceSalesPartyRiskReadPortV1.cs:126-127` | Aynı firmaya yapılan **tedarikçi ödemesi** müşteri riskini yanlış artırır (TEK-02) |

### 2.3 Hiç olmayan

- **Borç/alacak yönü** (açık kalemde), tedarikçi borcu (AP), tedarikçi ödemesinin borca dağıtımı, avans, mahsup.
- **Kasa** (kasa master, kasa hareketi, sayım, gün sonu), **virman**.
- Banka **defter bakiyesi**, banka hareket listesi, **ekstre içe aktarma ve mutabakat**, banka masrafı olayı (No 112/115).
- **Verilen** çek/senet (çek defteri, kendi çekimiz), **ciro**, bankaya teminat çeki, tahsil sonrası ters.
- **DBS** (anlaşma, limit, talimat, kabul/red, vade günü tahsilat).
- **Şirket kredi kartı** (kart, limit, harcama, ekstre, ekstre ödemesi).
- **Krediler**, banka limitleri, BTM/teminat, müşteri kredi limiti ve teminat master.
- Ödeme önerisi/paket/talimat/banka sonucu; tahsilat takibi (söz, görev, ihtilaf, hukuk).
- **Kur farkı** (No 34: çapraz para dağıtımında ayrı satır), dövizli bakiye değerlemesi.
- Nakit planı, nakit bütçesi, Finans genel bakış, kontrol merkezi, raporlar.
- Finans **outbox/inbox**; platform dağıtıcısı (ERP-000F) yok, Stok ve Work tablolarını da dağıtan yok.

### 2.4 Hubvogal karşılaştırması (ayrıntı §15)

Hubvogal Göymen'in bugün kullandığı sistemdir; finans bölümünde kasa, banka, çek, kredi, kredi kartı ve DBS modülleri
olduğu Sinan tarafından belirtildi. Finans bölümü 2026-11-02'de Sinan'ın izniyle ofis bilgisayarı üzerinden yalnız okunarak
incelendi; bulgular §15'tedir. Hubvogal yalnız referanstır; kör kopya yoktur.

### 2.5 Tasarım paketi (v3/v4) ile kod arasındaki fark

Finans v3/v4 paketi 13 ekran ailesi ve 73 işlem workspace'i tasarlar; master §27.11 bunları F-01…F-17 kanonik koduna
böler. Kodda karşılığı olan: F-02 (tahsilat + ödeme, kısmen), F-03 (dağıtım, yalnız alacak), F-04 (banka hesabı
master; ekstre yok), F-06 (yalnız alınan çek/senet), F-07 (yalnız POS tahsilatı). Diğer on iki ekran yok.
v3 §6 "geliştirme sırası" (şirket/banka → açık kalem → kaynak/dağıtım → banka/kasa → ödeme → araçlar → tahsilat
takibi → nakit → kredi → teminat → bütçe → kontrol → dashboard → AI) bu planın dalga sırasına (§10) temel alındı;
fark: borç/alacak yönü Alış'ı açtığı için en başa alındı.

---

## 3. Borç/alacak ayrımı (MOD-15) — tasarım önerisi

**Sorun (Alış §13 B-1):** `FinanceOpenItemRow` yön taşımaz; risk portu yalnız `ApprovedOfficialInvoiceInstallment`
toplar, başka tür görürse `Unavailable` döner. Alış faturası aynı türle yazılırsa tedarikçi borcu müşteri alacağı
sayılır; yeni türle yazılırsa o firmanın Satış risk okuması tamamen kapanır.

**Öneri (K3: migration + para + modüller arası sözleşme):**

| Parça | Öneri | Gerekçe |
|---|---|---|
| Alan | `FinanceOpenItems.Direction` zorunlu: `Receivable` (müşteri alacağı) / `Payable` (tedarikçi borcu) | Yön kalemin kendisinde; türden tahmin edilmez |
| Mevcut satırlar | Kaynak türü `official.invoice-installment.v1` olan satırlar `Receivable` ile doldurulur (bugün yalnız satış faturası taksitleri bu türle girer, SAT-D85). Production'da veri yok; migration `Down` veri varsa reddeder (mevcut `THROW 51051` deseni) | **FNS-D7** |
| Yeni kaynak türü | `official.incoming-invoice-installment.v1` → `Payable` (Alış MOD-04) | Kaynak türü yönü belirler; serbest seçim yok |
| İçe aktarma | CSV şablonu v2'ye `direction` sütunu; boşsa satır **"yön belirsiz" karantinasına** düşer, Finans sorumlusu yön atar (No 22) | Karar No 22 |
| Cari ekstre | Alacak kalemi → cari **borç** tarafı; borç kalemi → cari **alacak** tarafı; tahsilat → alacak, ödeme → borç (şirketin bakışıyla, mevcut `PartyLedgerSide`) | Hubvogal "Toplam Borç / Toplam Alacak" |
| Cari bakiye özeti | Yeni okuma: firma × şirket × para birimi: toplam alacak, toplam borç, net (yalnız gösterim; mahsup değil) | Alış sipariş başlığı, Satış cari kartı |
| Dağıtım kuralı | Gelen para (tahsilat) yalnız `Receivable`; çıkan para (ödeme) yalnız `Payable` kalemine dağıtılır; çapraz kapama yalnız **mahsup** işlemiyle (API-07) | ALS-D42 |
| Risk portu | `Receivable` kalemleri toplar; `Payable` kalemi yok sayılır (Unavailable değil); yalnız müşteriye **iade** olarak işaretli çıkan ödeme tahsilat indiriminden düşülür, tedarikçi ödemesi düşülmez (TEK-02) | ALS-D42, DEC-FIN-007 |
| Nakit planı | Alacak = giriş, borç = çıkış (No 113) | — |

---

## 4. Gelen ve giden faturanın açık kaleme dönüşmesi (MOD-04)

Fatura çekirdeği (dosya içe aktarma, eşleştirme, kesinleştirme) ALS-D44 gereği Alış ve Satış şeritlerindedir; Finans
yalnız **kabul eden taraftır**: kesinleşmiş faturanın taksitlerini açık kaleme çevirir ve sahibidir (DEC-FIN-005).

| Parça | Öneri |
|---|---|
| Sözleşme | Alış: `purchasing.invoice.finalized.v1`; Satış: `sales.invoice.finalized.v1`. İçerik: şirket, cari, fatura kimliği ve sürümü, para birimi, ödenecek tutar (KDV dahil, tevkif edilen KDV düşülmüş; No 15, K8), TL karşılığı ve kuru (Alış: tedarikçi faturasındaki kur, ALS-D41; Satış: SAT-D90/D172), taksitler (sıra no, vade, tutar; ALS-D24/D28, SAT-D86/D124) |
| Taşıma | ERP-000F yokken **senkron port** `IFinanceInvoiceOpenItemAdmissionPortV1` (No 55); dağıtıcı gelince aynı içerik olayla gelir, Finans inbox'ı (tüketici, mesaj kimliği, içerik hash'i) tekrarı engeller |
| Kural | Her taksit bir açık kalem; aynı fatura sürümü ikinci kez kalem üretmez; faturanın iptali kalemi ters kayıtla kapatır (SAT-D96); taksit sayısı değişirse ödenmiş taksit korunur, kalan sırayla dağıtılır (No 35) |
| Yön | Alış → `Payable`, Satış → `Receivable` (§3) |
| Mevcut CSV yolu | Kalır (açılış, düzeltme ve geçiş için); aynı fatura iki yoldan girerse kaynak kimliği tekrarı reddeder |
| Finans olayları | `finance.open-item.settled.v1` (kapandı/kısmi) ve `finance.payment.recorded.v1` → Alış'ın "ödendi" görünümü, Satış'ın risk tazelemesi için; Finans outbox'ı |

---

## 5. Eksik API uçları

Kök: `/api/v1/finance/...`. Her yazma ucu: `Idempotency-Key`, `expectedVersion`, yetki her çitte, audit; mali etkili
olanlar `preview` + `commit` ayrı (v3 geliştirme notları §4).

### 5.1 Borç/alacak çekirdeği (MOD-15)

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-01 | (migration) `OpenItems.Direction` + kaynak türü | §3 | — | MOD-15, No 22 | K3 |
| API-02 | `GET open-items?direction=` | Yöne göre liste | `finance.open-items.read` | — | K2 |
| API-03 | `GET open-items/direction-quarantine`, `POST open-items/{id}/direction` | "Yön belirsiz" karantina ve yön atama | yeni `finance.open-items.direction.assign` | No 22 | K3 |
| API-04 | `GET /api/v1/finance/party-balance?partyId&companyAccountId` | Toplam alacak / borç / bakiye + açık alacak/borç, para birimi ayrı | `finance.party-ledger.read` | §3 | K2 |
| API-05 | Cari ekstre: yönlü kalemler + devir bakiyesi (No 136) | Ekstre düzeltmesi | mevcut | No 79/136, OPEN-018 | K3 |
| API-06 | Risk portu düzeltmesi | Yalnız `Receivable` | sistem bağlamı | ALS-D42 | K3 |
| API-07 | `POST offsets/preview`, `POST offsets` | **Mahsup:** aynı firmanın alacağı ile borcunu kapatma; maker≠checker | yeni `finance.offset.*` | ALS-D42, DEC-FIN-006 | K3 |

### 5.2 Fatura kabulü (MOD-04)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-10 | Port `IFinanceInvoiceOpenItemAdmissionPortV1` (Alış ve Satış ortak) | Kesinleşen faturadan taksit başına açık kalem | DEC-FIN-005, SAT-D85, ALS-D41 | K3 |
| API-11 | İptal/iade portu | Kalemin ters kaydı; iade faturası ayrı belge (No 20/21) | SAT-D96 | K3 |
| API-12 | Finans outbox + inbox tabloları | Olay kaydı ve tekrar engeli | ALS-D44 notu, ERP-000F | K3 |
| API-14 | Açılış Kaydı (FNS-D12) | Açılış bakiyelerinin **elle** girişi: açık müşteri/tedarikçi kalemi, kasa/banka bakiyesi, portföydeki çek/senet, kredi kalanı, DBS/teminat limiti; "açılış" türüyle işaretli, gerçek tarihçe gibi gösterilmez; maker≠checker; geçici görünürlük (Ayarlar anahtarı) yorumu Sinan teyidi bekliyor | FNS-D12 | K3 |
| API-13 | Dekont/makbuz numara serisi (Ayarlar'da şirket bazlı) — **ertelendi: Ayarlar başlayınca (FNS-D15)** | D-6 | N-12 | K3 |

### 5.3 Tahsilat ve ödemenin tamamlanması (F-02, F-03)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-20 | Ödeme kaynağının dağıtımı | Tedarikçi ödemesini borç kalemine dağıtma (bugün yalnız tahsilat dağıtılabiliyor) | DEC-FIN-006 | K3 |
| API-21 | Avans | Siparişe bağlı dağıtım = avans; fatura gelince kaleme aktarılır (Satış ve Alış avansı) | DEC-FIN-006, master §6 "siparişe dağıtılan tutar avanstır"; Alış N-27 | K3 |
| API-22 | Kur farkı satırı | Çapraz para dağıtımında para olayından türeyen ayrı satır; muhasebe kaynağı | No 34, No 17 | K3 |
| API-23 | Ödeme ters kaydı | Tahsilattaki FIN-T06 deseninin ödemeye uygulanması | DEC-FIN-006 | K3 |
| API-24 | Banka masrafı ve kısmi ödeme | Brüt tutar + ayrı masraf para olayı | No 112, No 115 | K3 |
| API-25 | Masraf ödemesi / iadesi (cariye bağlı olmayan) | Vergi, SGK, maaş, kira, banka masrafı, kredi faizi; kasa/banka hesabı + iki seviyeli masraf türü (FNS-D9) + belge; açık kaleme dağıtılmaz (hubvogal emsali, R-5); FNS-D11 eksi bakiye kontrolü; **Sinan teyidi bekliyor** | §17 R-5, MOD-07, FNS-D11 | K3 |

### 5.4 Kasa ve virman (F-05)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-30 | `cash-accounts` öner/onay/liste | Kasa master (şirket, para birimi, sorumlu) | N-05 (ASSUMPTION-FIN-CASH) | K3 (yeni tablo + yetki) |
| API-31 | `cash-movements` taslak/kayıt/ters | Kasadan tahsilat ve ödeme; açık kaleme dağıtılabilir; eksi bakiye engeli | DEC-FIN-006, FNS-D11 | K3 |
| API-32 | `transfers` preview/commit | Virman: kasa↔banka, banka↔banka, aynı para birimi; döviz alım-satım ayrı tür (N-06, ASSUMPTION-FIN-FX-TRANSFER); eksi bakiye engeli | FNS-D11 | K3 |
| API-33 | `cash-counts`, gün sonu | Sayım, sayım farkı (eşik üstü müdür onayı), gün kapanışı | v3 §5 rol matrisi; eşik OPEN-009 | K3 |

### 5.5 Banka hareketleri, ekstre, mutabakat (F-04, F-16)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-40 | `GET bank-accounts/{id}/book` | ERP defter bakiyesi ve hareketler (para olaylarından hesaplanır, ayrı bakiye tablosu tutulmaz); FNS-D11 kontrolünün okuma tarafı | FNS-D11 | K2 |
| API-41 | `POST bank-statements/import` | Bankadan indirilen ekstre dosyası; dosya hash'i; satır değiştirilmez | OPEN-016 (dosya izinli), N-07 | K3 |
| API-42 | `POST reconciliation/preview`, `/matches`, `/unmatch` | 1:1, 1:n, n:1, n:n eşleştirme; farkı elle kapatma ayrı yetki | master §7.11 | K3 |
| API-43 | Ekstreden kaynak önerisi | Eşleşmeyen ekstre satırından tahsilat/ödeme **taslak önerisi**; kullanıcı açıp kontrol edip kaydetmedikçe hareket yoktur (FNS-D13: ekstre hareket üretmez, kayıt her zaman kullanıcınındır) | DEC-AI-004, FNS-D13 | K2 |

### 5.6 Ödeme yönetimi (F-15)

| ID | Uç | Amaç | Sev. |
|---|---|---|---|
| API-50 | `payment-proposals` | Vadesi gelen borç kalemlerinden öneri (vade, öncelik, nakit) | K3 |
| API-51 | `payment-batches`, `payment-instructions` | Paket ve banka talimatı (dosya/çıktı; banka API yok) | K3 |
| API-52 | `payment-results` | Banka kabul / kısmi / red / iade sonucu; sonuç gelmeden para olayı yok | K3 |
| API-53 | Görev ayrılığı | Hazırlayan, kontrol, onay, uygulayan, mutabakat ayrı (DEC-FIN-012 tablosu, OPEN-009) | K3 |

### 5.7 Çek ve senet (F-06)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-60 | Alınan çek: ciro (tedarikçiye) | Ciro edilen çek tedarikçi borcuna dağıtılır; riskte "ciro edilmiş – rücu riski" | No 109 | K3 |
| API-61 | Alınan çek: bankaya teminat | Riskte "teminatta" sınıfı | No 109 | K3 |
| API-62 | Karşılıksız / iade sonrası | Müşteri alacağı yeniden açılır; ciro edilmişse ciro edilen tarafa borç olayı | No 109 | K3 |
| API-63 | Tahsil sonrası ters | Bugün yok | DEC-FIN-016 | K3 |
| API-64 | **Verilen** çek/senet | Çek defteri (banka hesabı bazlı yaprak) ve senet serisi, kendi çek/senedimizin keşidesi, tedarikçi borcuna dağıtım, vade günü bankadan ödenme, iptal/iade/karşılıksız | DEC-FIN-016, **FNS-D8** | K3 |

### 5.8 DBS (F-07)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-70 | `dbs-agreements` | Banka × firma DBS anlaşması ve limiti | DEC-FIN-008, OPEN-010 | K3 |
| API-71 | DBS talimatı | Fatura/taksit DBS'ye yansıtılır; teminatsız riski ve kullanılabilir DBS limitini azaltır; nakit sayılmaz | DEC-FIN-008, SAT-D52 | K3 |
| API-72 | Banka kabul / red / vade günü tahsilat | Kabulde kapsam, gerçek tahsilatta kesin kapama | v3 §6 | K3 |
| API-73 | Tedarikçi DBS'si (Göymen alıcı) | Banka bazlı DBS limiti, yansıyan faturalar; vade günü bankadan çıkışla borç kalemi kapanır | **FNS-D3** (ikisi de) | K3 |

### 5.9 POS ve şirket kredi kartı (F-07)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-74 | POS batch/valör, chargeback/iade | Banka girişi net, valör ayrı; chargeback alacağı yeniden açar | DEC-FIN-014 | K3 |
| API-75 | Komisyonu müşteri ödesin | Bugün kaydediliyor ama uygulanmıyor; müşteriye ayrı alacak kalemi | DEC-FIN-014 | K3 |
| API-76 | `company-cards` | Kart master (banka, limit, hesap kesim/son ödeme günü) | DEC-FIN-015 | K3 |
| API-77 | Kart harcaması | Slip = harcama kaydı; tedarikçi borcunu kart borcuna dönüştürür; belge, harcayan, masraf türü | DEC-FIN-015 | K3 |
| API-78 | Kart ekstresi ve ödemesi | Ekstre mutabakatı; banka çıkışı ekstre ödemesinde | DEC-FIN-015 | K3 |

### 5.10 Krediler, limitler, teminatlar (F-08, F-09, F-10)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-80 | `credit-facilities` | Kredi tesisi: banka, tür (taksitli ticari, rotatif/spot, KMH/BCH, Eximbank/akreditif), para birimi, tutar, faiz, vade | **FNS-D4** | K3 |
| API-81 | `credit-drawdowns` preview/commit | Kullandırım: bankaya giriş para olayı + borç planı | — | K3 |
| API-82 | Ödeme planı ve taksit ödemesi | Anapara, faiz, BSMV/KKDF ayrı satır; taksit ödemesi para olayı; KMH dönem sonu faiz; akreditif/Eximbank taahhüt bağı | FNS-D4; ayrıntı F7 başında | K3 |
| API-83 | `bank-limits` | Nakdi/gayrinakdi limit, kullanım, boş limit | — | K3 |
| API-84 | `collaterals` | BTM ve diğer teminatlar; verilen/alınan; tahsis ve çözüm | DEC-FIN-011 | K3 |
| API-85 | Müşteri kredi limiti ve teminat master | Satış risk portundaki `NoSource`'u kaldırır; azaltım oranı kapsamları K6, yüzde K7 | SAT-D51, OPEN-010 | K3 |

### 5.11 Tahsilat takibi ve cari risk (F-13)

| ID | Uç | Amaç | Sev. |
|---|---|---|---|
| API-90 | Vadesi geçmiş liste, yaşlandırma | Müşteri gecikme ölçümü (calculator) | K3 |
| API-91 | Tahsilat görevi, ödeme sözü | Görev ve söz kaydı; söz bozulursa uyarı | K3 |
| API-92 | İhtilaf, yapılandırma, hukuki takip | Ayrı kayıt; açık kalemi değiştirmez | K3 |
| API-93 | Risk istisnası | Satış'taki iki tür (No 32) Finans okumasında görünür | K2 |

### 5.12 Nakit planı, bütçe, genel bakış, raporlar (F-01, F-11, F-12, F-17)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-95 | Nakit akışı olay havuzu | Sipariş → fatura → araç → banka aşamaları; çifte sayım engeli | DEC-FIN-013, No 113 | K3 |
| API-96 | 13 haftalık plan | İlk 30 gün günlük; tampon ve kaynak önceliği OPEN-011 açık → ayarlanabilir, başlangıç kapalı | OPEN-011 | K3 |
| API-97 | Nakit bütçesi ve sürüm onayı | Basit sürüm; gelişmiş özellikler bayrakla kapalı | v3 §7 | K3 |
| API-98 | Genel bakış KPI'ları | 6 ana + 3 ikincil (v3 §4.1); hepsi calculator | — | K2 |
| API-99 | Raporlar | Cari yaşlandırma, banka/kasa hareket, çek portföy vade, kredi borç servisi | — | K2 |

### 5.13 Ayarlar sahibinde (küçük eklemeler, iki review)

Finans onay matrisi (işlem × rol × tutar × para birimi × 1./2. onay; DEC-FIN-012, OPEN-009); DBS/teminat azaltım oranı
(OPEN-010, başlangıç 0); kasa sayım farkı eşiği; numara serileri; Finans para profilleri (T02-b). Hepsi Policy Registry
(ADR-SET-002) üzerinden, fail-closed.

---

## 6. Diğer modüllerle eksik ilişkiler (MOD)

| ID | Modül | Eksik | Kim yapar | Sözleşme | Sev. |
|---|---|---|---|---|---|
| MOD-01 | Alış | Kesinleşen gelen faturanın borç kalemi (Alış MOD-04) | Finans tarafı bu şerit; olay üreten Alış | API-10, `purchasing.invoice.finalized.v1` | K3 |
| MOD-02 | Alış | Sipariş başlığında cari bakiye (Toplam Borç/Alacak) ve limit boşluğu | Finans okuma ucu; ekran Alış | API-04 | K3 |
| MOD-03 | Alış | "Ödendi / kısmi ödendi" görünümü; tedarikçi avansı | Finans olayı; tüketen Alış | `finance.open-item.settled.v1`, API-21 | K3 |
| MOD-04 | Satış | Kesinleşen satış faturasının alacak kalemi (Satış MOD-04) | Finans tarafı bu şerit; olay üreten Satış | API-10, `sales.invoice.finalized.v1` | K3 |
| MOD-05 | Satış | Risk portu: yönlü kalem, limit ve teminat kaynağı, DBS güvenceli/teminatsız ayrımı | Bu şerit (port Finans'ta) | `ISalesPartyRiskReadPortV1` (sözleşme değişirse Satış notu) | K3 |
| MOD-06 | Cari | Firmanın hem müşteri hem tedarikçi olması; IBAN (tedarikçi banka hesabı) doğrulaması — **ertelendi: Cari başlayınca (FNS-D15)** | Cari'de küçük ekleme | DEC-CAR-001, DEC-AI-007 | K2 |
| MOD-07 | Muhasebe | Banka masrafı, kur farkı, kasa/virman, kredi faizi için fiş taslağı kaynakları | Muhasebe okuyucusu; Finans kaynak sağlar | Mevcut `...JournalSourceProviderV1` deseni | K3 |
| MOD-08 | Ayarlar | §5.13 politikaları — **ertelendi: Ayarlar başlayınca (FNS-D15)** | Bu şerit (küçük ekleme) | ADR-SET-002 | K3 |
| MOD-09 | Work | Finans onay kuyrukları (ödeme, mahsup, ters kayıt, sayım farkı) tek onay kutusunda | Work deseni | Mevcut Work gözlem portu | K2 |
| MOD-10 | Belgeler | Dekont, ekstre dosyası, çek görüntüsü, kredi sözleşmesi | Documents portu | DEC-DOC-* | K2 |
| MOD-11 | Platform | Outbox dağıtıcısı ERP-000F | Alış şeridi (ALS-D44); Finans tüketir | — | K3 |
| MOD-12 | Üretim | Fason hizmet faturası Alış üzerinden gelir; Finans'a doğrudan bağ yok | — | — | — |

---

## 7. Ekranlar (UI)

Kodlar master §27.11'deki kanonik F-01…F-17'dir. "Var" = bugün route'u olan ekran.

| ID | Ekran | Bugün | Sev. | Bağımlılık |
|---|---|---|---|---|
| UI-01 | F-02 Tahsilat / Ödeme (tek görev merkezi; kaynak türü seçimi: banka, kasa, çek, POS, kart, DBS) | Var (tahsilat + ödeme ayrı) → birleştirilir | K2 | API-20, API-31 |
| UI-02 | F-03 Açık Kalemler ve Dağıtım (yön filtresi, karantina sekmesi, mahsup) | Var (yalnız alacak) | K3 | §3, API-07 |
| UI-03 | Cari ekstre + bakiye özeti (ayrı route) | Gömülü | K2 | API-04/05 |
| UI-04 | F-04 Banka Hesapları ve Ekstre (defter bakiyesi, ekstre, mutabakat) | Kısmen (master) | K3 | API-40…43 |
| UI-05 | F-05 Kasa ve Virman | Yok | K3 | API-30…33 |
| UI-06 | F-06 Çek / Senet (alınan + verilen, ciro, teminat, çek defteri) | Kısmen (alınan) | K3 | API-60…64 |
| UI-07 | F-07 POS / Şirket Kartı / DBS | Kısmen (POS tahsilatı) | K3 | API-70…78 |
| UI-08 | F-15 Ödeme Önerisi ve Banka Talimatı | Yok | K3 | API-50…53 |
| UI-09 | F-13 Cari Risk ve Tahsilat Takibi | Yok | K2 | API-85, API-90…93 |
| UI-10 | F-08 Krediler, F-09 Banka Limitleri, F-10 Teminatlar | Yok | K3 | API-80…84 |
| UI-11 | F-11 Nakit Akışı, F-12 Bütçe/Senaryo | Yok | K2 | API-95…97 |
| UI-12 | F-01 Genel Bakış, F-16 Mutabakat, F-17 Raporlar ve Audit | Yok | K2 | Hepsi |
| UI-13 | "Yeni Finans İşlemi" kataloğu (73 işlem, 22 sık kullanılan; v3 §6) | Yok | K1 | Ekranlar geldikçe |
| — | F-14 Grup İçi Finans | Kapsam dışı (No 114; kanonik durum "referans yok") | — | — |

Her ekranda: yükleniyor, boş, hata, yasak, çakışma, bilinmeyen sonuç; mobil davranış; axe taraması. Mali etkili işlem
tam ekran workspace (adım göstergesi, sağda etki önizlemesi ve kontrol listesi, sabit alt çubuk; v3 UI/UX kararları §4).

---

## 8. Kararlar ve sorular

### 8.1 Bu planın dayandığı kilitli kararlar (değiştirilmez)

DEC-FIN-001…016, DEC-INT-001 (ilk sürümde banka/Mikro entegrasyonu yok), DEC-SET-001, No 22/34/108/109/112–115,
K6/K7/K8/K14, SAT-D51/D52/D85/D96, ALS-D41/D42/D44.

**Roller (EK30, SAT-D196/D197, 2026-11-02):** finans işlemini **Finans Uzmanı oluşturur**, **Finans Müdürü onaylar**
(hazırlayan ≠ onaylayan); **Genel Müdür** finans işlemlerini onaylayabilir ama oluşturamaz. Bütün maker≠checker adımları
(API-07 mahsup, API-23 ödeme ters kaydı, API-31/33 kasa ve sayım farkı, API-50…53 ödeme önerisi/talimat, API-60…64 çek,
API-70…73 DBS, API-77/78 kart, API-80…84 kredi/limit/teminat) bu iki personaya göre izin adı alır; grant insan kapısıdır.

### 8.2 Sinan'a sorular

| No | Soru | Neden | Dalga |
|---|---|---|---|
| N-01 | ~~Hubvogal finans ekranları~~ → **§15'te incelendi** (2026-11-02). Eski soru: Hubvogal'in finans ekranlarının (kasa, banka, çek/senet, kredi, kredi kartı, DBS, cari ekstre) liste ve kayıt ekranı görüntülerini paylaşabilir misin? | Alan, durum ve numara eşlemesi; göç (MIG-02) | F0 |
| N-02 | ~~Şerit~~ → **FNS-D1: hepsi burada** | — | kapandı |
| N-03 | ~~Mevcut kalemler~~ → **FNS-D7: alacak**; alış borç, satış alacak (§16) | MOD-15 migration | kapandı |
| N-04 | ~~DBS yönü~~ → **FNS-D3: ikisi de** | — | kapandı |
| N-05 | Kasa → **ASSUMPTION-FIN-CASH** (hubvogal §15 gözlemi, Sinan kararı değil): şirket bazlı, TL/USD/EUR kasaları; yapılandırılabilir | API-30 | varsayım |
| N-06 | Döviz alım-satımı → **ASSUMPTION-FIN-FX-TRANSFER** (hubvogal §15 gözlemi, Sinan kararı değil): virman içinde "Döviz Alış/Satış" türü (API-34) | API-32/34 | varsayım |
| N-07 | ~~Ekstre~~ → **FNS-D13:** hareket elle girilir; internet bankacılığı dosyası ve MT940 yalnız kontrol/mutabakat için içe aktarılır | API-41 | kapandı |
| N-08 | ~~Verilen çek~~ → **FNS-D8:** müşteri çeki/senedi ciro + kendi çek ve senedimiz | API-60, API-64 | kapandı |
| N-09 | ~~Kredi türleri~~ → **FNS-D4: hepsi** (taksitli ticari, rotatif/spot, KMH/BCH, Eximbank/akreditif); akreditif/Eximbank kapatma ayrıntısı F7 başında sorulur | API-80…82 | kapandı |
| N-10 | ~~Kredi kartı~~ → **FNS-D9: tam takip** | API-76…78 | kapandı |
| N-11 | Ödeme talimatı nasıl gidiyor bugün: internet bankacılığına elle mi, toplu ödeme dosyasıyla mı? | API-51 | F6 |
| N-12 | Dekont/makbuz numarası istenir mi; hubvogal'deki biçim korunsun mu? | API-13 | F2 |
| N-13 | Açılış → **FNS-D12 (karar):** elle girilir. **Yorum (teyit bekliyor):** geçici "Açılış Kaydı" işlemi (açılış dönemi açıkken görünür, maker≠checker) | API-14 | karar kapandı, yorum teyitte |

### 8.3 Açık kalanlar (insan kapısı; plan bunları seçmez)

OPEN-009 (onay matrisi tutarları), OPEN-010 (DBS/teminat azaltım oranları; başlangıç 0), OPEN-011 (nakit tamponu),
OPEN-012 (kur saati), OPEN-016 (entegrasyon erişimi), OPEN-018 (devir temsili). Bunlar yapılandırılabilir ve
fail-closed kalır.

---

## 9. Yeniden kullanılacaklar

- Finans'ın kendi desenleri: `Commands` (ADR-011), maker≠checker (`CK_*_Approval`), ters kayıt önerisi (FIN-T06),
  `occurredOn` dönem kuralı, tam metin tutar, `Down` reddi.
- `BuildingBlocks/Monetary` (`Money`, `ExchangeRate`) ve Satış'taki TCMB önceki iş günü kur okuması (SAT-D54/D55) —
  Finans kendi kur hesabını yazmaz.
- Stok/Work outbox tablo deseni (Alış da aynısını kullanır); Muhasebe'nin `Deliveries` inbox deseni.
- Work onay kuyruğu gözlem portu (`OfficialInvoiceImportWorkDeliveries` emsali).
- Frontend: mevcut `finance/` ekranları, generated client, ortak durum bileşenleri.

---

## 10. Sıralı yol haritası

| Dalga | İçerik | Ön şart |
|---|---|---|
| **F0** | Bu plan; karar dosyası FNS-D1…; `FINANS_ALIS_DEVIR_v1.md` ve `FINANS_SATIS_DEVIR_v1.md` (MOD-01/04/05 sözleşme taslağı); `SISTEM_OF_RECORD_MATRISI.md` ve `DURUM_MAKINELERI.md` Finans satırları; hubvogal finans incelemesi | N-01, N-02 |
| **F1 (öncelik, FNS-D2)** | MOD-15: API-01…06 (yön, karantina, bakiye özeti, ekstre yönü, risk portu), TEK-01/02; UI-02/03 güncellemesi | N-03. **Alış A5'in ön şartı** |
| **F2** | MOD-04: API-10…12 (fatura kabul portu, iptal, Finans outbox/inbox); ~~API-13 numara serisi~~ (ertelendi, FNS-D15); MOD-01…05 sözleşmeleri | F1; Alış/Satış fatura dalgaları paralel ilerler (port fail-closed) |
| **F3** | API-20…25 (ödemenin dağıtımı, avans, kur farkı, ödeme ters kaydı, banka masrafı, cariye bağlı olmayan masraf ödemesi); API-07 mahsup; API-30…33 kasa ve virman; UI-01, UI-05 | F1; N-05, N-06 |
| **F4** | API-40…43 banka defter bakiyesi, ekstre içe aktarma, mutabakat; UI-04 | F3; N-07 |
| **F5** | API-60…64 çek/senet (ciro, teminat, karşılıksız, verilen çek); API-70…73 DBS; API-74…78 POS ve şirket kartı; UI-06, UI-07 | F3; FNS-D3, N-08, N-10 |
| **F6** | API-50…53 ödeme yönetimi; API-85 müşteri limit/teminat; API-90…93 tahsilat takibi; UI-08, UI-09 | F5; OPEN-009 tablosu (yapılandırılabilir) |
| **F7** | API-80…84 krediler, limitler, teminatlar; API-95…97 nakit planı ve bütçe; UI-10, UI-11 | F6; FNS-D4 |
| **F8** | API-98/99, UI-12, UI-13; açılış kontrolü (API-14 ile elle giriş; MIG-02 dosya göçü isteğe bağlı); kabul taraması (AT-004/010/011/014/022/058/065); production aktivasyonu insan kararıdır | Hepsi; N-13 |

Paralellik: F1 ve F2 tek başına Alış ve Satış'ın fatura dalgalarını açar (FNS-D2: önce borç/alacak, sonra kasa/banka).

---

## 11. Kod ve teknoloji düzeltmeleri (TEK) ve temizlik (FZL)

| ID | İş | Neden | Sev. |
|---|---|---|---|
| TEK-01 | `PartyLedgerPolicy` satır türünden sabit taraf yerine kalem yönünden taraf | §3 | K3 |
| TEK-02 | Çıkan ödemeye amaç (müşteriye iade / tedarikçi ödemesi) eklenir; risk portu yalnız iadeyi hesaba katar ve yalnız `Receivable` toplar | D-7, ALS-D42 | K3 |
| TEK-03 | `FinanceAllocationService` yalnız `ReceiptDrafts` okuyor → kaynak soyutlaması (tahsilat, ödeme, kasa, çek, POS, DBS) | DEC-FIN-006 tek dağıtım motoru | K3 |
| TEK-04 | Finance README'yi bölümlere ayır (ödeme, dağıtım, açık kalem, içe aktarma bölümü yok) | Bakım | K1 |
| FZL-01 | `FinanceRecordedReceiptFixtureStore` yalnız test/tarayıcı; host'ta production'da kapalı olduğunun testi | Güvenlik | K2 |

---

## 12. Test planı

| Katman | Ne | Nerede |
|---|---|---|
| Birim | Yön kuralları, ekstre tarafı, bakiye özeti, dağıtım (yön uyumu, kısmi, çapraz para → kur farkı), mahsup, kasa sayımı, çek durum makinesi, DBS, kredi ödeme planı (golden), nakit planı çifte sayım | `tests/GoymenERP.Finance.UnitTests` |
| Gerçek SQL | Şirket izolasyonu, replay, sürüm çakışması, migration (yön doldurma + `Down` reddi), aynı faturanın tek kalem üretmesi, iptalde ters kayıt, mutabakat n:n | `tests/GoymenERP.Api.IntegrationTests` |
| Sözleşme | OpenAPI drift; `IFinanceInvoiceOpenItemAdmissionPortV1` ve risk portu sözleşme testleri; olay sürümleri | `tests/openapi_contract_drift` |
| Tarayıcı | Satış faturası → alacak → tahsilat → dağıtım; gelen fatura → borç → ödeme → dağıtım; aynı firma hem müşteri hem tedarikçi (bakiye özeti, risk etkilenmez, mahsup); çek ciro → karşılıksız | `tests/e2e/finance-fin01` genişlemesi |
| Erişilebilirlik | axe | frontend |

Her PR'da: karar referansı, CompanyId ve yetki testi, audit/idempotency/concurrency etkisi, UI durumları, migration
geri dönüş planı.

---

## 13. Büyüklük, darboğaz ve riskler

| Konu | Not |
|---|---|
| Büyüklük | Finans, Alış ve Satış'tan büyüktür (17 ekran, ~70 uç). F1–F2 kısa ve acildir (Alış'ı açar); F3–F8 asıl gövdedir |
| Darboğaz 1 | F1 (MOD-15) gecikirse Alış A5 ve Satış fatura dalgası bekler → F1 ilk iş |
| Darboğaz 2 | Ayarlar para profili (T02-b) ve izin grant'ları gelmeden hiçbir Finans ekranı gerçek kullanıcıyla çalışmaz (D-1, D-2) |
| Darboğaz 3 | ERP-000F yokken olaylar senkron portla gider (No 55); dağıtıcı gelince geçiş testi gerekir |
| Risk | Hubvogal finans incelemesi salt-okuma ve sınırlıydı (kredi listesi sayfası hata verdi, §15); alan eşlemesi uygulama sırasında Sinan'la teyit edilir |
| Risk | Onay tutarları (OPEN-009) ve azaltım oranları (OPEN-010) açık; ekranlar çalışır ama eşikler başlangıçta "her işlem onaylı" gibi en sıkı ayarda kalır |

---

## 14. Durum (2026-11-02)

Bu bölüm iş takibidir; karar değildir.

| İş | PR | Durum |
|---|---|---|
| Bu plan (v1) + karar dosyası FNS-D1…D13 + hubvogal incelemesi §15 | #896 | taslak PR; kalan sorular N-11, N-12, N-15, N-17, N-18 |
| F1 MOD-15 borç/alacak | #902 | taslak PR; CI ve iki review bekliyor |

Sıradaki (FNS-D10): F1b (API-04 cari bakiye özeti) ve F2 (API-10…12 fatura kabul portu), F1'in üstüne ayrı PR'lar.
Yeni iş (FNS-D12): **API-14 Açılış Kaydı** — geçici, Ayarlar anahtarıyla açılan, maker≠checker açılış girişi (F8'den
öne alınabilir).

---

## 15. Hubvogal finans incelemesi (2026-11-02)

Sinan'ın izniyle hubvogal (iç ağda çalışan web uygulaması; ASP.NET MVC) ofisteki masaüstü bilgisayar üzerinden **yalnız
okunarak** incelendi; kayıt eklenmedi, değiştirilmedi. Oturum kapatıldı, geçici dosyalar silindi. Giriş bilgisi, hesap
numarası, IBAN, bakiye ve firma adları bu dosyaya **yazılmaz**. Hubvogal yalnız referanstır; kör kopya yoktur.

### 15.1 Menü

`Finans`: Kasa Yönetimi · Banka Yönetimi · Çek Yönetimi · Kredi/BCH Hesapları · DBS/Teminat/KK Hesapları.
`Muhasebe`: İrsaliyeler · Fatura Listesi · **Cari Hareketler**. `Raporlar`: **Cari Bakiye Raporu**, fatura raporu.
`Parametreler`: Ödeme Tipleri, Masraf Tipleri (üst/alt iki seviye), Maliyet Tipleri, İskonto Tipleri.
Kredi/BCH Hesapları sayfası inceleme sırasında sunucu hatası verdi; kredi yapısı banka ekranındaki kredi
formlarından çıkarıldı.

### 15.2 Ekran bazında bulgular → Göymen karşılığı

| Hubvogal | Gözlenen | Göymen karşılığı / plan |
|---|---|---|
| **Ortak işlem formu** | İşlem tarihi, Ödeme Türü (vade tipi), **Firma** (bizim grup şirketimiz), **Cari Kur Grubu** (carinin TL/USD alt hesabı), Cari, Kur, **Kur Pozisyonu (Yurtiçi/Yurtdışı)**, İkincil Kur, elle kur bilgisi, işlem tutarı, döviz tutarı, hesap, açıklama, döküman | Şirket = `CompanyId`; para birimi + kur her para olayında zorunlu (FNS-D5); kur önerisi TCMB, elle değişiklik yetki+gerekçe (DEC-FIN-010, K14); döküman = Belgeler portu (MOD-10). Cari Kur Grubu → **N-14**; Kur Pozisyonu → **N-15** |
| **Evrak tipleri (24)** | Satış/Alış Faturası, Ödeme, Gelen/Giden Havale-EFT, Bankalar/Kasalar arası, Kasa↔Banka, Başlangıç Bakiye, Çek Tahsilatı (Banka/Kasa), Kredi Ana Para / Faiz Ödemesi, Kredi BCH Transferi, Masraf Ödeme/İadesi, Çek Ödemesi, DBS/BTM/KK Tahsilatı/Ödemesi, Çek Giriş/Çıkış Bordrosu, **Tahsilat Eşleme / Tediye Eşleme** | Göymen'de bunlar para olayı türleri (kaynak) + ayrı dağıtım (eşleme = DEC-FIN-006 dağıtımı). "Başlangıç Bakiye" = açılış kaydı (API-14 elle giriş, FNS-D12; MIG-02 isteğe bağlı; gerçek tarihçe gibi gösterilmez) |
| **Kasa** | Kasa: ad, para birimi (TL/USD/EUR kasaları var), başlangıç bakiyesi, aktif/pasif. Giriş: Tahsilat, Masraf İadesi; çıkış: Tediye, Masraf Ödemesi; virman: kasalar arası, kasadan bankaya, **Döviz Alış/Satış**. Günlük rapor (devir → işlemler → günlük toplam → gün sonu), kasa ekstresi, tarih bazlı bakiye, para birimi bazında toplam | API-30…33. Gözlem: döviz kasası var → **ASSUMPTION-FIN-CASH** (Sinan kararı değil). Döviz alış/satış ayrı işlem türü → **ASSUMPTION-FIN-FX-TRANSFER** (Sinan kararı değil). İkisi de yapılandırılabilir; Sinan teyidine kadar yürürlüğe girmez (R-2) |
| **Banka** | Banka master (ad, EFT kodu listesi, logo, telefon); hesap: banka, ad, IBAN, para birimi, başlangıç bakiyesi; hesap adında tür: vadesiz, vadeli, POS, factoring. Giriş: Gelen Havale/EFT, Masraf İadesi, Çek Tahsilatı, Kredi Kullan, DBS/BTM/KK; çıkış: Giden Havale/EFT, Masraf Ödemesi, Çek Ödemesi, Kredi Ödemesi, DBS/BTM/KK; virman: bankalar arası, bankadan kasaya, döviz alış/satış; günlük rapor ve ekstre. Bazı döviz hesaplarında **eksi bakiye** görülüyor (kontrol yok) | FS0 banka master'ına hesap türü (vadesiz/vadeli/POS/factoring) eklenir; API-40 defter bakiyesi; eksi bakiye politikası → **N-16** |
| **Çek** | Tek portföy listesi (sayfalı, ~12 sayfa). Alanlar: işlem ve vade tarihi, keşideci ad/soyad, ülke/şehir, çek no, çek bankası, IBAN/hesap no, çeki alan firma (bizim), çeki veren cari, tahsil edilecek banka, kur grubu, para birimi, tutar, döviz tutar, **kalan**. Durumlar: **Portföyde, Bankada, Tahsil Edildi, İade, Ödendi**. Çek giriş/çıkış bordrosu, bankaya **tevdi bordrosu**, toplu işlem, yazdırma. Tahsilat banka veya kasaya; tedarikçiye verilen müşteri çeki "Çek Ödemesi" ile **Ödendi** (ciro) | Mevcut alınan çek modülü (DEC-FIN-016) + API-60 ciro (= hubvogal "Çek Ödemesi"), API-61 teminat, bordrolar (UI-06). "Kalan" alanı kısmi tahsilat gösteriyor → **N-17**. Kendi çekimizin (verilen çek) hubvogal'de ayrı yeri görülmedi → N-08 |
| **Kredi** | Kullandırım türleri: **BCH, Spot, Taksitli**; ödeme ekranında ayrıca **Teminatlı, Çek Teminatlı, Kefalet, Gayri Nakdi**. Alanlar: açılış tarihi, limit, risk, limit boşluğu, faiz oranı, taksit sayısı; taksit planı satırı: **anapara, faiz, BSMV, taksit tutarı**; ödeme anapara ve faiz ayrı evrak; BCH transferi | API-80…83 (FNS-D4). Kredi türü listesi genişler: taksitli, spot/rotatif, BCH/KMH, Eximbank/akreditif + teminatlı/çek teminatlı/kefalet/gayri nakdi (limit türü) |
| **DBS / Teminat / Kredi Kartı** | Tek "limit" ekranı; hesap türü **Teminat / Kredi Kartı / DBS**. **Limit Ver** (Göymen tedarikçiye DBS limiti veya kuruma teminat mektubu verir; limit alan: tedarikçi, vergi dairesi, icra dairesi, OSB…) ve **Limit Al** (müşteriden). Alanlar: banka, banka hesabı, limit alan/veren cari, açılış tarihi, limit, **limit boşluğu**, açık/kapalı; risk satırları (işlem zamanı, doküman no, işlem türü, vade, risk tutarı) | FNS-D3 doğrulandı: tedarikçi DBS'si yoğun. API-70…73 + API-84 (BTM): "limit" modeli DBS ve teminatı ortak taşır, risk satırları vade bazlı. Kredi kartı burada yalnız limit türü; harcama/ekstre takibi görülmedi → N-10 |
| **Cari Hareketler** | Tahsilat, Ödeme, **Ön Ödeme** (avans), elle Satış/Alış Faturası (ara toplam, KDV oranı), **Tahsilat Eşleştir / Tediye Eşleştir** (kapama), Masraf Öde/Al. İşleme **ilgili satış/alış siparişi** bağlanabiliyor. Hesap türü: Çekler / Kasa & Banka / DBS Limitler. Satır başına toplam, tutar, **kalan tutar** (TL ve kur). Üstte para birimi bazında **Toplam Borç / Toplam Alacak**. Ekstre: Borç, Alacak, Sonuç, Vade, Durum; ödeme bilgisi: toplam borç, ödenen, kalan, vade durumu | §3 yön + API-04 bakiye özeti; eşleme = mevcut dağıtım (API-20 ödeme tarafı); ön ödeme siparişe bağlı = API-21 avans. Hubvogal'de elle fatura girişi var; Göymen'de fatura Alış/Satış'ta (ALS-D7, SAT-D7), Finans'ta elle fatura yok |
| **Cari Bakiye Raporu** | Bizim şirket × cari; **TL Cari ve USD Cari ayrı**; TL Borç/Alacak/Bakiye, USD Borç/Alacak/Bakiye | API-04 + UI-03; para birimleri netlenmez (AT-011) |
| **Ödeme Tipleri** | Ad + gün: PEŞİN, 1…120 GÜN, ÇEK (70/75 gün), KREDİ KARTI, ÖN ÖDEME, KREDİLER, "X GÜN DBS" ve "X GÜN DBS VADE" çiftleri | Vade tipi master'ı Satış/Alış'ta (SAT-D86, ALS-D24); Finans yalnız okur. "DBS" / "DBS VADE" farkı → **N-18** |
| **Kurlar** | TRY, USD, EUR; her biri için alış ve satış kuru tutuluyor | FNS-D5; TCMB önceki iş günü (DEC-FIN-010, SAT-D54/55); kur türü Ayarlar'da |

### 15.3 Plana etkisi

- **Dövizli işlem her yerde** (FNS-D5): kasa, banka, çek, kredi, DBS limiti ve cari hareket para birimi + kur + TL
  karşılığı taşır. Kur farkı No 34 ile ayrı satır; dövizli bakiyelerin dönem sonu değerlemesi Muhasebe kaynağıdır (MOD-07).
- **Yeni iş: döviz alış/satış (API-34).** Aynı şirketin iki hesabı arasında, iki para birimi ve işlem kuruyla tek
  işlem; iki para olayı + kur bilgisi; kâr/zarar muhasebe kaynağı.
- **Banka hesap türü (API-44):** vadesiz, vadeli, POS, factoring (FS0'a alan; migration K3).
- **Limit modeli (API-70/84 birleşik tasarım):** DBS ve teminat mektubu "verilen/alınan limit" + vade bazlı risk satırı
  olarak aynı modelde; kredi kartı limiti ayrı (N-10).
- **Çek bordroları (API-65):** giriş, çıkış ve bankaya tevdi bordrosu (yazdırılabilir belge).
- **Kasa/banka günlük raporu (API-45):** devir, işlemler, günlük toplam, gün sonu; tarih bazlı bakiye.
- **Açılış (FNS-D12):** açılış bakiyeleri **elle** girilir (API-14 Açılış Kaydı). Hubvogal'de ~860 sayfa cari hareket ve
  çek portföyü var; dosya göçü (MIG-02) istenirse yalnız yardımcı/kontrol yolu olarak eklenir, zorunlu değildir.

### 15.4 Hubvogal sonrası yeni sorular

| No | Soru |
|---|---|
| N-14 | ~~Cari Kur Grubu~~ → **FNS-D6: tek cari, para birimi bazında bakiye** |
| N-15 | "Kur Pozisyonu: Yurtiçi / Yurtdışı" neyi ayırıyor (ithalat/ihracat, yurtdışı banka, yurtdışı cari)? |
| N-16 | ~~Eksi bakiye~~ → **FNS-D11: engellenir** (KMH/BCH limit içinde hariç) |
| N-17 | Bir çek kısmen tahsil edilebiliyor mu ("Kalan" alanı)? |
| N-18 | Ödeme tiplerindeki "X GÜN DBS" ile "X GÜN DBS VADE" farkı nedir? |

---

## 16. Muhasebe şartlarıyla eşleme (FNS-D7 araştırması)

Sinan'ın isteği: "alış ile satış ayrı olacak biri borç biri alacak sayacak bunu web muhasebe şartlarında da araştır".
Resmî defter Mikro'dadır (DEC-FIN-001); Finans fiş üretmez. Aşağıdaki tablo Göymen kavramlarının Tekdüzen Hesap
Planı (THP, Muhasebe Sistemi Uygulama Genel Tebliği) karşılığıdır; **yalnız anlam eşlemesidir**, hesap kodu ve alt
hesap yapısı mali müşavir teyidiyle Muhasebe modülünün onaylı hesap planı aynasında (ACC) kesinleşir.

| Göymen kavramı | Yön (Finans) | THP karşılığı | Normal bakiye | Not |
|---|---|---|---|---|
| Satış faturası taksiti (açık kalem) | `Receivable` | 120 Alıcılar | Borç | Cari bazında alt hesap (THP 120 ana hesap düzeyinde tutulmaz) |
| Alış faturası taksiti (açık kalem) | `Payable` | 320 Satıcılar | Alacak | — |
| Alınan müşteri çeki | Kaynak (araç) | 101 Alınan Çekler | Borç | Tahsil/ciro/karşılıksız ayrı olay; karşılıksız → alacak yeniden açılır (No 109) |
| Alınan müşteri senedi | Kaynak (araç) | 121 Alacak Senetleri | Borç | Vadesi bir yıldan uzun olanlar uzun vadeli hesaba (muhasebe sınıflaması) |
| Kendi çekimiz (FNS-D8) | Ödeme aracı | 103 Verilen Çekler ve Ödeme Emirleri (−) | Alacak | Keşidede 320 borç / 103 alacak; vadede 103 borç / 102 alacak |
| Kendi senedimiz (FNS-D8) | Ödeme aracı | 321 Borç Senetleri | Alacak | — |
| Kasa | Hesap | 100 Kasa | Borç | Döviz kasası ayrı alt hesap (FNS-D5) |
| Banka hesabı | Hesap | 102 Bankalar | Borç | KMH/BCH eksi bakiyesi kredi niteliğinde (N-16) |
| POS tahsilatı (valör bekleyen) | Kaynak | 108 Diğer Hazır Değerler | Borç | Müşteri brütle kapanır, komisyon gider (DEC-FIN-014) |
| Müşteriden avans | Kaynak (siparişe dağıtım) | 340 Alınan Sipariş Avansları | Alacak | Fatura gelince kaleme aktarılır (API-21) |
| Tedarikçiye avans | Kaynak (siparişe dağıtım) | 159 Verilen Sipariş Avansları | Borç | — |
| Banka kredisi | Hazine | 300 Banka Kredileri (uzun vadeli kısım 400) | Alacak | Faiz ve BSMV ayrı satır (FNS-D4, hubvogal taksit planı) |
| Şirket kredi kartı borcu | Hazine | 309 Diğer Mali Borçlar | Alacak | Harcama tedarikçi borcunu kart borcuna dönüştürür (FNS-D9) |
| Kur farkı (No 34) | Ayrı satır | 646 Kambiyo Kârları / 656 Kambiyo Zararları | — | Dönem sonu dövizli bakiye değerlemesi (VUK 280) Muhasebe kaynağıdır, Finans hesaplamaz |
| Banka masrafı (No 112) | Para olayı | 780 Finansman Giderleri / 770 (muhasebe seçer) | — | Muhasebe kaynağı (MOD-07) |

Sonuç (FNS-D7): Finans'ta yön iki değerlidir ve kaynak belgeden gelir — satış → `Receivable` (cari ekstrede **borç**
tarafı, firma bize borçlu), alış → `Payable` (cari ekstrede **alacak** tarafı, biz firmaya borçluyuz). Aynı firmanın
iki yönlü bakiyesi ayrı görünür, netleşmez; mahsup ayrı işlemdir (ALS-D42). Bu, THP'deki 120/320 ayrımıyla aynıdır.

Kaynaklar: [Tek Düzen Hesap Planı — muhasebetr.com](https://www.muhasebetr.com/tekduzenhesapplani);
[Hesap planı açıklamaları — alomaliye.com](https://www.alomaliye.com/2000/12/26/hesap-plani-aciklamalari);
[Verilen çekler hesabı kayıtları — muhasebenews.com](https://www.muhasebenews.com/?p=165448). Kambiyo (646/656) ve
VUK 280 satırı genel muhasebe bilgisidir; kaynakla doğrulanamadı, mali müşavir teyidi gerekir.

---

## 17. Revizyon v1.1 (2026-11-02) — #896 doküman review P2'leri

| No | Düzeltme |
|---|---|
| R-1 | Açılış metni FNS-D12 ile uyumlu: §1 m.11 ve §15.3 artık "elle giriş (API-14), dosya göçü isteğe bağlı" diyor. "Elle giriş" Sinan kararıdır (FNS-D12). "Geçici Açılış Kaydı düğmesi + maker≠checker" Claude'un yorumudur, **Sinan teyidi bekliyor** (Issue #66 `NEEDS-SINAN`); teyide kadar yapılandırılabilir ve fail-closed. API-14 satırı §5.2'ye eklendi |
| R-2 | N-05 ve N-06 hubvogal gözlemidir, Sinan kararı değildir: **ASSUMPTION-FIN-CASH** ve **ASSUMPTION-FIN-FX-TRANSFER** olarak işaretlendi; yapılandırılabilir kalır ve **teyide kadar yürürlüğe girmez** (kasa master ve döviz alış/satış para davranışına dokunur; AGENTS.md: para politikası tahminle etkinleştirilmez). Issue #66'ya `NEEDS-SINAN` yazıldı; ASSUMPTION hakemliği (ChatGPT) bu ikisi için uygulanmaz, karar Sinan'dadır. §15 Kasa satırı da buna göre düzeltildi |
| R-3 | API-43: ekstre satırından yalnız **taslak önerisi** üretilir; kullanıcı kaydetmedikçe hareket yoktur (FNS-D13) |
| R-4 | **FNS-D11 yorumu (fail-closed, Sinan teyidi bekliyor):** geriye tarihli girişte işlemin `occurredOn` günü ve sonraki her günün defter bakiyesi eksiye düşmemelidir. Alternatif okuma: yalnız kayıt anındaki güncel bakiye. Kredili hesaplar (KMH/BCH, FNS-D4) tanımlı limit içinde istisnadır (FNS-D11). Teyide kadar sıkı (fail-closed) okuma uygulanır; Issue #66'ya `NEEDS-SINAN` yazıldı. Uygulama: API-25/31/32/40 kayıt komutlarında hesap bazında kilitli bakiye kontrolü |
| R-5 | **Cariye bağlı olmayan ödemeler** (vergi, SGK, maaş, kira, banka masrafı, kredi faizi): hubvogal "Masraf Ödemesi/Masraf İadesi" emsali. F3'e **API-25 Masraf ödemesi/iadesi** eklendi: kasa/banka hesabı + masraf türü (iki seviyeli, Ayarlar'da) + belge; cari açık kalemine dağıtılmaz; muhasebe kaynağıdır (MOD-07). Masraf türü listesi FNS-D9'daki iki seviyeli yapıyla aynıdır. Kapsam **Sinan teyidi bekliyor** (Issue #66 `NEEDS-SINAN`); teyide kadar uygulanmaz |
| R-6 | K seviyesi düzeltmesi (v4 tanımı: yeni tablo/migration, para, yetki, modüller arası sözleşme = K3): API-30 (kasa master), API-76 (kart master), API-13 (numara serisi, Ayarlar sözleşmesi) **K3**; API-90…92 ve API-97 yeni tablo içerdiğinden **K3**; API-04 salt okuma, migration ve yeni yetki yok → **K2** kalır; MOD-02/03 Alış ile sözleşme → **K3**. Tablolar (§5, §6) buna göre güncellendi |
| R-7 | §2.4'teki "hubvogal'e erişilemiyor" ifadesi güncellendi (§15'e atıf) |
| R-8 | API-04'ün gerçek yolu: `GET /api/v1/finance/party-balance?partyId&companyAccountId` (PR #912, taslak) |

---

## 18. Modül bağlantıları (2026-11-02, FNS-D14)

Kural: bir modül başka modülün tablosuna yazmaz; bağ yalnız sürümlü port veya outbox/inbox ile kurulur. Her yeni Finans
dilimi PR açıklamasına bir **"Modül bağlantıları"** satırı yazar: ürettiği/tükettiği port veya olay ve karşı modülde
bağlı olup olmadığı. Sinan'ın 2 Kasım kararıyla Ürün, Lojistik/Stok, Ayarlar ve Cari modüllerine şimdi yeni
port/tablo/ekran açılmaz; o bağlar mevcut portlarla kurulur, yoksa fail-closed kalır ve "ertelendi: <modül> başlayınca"
olarak işaretlenir (FNS-D15). Buna göre MOD-06 (Cari), MOD-08 (Ayarlar politikaları) ve API-13 (Ayarlar'da numara
serisi) **ertelendi: ilgili modül başlayınca**; o zamana kadar ilgili davranış fail-closed kalır.

Durum: ✅ main'de implementasyon + DI + gerçek-SQL/contract testi · 🟡 kısmi · ❌ yok.

| Bağ | Üretici → tüketici | Port / olay | Durum | Dilim |
|---|---|---|---|---|
| Satış faturası → alacak kalemi | Satış → Finans | `IFinanceInvoiceOpenItemAdmissionPortV1` (Producer=Sales); dispatcher sonrası `sales.invoice.finalized.v1` | ❌ — Finans portu F2a PR'ında (host kaydı yok); Satış üreticisi fatura dalgasında | F2a + Satış API-32…37 |
| Gelen fatura → borç kalemi | Alış → Finans | aynı port (Producer=Purchasing); sonra `purchasing.invoice.finalized.v1` | ❌ — F1 (#902) yönü, F2a portu; Alış A5 | F2a + Alış A5 |
| Fatura iptali/iadesi → ters kalem | Satış/Alış → Finans | iptal portu / olayı (SAT-D96) | ❌ | F2b |
| Satış risk okuması | Finans → Satış | `ISalesPartyRiskReadPortV1` (`FinanceSalesPartyRiskReadPortV1`) | 🟡 port main'de (DI + SQL testli); yalnız-alacak filtresi #902'de (taslak); limit/teminat NoSource (OPEN-010) | API-06, API-85 (F6) |
| Tedarikçi ödemesi → borç kalemi | Finans içi; Alış okur | elle banka ödemesi + dağıtım | 🟡 ödeme var, borç kalemine dağıtım yok; TEK-02 | F3 (API-20) |
| Ödendi/kısmi ödendi, avans | Finans → Satış/Alış | `finance.open-item.settled.v1`, `finance.payment.recorded.v1` | ❌ — Finans'ta outbox yok | F2 outbox dilimi (ERP-000F sonrası) |
| Muhasebe fişi (120/320) | Finans → Accounting | aynı kaynak olay (FNS-D14); taşıma seçilmedi (outbox/inbox ya da kaynak sağlayıcı) | 🟡 elle tahsilat için salt-okuma `FinanceManualReceiptNormalJournalSourceProviderV1` (Accounting DI'da); satış/alış faturası (120/320) için ❌ | Outbox dilimi + MOD-07 |
| Kur → döviz değerleme | Ayarlar → Finans | `ISettingsExchangeRateResolverV1` (#906, main'de) | ❌ — Finans okumuyor | F7 (API-95) |
| Cari ana veri / uygunluk | Cari → Finans | `IFinancePartyAccountPort` (`CounterpartyFinancePartyAccountPort`) | ✅ mevcut port; yeni alan **ertelendi: Cari başlayınca** (MOD-06) | — |
| Onay kutusu | Finans → Work | mevcut Work gözlem portu | 🟡 yalnız resmî fatura içe aktarma onayı (`FinanceInvoiceImportPendingApprovalAdapterV1`); ters kayıt önerisi Finans'ın kendi onay ucunda, Work kuyruğu yok; ödeme/mahsup kuyrukları yok | MOD-09 |
| Üretim | — | doğrudan bağ yok (MOD-12); maliyet OPEN-008 | — | — |

Eklenen dilim: **F2-OB (Finans outbox).** Açık kalem, tahsilat ve ödeme olayları Finans'ın kendi outbox'ından
yayınlanır; tüketiciler Accounting (fiş), Satış ve Alış (ödeme durumu). Ön şart platform dağıtıcısı ERP-000F (MOD-11);
dağıtıcı yokken olaylar yazılmaz, bağ senkron sürümlü porttur (SINAN 10-21 No 55). K3.

Ekran önerisi (FNS-D14 yorumu, Sinan teyit eder): cari ekstresi (UI-02) ve bakiye özeti (API-04, #912) "Muhasebe > Cari
Hesaplar" girişinden açılır.

Devir notları: `docs/handoffs/FINANS_SATIS_DEVIR_v1.md`, `docs/handoffs/FINANS_ALIS_DEVIR_v1.md`.
