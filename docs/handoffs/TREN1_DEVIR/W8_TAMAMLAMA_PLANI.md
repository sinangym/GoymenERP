# TREN-1 sabit kapsam tamamlanma planı — 17 Kasım 2026

Kullanıcının güncel talimatı: mevcut işleri bitir, yeni dalga veya kapsam açma;
geliştirme yüzde 100 olunca dur. W8 son çalışma paketidir; W9 açılmaz.
Bu kayıt işletme kararı, production onayı veya bağımsız inceleme değildir.

## Sabit başlangıç ve ölçüm

- Trene alınmış başlangıç: `df1b7055f9397cd5206b57df439986d1e8344b4e`.
- Ana plan: **174 dilim; 135 DONE (%77,6), 23 PARTIAL, 16 diğer açık**.
- Payda 174 kalır. Kayıtlı EK24–EK28 takipleri ve W5 düzeltmeleri ilgili mevcut
  dilimin tamamlanma koşuludur; yeni dilim sayısı veya dalga üretilmez.
- Bir iş yalnız kodu, gerekli test kaynakları ve migrationı tamamlanıp derlenerek
  trene alınınca kaynak DONE olur. Ekip WIP'i ve yalnız calculator yazılması sayılmaz.
- Kaynak yüzdesi ile test/SQL/browser/CI/review kabulü ayrı raporlanır.

## Kalan 39 dilim

| Ekip | Sabit açık kimlikler | Adet |
|---|---|---:|
| kimlik | KIM-S5, KIM-S6, KIM-S7, KIM-S8, KIM-S9, KIM-S15, KIM-S19 | 7 |
| satis | SAT-S9, SAT-S12, SAT-S13, SAT-SC34 | 4 |
| alis_cari | ALC-S5, ALC-S6, ALC-S7, ALC-S8, ALC-S14, ALC-SC47 | 6 |
| stok_lojistik | STK-S3, STK-S8, STK-S11, STK-S13, STK-S19, STK-S21, STK-S29, STK-SC36-a, STK-SC36-b, STK-SC36-c | 10 |
| uretim | URT-S4, URT-S5, URT-S8, URT-SC32, URT-SC33, URT-SC36 | 6 |
| finans | FIN-S13, FIN-S14, FIN-S17, FNS-SC32, FIN-SC36, FIN-SC47-KREDI | 6 |

Platform ana planı 16/16'dır; KLN-D114 bildirim deposu ve değiştirilebilir 90 gün
başlangıcı kayıtlı karar takibidir. Bu takip, platformun eski belge senkronu DONE
satırının çalışma kabulü olarak gösterilmez; ayrı açık kontrol olarak korunur.

## Sıra ve sorumluluk

1. Hazır W8 şirket açma/ilk yönetici ve üretim fiziksel kg/kalıcı kuyruk kaynaklarını
   final derleme ve entegrasyonla tamamla. Mevcut karar dışına çıkma.
2. Purchasing kabul geçmişi/net düzeltme, Inventory tersleme ve host transactionını
   birlikte tamamla; aynı idempotency Attempts kaydından immutable başarı/ret replay.
   Sonra bu portlara bağlı satış kaynak planı ve iade ekranlarını bağla.
3. Kimlik/Settings, üretim, stok/lojistik ve kök satın alma/satış/finans sahipleri
   tablodaki kalan gerçek boşlukları doldurur. Önce mevcut uygulama doğrulanır;
   yazılmış davranış yeniden yazılmaz. Aynı dosyada ikinci yazar ve paralel aynı-context
   migration yoktur. Tek .NET/EF slotu koordinatörden alınır.
4. Maliyet/kur, faturadan muhasebe, dönem/devretme, kredi ve satır SMM düzeltmesi
   gerçek owner portları ve transaction ile bağlanır. Ürün sınıfı hareketten tahmin
   edilmez. Eksik veri sıfır, sahte provider veya başarı sayılmaz.
5. W5'ten kalan source alignment, tenant bypass ve Audit sahipliği açıklarını kapat;
   test atlama/silme/karantina yoktur. Gerekçeli mevcut K3 inceleme listesi korunur.

## Karar ve belge kapıları

- SAT-D421 takip sorusu 17 Kasım'da [6929718990](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6929718990)
  aktarımıyla yanıtlandı: kesinleşmiş dış belge yokken hesaplanan irsaliye dağılımının
  artığı eşit en ağır satırlar içinden ilk satıra verilir. Yeni SAT-D424 / #1198
  belgesi hazırlanmıştır; önce irsaliye, irsaliyelerden fatura normal akışı korunur.
  Kesinleşmiş gerçek dış belge dağılımı yeniden hesaplanmaz.
- #1194/#1195/#1196/#1197 güncel head'lerinde iki DOCS APPROVE doğrulandı.
  Bu head'lere yazılmaz; merge ayrı yetkili koordinatördedir. Sıra
  #1194 → #1196 → #1197; #1195 bağımsız. Makbuz güncel AGENTS.md gereği #1189'a gider.
- Engellemeyen atıf/tarihçe notları tek yeni takip belgesinde ele alınır; önceki
  karar dosyaları yerinde düzenlenmez. Vergi mevzuatı kaynağı resmi metinden
  doğrulanır; mevzuat yeni ürün/hesap/beyanname politikası icat etmez.
- Kredi alan taslağının insan onayı, gerçek rol/grant, OPEN kapatma ve production
  bu planın otomatik tamamlanma işlemi değildir. Karar bekleyen kapsam DONE yapılmaz.

## Kod bitince doğrulama ve durma

1. Kaynak listesinde 174/174 DONE ve kayıtlı karar takiplerinde açıklanmamış kod
   eksiği olmamalı. Açık insan kapıları ve eksik kanıt ayrı, açıkça raporlanır.
2. Güncel main'i izin verilen merge yöntemiyle al; kaynak çatışmasında geçerli dar
   istisna dışına çıkma. Tüm zorunlu backend/test projeleri ve frontend derlenir.
3. Shell → OpenAPI → recovery/release → mimari guard → ilişkisel envanter/grup
   üreticileri sırayla çalıştırılır. Yeni bypass yalnız testi yeşile çevirmek için
   allowlist'e eklenmez. Hafif doğrulayıcıların exit/skip değerleri kaydedilir.
4. Kod bittikten sonra tam birim/mimari/OpenAPI drift/frontend testleri ve mevcut
   onaylı gerçek SQL kabulü koşulur; eski kırmızılar karara göre düzeltilir. Yeni
   DB/container/servis gereksinimi somut kullanıcı izni olmadan uygulanmaz.
5. Tren → main için tek taslak K3 PR hazırlanır. CI yalnız bu son PR'da koşar;
   kaynak/test/review/SQL/browser kanıtları ve açık insan kapıları ayrılır.
   Yazar merge etmez; iki tam bağımsız exact-head K3 incelemesi sonraki kapıdır.
6. Tamamlanma raporu ve inceleme/devralma istemi bırakılır, yeni geliştirme kapsamı
   açılmadan durulur. Kullanıcının sonraki genel analizi bu çalışmaya eklenmez.
