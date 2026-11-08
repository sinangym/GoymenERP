# Sinan Kararları — 2026-11-07 (B-04 faturadan muhasebe fişi ve ek sorular; recovery pin önerisi)

Bu belge önceki karar dosyalarını silmez; KLN-D6…KLN-D10 (B-04 muhasebe fişi), KLN-D11…KLN-D19 (B-04 ek soruları) ve
GOV-D2…GOV-D4 (recovery pin önerisi, #962) ekler. DEC-FIN-001, DEC-FIN-004, FNS-D7, FNS-D14, SAT-D208, ALS-D41,
ALS-D59/D62, KLN-D1…D5 ve GOV-D1 geçerlidir.
Güvenlik değişmezleri (AGENTS.md "Mimari değişmezler", "Yasaklar", K3 kapsamı, insan kapıları) değişmez.

**Kaynak:** Issue #66 yorumu
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6707619685 — Sinan'ın 7 Kasım 2026'da koordinatör
oturumu sohbetinde seçeneklerle verdiği cevaplar; koordinatör oturumu aracılığıyla aktarıldı. B-04 için Sinan'ın
cevabı, aynen: *"Önerildiği gibi"*. Recovery sorularında seçilen seçenekler aynen: **"Evet"**, **"Şimdilik
açılmasın"**, **"Hayır"** (üçü de önerilen seçenek). Aynı yorumdaki not, aynen: *"Sinan "mali müşavirle birlikte"
karar verilmesi gerektiğini biliyor; teyidi bu cevapla verdi. Mali müşavir farklı bir madde isterse Sinan buraya
yazar."*

**Kaynak (ek sorular, KLN-D11…D19):** Issue #66 yorumu
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6707864938 — Sinan'ın 7 Kasım 2026'da koordinatör oturumu
sohbetinde dokuz `NEEDS-SINAN` sorusuna verdiği cevap, aynen: *"Önerildiği gibi"* (iki grubun ikisine de); koordinatör
oturumu aracılığıyla aktarıldı. Aynı yorumdaki not, aynen: *"Mali müşavir farklı bir madde isterse Sinan buraya
yazar."* Sorular: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6707675903 (Kalan Modüller şeridi).

Soruların kaynakları:
- B-04: `NEEDS-SINAN` https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6705676636 (Kalan Modüller
  şeridi); plan `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md` §3 B-04.
- Recovery: `docs/delivery/RECOVERY_PIN_CAKISMA_ONERISI_v1.md` §5 (PR #962) üç `NEEDS-SINAN` sorusu.

"(Önerilen)" işaretleri soruyu soranındır.

## Soru ve cevaplar

### B-04 — faturadan 120/320 taslak fişi

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | 120 satırının tutarı | **Ödenecek toplam (matrah + KDV − tevkifat)** (Önerilen) | Cari satırı tek satırdır ve faturanın tahsil/ödeme edilecek net toplamını taşır. Seçilmeyen seçenek: *brüt tutar ve ayrı bir tevkifat satırı* (120 brüt yazılır, tevkifat ayrı satırla düşülür). |
| 2 | Karşı hesaplar | **Satışta 600 (yurt dışı 601) ve 391; alışta hammadde 150, ticari mal 153, KDV 191, karşılığı 320; alış tevkifatı 360. Hesap numaraları sabit kodlanmaz, Ayarlar'dan ürün türüne göre eşlenir.** (Önerilen) | Yukarıdaki numaralar başlangıç eşlemesidir; kod numara bilmez, eşleme Ayarlar'da ürün türü bazında tutulur. Soruda seçenek olarak "600?", "391?", "153/191/320" soru işaretiyle önerilmişti. |
| 3 | Fiş dönemi hangi tarihe göre | **Fatura tarihinin dönemi** (Önerilen: belge tarihi) | Fiş, faturanın belge tarihinin düştüğü muhasebe dönemine yazılır. |
| 4 | Dövizli faturada fiş | **Fatura tarihindeki kurla TL karşılığı yazılır; döviz tutarı bilgi olarak saklanır** (Önerilen) | Fiş tutarları TL'dir; döviz cinsi tutar fiş satırında bilgi alanı olarak kalır. |
| 5 | Taslak fişi kim oluşturur | **Sistem oluşturur (faturayı kesinleştiren kullanıcı izlenir); muhasebe kullanıcısı onaylar** (Önerilen) | Taslak, fatura kesinleşme olayından sistem adına üretilir; kaynak kullanıcı olarak faturayı kesinleştiren kişi kaydedilir. Fiş, muhasebe kullanıcısının onayıyla kesinleşir (DEC-FIN-004). |

### B-04 ek soruları (#66 yorum 6707675903)

Dokuz sorunun her birinde tek öneri vardı ve Sinan öneriyi seçti; seçilmeyen seçenek "öneri dışında başka bir kural"dır.

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | 150 / 153 ayrımı (ürün türü bugün yalnız Çelik, Hurda, Hizmet; hammadde/ticari mal ayrımı yok) | **Alış satırının amacına göre: üretim girdisi 150, alıp satma 153; hizmet alışı 770 genel gider; hurda satışı 600. Hepsi Ayarlar'daki eşlemeden değiştirilebilir.** (Önerilen) | Alışta stok hesabı (150/153) satırın amacından seçilir; hizmet ve hurda ürün türüne göre eşlenir; eşleme Ayarlar'da değiştirilebilir. |
| 2 | Tevkifatlı satışta 391 | **391'e KDV − tevkifat yazılır** (Önerilen) | 120 = matrah + KDV − tevkifat olduğundan fiş bu şekilde dengelenir. |
| 3 | Birden çok KDV oranı | **391 ve 191 tek hesapta toplanır; oran başına alt hesap istenirse eşlemeden açılır** (Önerilen) | Varsayılan tek hesap; alt hesap Ayarlar eşlemesiyle. |
| 4 | Kuruş farkı | **TL karşılığı satır başına yuvarlanır; toplam farkı faturanın kendi toplamına göre son satırda kapatılır, ayrı hesaba gitmez** (Önerilen) | Fiş toplamı faturanın toplamına eşittir; yuvarlama farkı hesabı açılmaz. |
| 5 | Fatura tarihinin dönemi kapalı veya açılmamışsa | **Fiş reddedilmez, "dönem bekliyor" kuyruğuna düşer; dönem açılınca muhasebe kullanıcısı işler** (Önerilen) | Fatura kesinleşmesi engellenmez; fiş bekler. |
| 6 | Fatura iptali veya iadesi | **Onaylanmamış taslak iptal edilir; onaylı fiş için ters taslak açılır, o da onaya gider** (Önerilen) | Onaylı fiş silinmez/değiştirilmez; ters kayıtla düzeltilir (AGENTS.md "silme yerine ters kayıt"). |
| 7 | Faturayı kesinleştiren kişide muhasebe onay yetkisi de varsa kendi fişini onaylayabilir mi? | **Hayır** (Önerilen) | Kesinleştiren kullanıcı, yetkisi olsa bile kendi fişini onaylayamaz. |
| 8 | İhracat (601) neye göre belirlenir? | **Faturanın ihracat/e-fatura senaryo bilgisine göre** (Önerilen) | 600/601 seçimi fatura senaryosundan gelir. |
| 9 | Kur: "fatura tarihindeki kur" ile ALS-D41 "tedarikçi faturasındaki kur" farklıysa | **Alışta tedarikçi faturasındaki kur (ALS-D41), satışta fatura tarihindeki TCMB kuru** (Önerilen) | KLN-D9'u taraf başına netleştirir. |

### Recovery pin önerisi (#962 §5)

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Bütün backend'in tek bayt özetini PR'da sabit tutma şartı kaldırılıp yerine "dar riskli dosyalar" pin'i + envanter/manifest kaydı konsun mu? | **Evet** (Önerilen: "Evet, ama Faz 0 kuralı yürürlüğe girdikten sonra") | Öneri §4 Faz 1: global `EXPECTED_SOURCE_SURFACE_*` yerine riskli dosya kümesinin (build kontrol dosyaları, csproj/props/targets, DbContext/SQL/migration token'lı `.cs`, `using X =` ve incelenmiş tiplerin `partial` dosyaları) hash'i; tam bayt özeti engelleyici olmadan envanter/manifest çıktısına yazılır. (b) türü incelenmiş pin'ler aynen kalır. Seçilmeyen: Hayır (global pin kalır). |
| 2 | GitHub Settings'te "Require branches to be up to date before merging" açılsın mı? | **Şimdilik açılmasın** (Önerilen) | Aynı güvence merge edenin squash'tan hemen önceki yazılı "head main'i içeriyor + aynı head'de yeşil CI" kontrolüyle sağlanır. Sorun sürerse ayarı Sinan açar. Seçilmeyen: Evet, açılsın. |
| 3 | Main'e otomatik commit atan, yazma yetkili bir bot (token) | **Hayır** (Önerilen) | Secret gerektirir; bot commit'i CI'dan ve review'dan geçmeden main'e yazar. Seçilmeyen: Evet. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D6 | B-04 — 120/320 cari satırı | Faturadan üretilen taslak fişte cari satırı (satışta 120, alışta 320) **ödenecek toplamı** taşır: matrah + KDV − tevkifat. Ayrı brüt cari satırı ve ayrı cari tevkifat satırı yoktur. | Plan §3 B-04, §6.1; Accounting fiş kaynak sağlayıcısı; `sales.invoice.finalized.v1` (SAT-D208), `purchasing.invoice.finalized.v1` (ALS-D59/D62) tüketicisi |
| KLN-D7 | B-04 — karşı hesaplar | Başlangıç eşlemesi: **satış** gelir 600 (yurt dışı satış 601), hesaplanan KDV 391; **alış** hammadde 150, ticari mal 153, indirilecek KDV 191, karşılığı 320, alış tevkifatı 360. Hesap numaraları kodda **sabit yazılmaz**; Ayarlar'da **ürün türüne göre** eşlenir (AGENTS.md "magic number" yasağı). Ayrıntılar KLN-D11…D13, D18. | Ayarlar (Ana Veriler › parametre, KLN-D2); Accounting; Ürün türü ana verisi |
| KLN-D8 | B-04 — fiş dönemi | Fiş, **fatura tarihinin** düştüğü muhasebe dönemine yazılır. Kapalı/açılmamış döneme düşen fatura için KLN-D15 geçerlidir. | Accounting dönem kontrolü |
| KLN-D9 | B-04 — dövizli fatura | Fiş tutarları **fatura tarihindeki kurla TL** karşılığıdır; döviz cinsi tutar bilgi olarak saklanır. Kurun hangisi olduğu taraf başına KLN-D19 ile netleşir. | Accounting fiş satırı; ExchangeRate value object |
| KLN-D10 | B-04 — taslak ve onay | Taslak fişi **sistem** oluşturur; faturayı kesinleştiren kullanıcı kaynak kullanıcı olarak izlenir. Fiş **muhasebe kullanıcısının onayıyla** kesinleşir; elle ikinci kayıt yapılmaz (DEC-FIN-004, SAT-D208, FNS-D14). Onay yetkisinin hangi role verileceği bu kararla seçilmez (grant/rol insan kapısı). Kendi fişini onaylama yasağı KLN-D17'dedir. | Accounting onay komutu; permission kaydı (grant yok) |
| KLN-D11 | B-04 — 150/153, hizmet ve hurda hesapları | Alışta stok hesabı satırın **amacından** seçilir: üretim girdisi → 150, alıp satma → 153. Hizmet alışı → 770 genel gider. Satışta hurda satışı → 600. Hepsi Ayarlar'daki eşlemeden değiştirilebilir (KLN-D7). | Accounting eşleme; Alış/Satış fatura satırı (LinePurpose) |
| KLN-D12 | B-04 — tevkifatlı satışta 391 | Tevkifatlı satış faturasında 391'e **KDV − tevkifat** yazılır (KLN-D6 ile denge: 120 = matrah + KDV − tevkifat, 600/601 = matrah). Alıcının kestiği tevkifatın ayrıca hangi hesapta izleneceği açıktır (Sınırlar). | Accounting fiş satırı |
| KLN-D13 | B-04 — birden çok KDV oranı | 391 ve 191 **tek hesapta** toplanır; oran başına alt hesap istenirse Ayarlar eşlemesinden açılır. | Accounting eşleme |
| KLN-D14 | B-04 — kuruş farkı | TL karşılığı **satır başına** yuvarlanır; toplam farkı faturanın kendi toplamına göre **son satırda** kapatılır; ayrı (yuvarlama farkı) hesaba gitmez. **Satış faturasında** (düzeltme: SAT-D223, EK42, #966 merge edildi) kapatma **KDV oranı grubu başına** yapılır: grubun TL toplamına göre fark, grupta o tutar türünü taşıyan son satıra eklenir; istisna/%0 kendi grubudur (SAT-D223). **Alışta** yukarıdaki ifade aynen geçerlidir (KLN-D14). | Accounting fiş üretimi |
| KLN-D15 | B-04 — kapalı veya açılmamış dönem | Fatura tarihinin dönemi kapalı veya açılmamışsa fiş **reddedilmez**, "dönem bekliyor" kuyruğuna düşer; dönem açılınca muhasebe kullanıcısı işler. Kapalı dönem kaydı değiştirilmez (DEC-FIN-004). | Accounting dönem kontrolü, kuyruk ekranı |
| KLN-D16 | B-04 — fatura iptali/iadesi | Onaylanmamış taslak fiş **iptal edilir**; onaylı fiş için **ters taslak** açılır ve o da onaya gider. | Accounting; `sales-invoice-cancel:…` / `purchasing-invoice-cancel:…` kabulü |
| KLN-D17 | B-04 — kendi fişini onaylama | Faturayı kesinleştiren kullanıcı, muhasebe onay yetkisi olsa bile **kendi fişini onaylayamaz**. | Accounting onay komutu (görev ayrılığı kontrolü); yetki testi |
| KLN-D18 | B-04 — ihracat (601) | 600/601 seçimi faturanın **ihracat/e-fatura senaryo** bilgisine göre yapılır. | Satış fatura olayı; Accounting eşleme |
| KLN-D19 | B-04 — kur, taraf başına | **Alışta** tedarikçi faturasındaki kur (ALS-D41) aynen geçerlidir. **Satışta** (düzeltme: SAT-D226, EK42, #966 merge edildi) fiş kuru, faturaya yazılan sabit kurdur: sevkten önceki iş günü kuru (SAT-D90) faturaya SAT-D172 ile sabitlenir; muhasebe fişi yeniden kur çekmez (SAT-D226, SAT-D172). "Fatura tarihindeki TCMB kuru" ifadesi bu düzeltmeyle geçersizdir (SAT-D226). KLN-D9'u taraf başına netleştirir. | Accounting fiş satırı; ALS-D41; Satış fatura kuru |
| GOV-D2 | Recovery — global bayt pin'i | `EXPECTED_SOURCE_SURFACE_FILE_COUNT` / `_FINGERPRINT` global bayt pin'i, #962 §4 Faz 1'deki **dar riskli-yüzey pin'iyle** değiştirilir; tam bayt özeti engelleyici olmadan envanter/manifest çıktısına (`sourceSurface`) yazılır; (b) türü incelenmiş pin'ler ve pin'den bağımsız kontroller aynen kalır. **Ön koşul:** GOV-D3'teki Faz 0 merge kuralı AGENTS.md'ye girmiş (bu PR merge edilmiş) olmalıdır. Faz 1 kod PR'ı K3'tür, iki bağımsız exact-head review ister. Kapsanmayan girdilerin (`appsettings*.json`, `.resx`, `.cs` dışı metin) dar kümeye alınması Faz 1 PR'ında karara bağlanır — uygulama ayrıntısı (geri alınabilir). | `tools/generate_recovery_surface_inventory.py`, `contracts/recovery/*.schema.json`, `tests/recovery_surface_inventory`, `tests/release_manifest` |
| GOV-D3 | Recovery — "up to date" ayarı ve Faz 0 kuralı | GitHub Settings'te "Require branches to be up to date before merging" **şimdilik kapalı kalır**. Aynı güvence yazılı merge kuralıyla sağlanır (Faz 0): `src/backend/`, migration, kök build-control dosyaları (`REPOSITORY_BUILD_CONTROL_PATHS`) veya recovery envanterine dokunan PR'ı merge eden **herkes** (`CODEX-ORCHESTRATOR` ve Claude şeridi) expected-head squash'tan hemen önce `git fetch origin main <pr-branch> && git merge-base --is-ancestor origin/main <expected-head>` çalıştırır, main SHA'sını ve exit code'u merge makbuzuna yazar; fresh zorunlu CI aynı head'de yeşil olmalıdır; exit ≠ 0 ise main merge edilir, pin'ler yeniden üretilir, CI yeniden koşar. Kuralın metni AGENTS.md ve `.claude/skills/review-signatures/SKILL.md` §4'tedir. Sinan'ın metni "koordinatörün" der; kuralın Claude şeridini de kapsaması #962 §4 Faz 0'dan gelir — uygulama ayrıntısı (geri alınabilir). **Kapsam** (yalnız `src/backend/`, migration, kök build-control ve recovery envanteri girdilerine dokunan PR'lar) da bir uygulama ayrıntısıdır: bu yollar recovery generator'ının ve global pin'in bugün kapsadığı girdilerdir, dolayısıyla "aynı güvence" bu yollar için korunur; yalnız frontend/doküman PR'larına genişletmek geri alınabilir bir tercih olarak açık kalır. Settings değişikliği insan kapısıdır. | AGENTS.md merge kuralları; `review-signatures` §4 |
| GOV-D4 | Recovery — yazma yetkili bot | Main'e otomatik commit atan, yazma yetkili token'lı bot **kurulmaz** (#962 Seçenek C reddedildi). | `.github/**`; secret yüzeyi açılmaz |

## Sözleşme notları (şeritlerle uzlaşılan; Sinan kararı değil — uygulama ayrıntısı, geri alınabilir)

Koordinatörün aktardığı ve Satış/Alış/Finans şeritleriyle uzlaşılan teknik notlar:

- Kabul anahtarları: alış `purchasing-invoice:{id}:{version}` / `purchasing-invoice-cancel:…`; satış
  `sales-invoice:{invoiceId}:{invoiceVersion}` / `sales-invoice-cancel:…` (plan §12a).
- Finans kabulü ve Muhasebe kabulü **iki bağımsız bekleyen eksendir**; biri diğerini beklemez veya bloke etmez.
- Fatura satırı isteğe bağlı `ItemType?` / `LinePurpose?` taşır. Eşleme bulunamazsa satır **PostingException**
  kuyruğuna düşer; hesap asla tahmin edilmez; fatura kesinleşmesi engellenmez (Alış tarafı fail-closed yaklaşımını
  bıraktı).
- Satış tutarları satır başına yuvarlar; Muhasebe tutarları yeniden hesaplamaz, gelen satır tutarlarını kullanır
  (KLN-D14 TL karşılığı yuvarlaması ve son satır farkı bunun üzerine uygulanır). Satış kaynakları (Satış şeridi teyidi,
  7 Kasım): SAT-D11 (satır tutarı 2 ondalık, AwayFromZero; KDV ve tevkifat satır bazında, toplamlar satırlardan),
  SAT-D76 (yalnız satır tutarı bir kez yuvarlanır), SAT-D75 (tevkifat = kuruşa yuvarlanmış satır KDV'si × oran, kuruşa
  yuvarlanır; tevkifatlı satırda alanlar dolu gelir), SAT-D172 (faturanın kuru sabittir). SAT-D206/D207 risk döviz
  çevrimi içindir, faturaya uygulanmaz.

## Cevaplanan sorular

- B-04 `NEEDS-SINAN` (#66 yorum 6705676636): beş madde KLN-D6…KLN-D10 ile cevaplandı. B-04'ün kodlanması bu kararla
  açılır (K3).
- B-04 ek `NEEDS-SINAN` (#66 yorum 6707675903): dokuz madde KLN-D11…KLN-D19 ile cevaplandı.
- FNS-D7'deki "120/320 eşlemesi mali müşavir teyidine kadar yalnız anlam eşlemesidir" şartı: Sinan teyidi bu cevapla
  verdi. Mali müşavir farklı bir madde isterse Sinan #66'ya yazar; o durumda yeni bir karar dosyası bu kararların
  yerine geçer.
- `docs/delivery/RECOVERY_PIN_CAKISMA_ONERISI_v1.md` §5 soruları 1–3: GOV-D2…GOV-D4 ile cevaplandı.
- `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md` §11 K-2 (fiş taşıması) teknik/ASSUMPTION olarak kalır; bu
  kararla değişmez.
- **`OPEN-*`:** Bu belge hiçbir `OPEN-*` kimliğini kapatmaz. En yakın kayıt `OPEN-016` (dış muhasebe/e-belge/banka
  entegrasyonu, Mikro'ya kullanıcı onaylı fiş taslağı) dış entegrasyonla ilgilidir; KLN-D6…D10 ERP içi taslak fişi
  düzenler ve `OPEN-016`'nın açık kısmını (sağlayıcı, credential, erişim yöntemi) cevaplamaz. `ACIK_KARARLAR.md`
  ve `KARAR_SICILI.md` bu PR'da değişmez (EK39/GOV-D1 dosyalarındaki uygulama).

## Sınırlar

- **Satış tarafı tevkifat karşı hesabı** (satış faturasında alıcının kestiği KDV tevkifatı 120'den düşülünce hangi
  hesapta izlenir) karara bağlı değildir; KLN-D7 eşleme altyapısı bunu yapılandırılabilir tutar. Açık
  `NEEDS-SINAN`: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6707860683 (öneri: 136 Diğer Çeşitli
  Alacaklar altında "tevkif edilen KDV" alt hesabı, Ayarlar'dan eşlenir). Cevap gelene kadar eşleme yoksa fiş kuyruğa
  düşer; tahminle seçilmez.
- Ters fişin dönemi (orijinal fatura dönemi mi, iptal/iade tarihinin dönemi mi) ve kur farkı muhasebeleştirmesi bu
  kararla seçilmez.
- Hesap planının kendisi, ürün türü listesi ve fişin resmî sisteme (Mikro) aktarımı bu kararın dışındadır
  (DEC-FIN-001, DEC-INT-001).
- GOV-D2 Faz 1 kod değişikliği bu belgeyle yapılmaz; ayrı K3 PR'ıdır. Faz 2–3 (#962 §4) seçilmemiştir.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme ve Settings'teki branch
  koruma ayarları, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
