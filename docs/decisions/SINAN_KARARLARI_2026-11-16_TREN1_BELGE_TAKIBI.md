# Sinan kararları — 16 Kasım 2026 tren-1 belge takibi

Bu belge, kullanıcının 16 Kasım 2026 tarihli devir güncellemesindeki engellemeyen belge
düzeltmelerini tek dosyada toplar. Önceki karar dosyaları yerinde değiştirilmez. Bu kayıt
yeni işletme kararı vermez, bir `OPEN-*` kaydını veya aşağıdaki soruları kapatmaz.
Kodun, testin, gerçek SQL kabulünün, CI'ın ve bağımsız incelemenin durumu bu belgeyle yükselmez.

## Kaynakla doğrulanan metin düzeltmeleri

| Konum | Doğru okuma ve kaynak |
|---|---|
| EK16 girişindeki “ilk altı” | EK15 Sınırlar listesinin **1–4, 6 ve 7** numaralı maddeleri kastedilir. **5 numaralı URT-D95 tüketilen miktar/formül eşlemesi EK16 tarafından cevaplanmaz.** EK16'nın yayımlandığı andaki kapsam budur; daha sonraki URT-D108 ve URT-D109 ayrıca okunur. Kaynak: [EK15](SINAN_KARARLARI_2026-11-16_DALGA4_EK15.md) Sınırlar, [EK16](SINAN_KARARLARI_2026-11-16_DALGA4_EK16.md) karar satırları. |
| EK21 soru 45.2 | Kaynak sorunun aynısı: **“Banka açılışı eksi (KMH/kredili) girilebilir mi?”** EK21'deki “Banka hesabı eksi bakiyeye düşebilir mi?” kaynak sorunun sözcüğü sözcüğüne alıntısı değildir. Serbest cevap değişmez; FNS-D87'nin ayrıca yazılmış karar hükmü bu editoryal düzeltmeyle geri alınmaz. Kaynak: [SINAN-CEVAP 45, 45.2](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6911663004). |
| EK21 FNS-D87 içindeki FNS-D11 alıntısı | FNS-D11 açıklaması: **“KMH/BCH gibi kredili hesaplar hariç, bakiye eksiye düşecek işlem kaydedilmez”**. EK21'de alıntının içine giren “tanımlı limit içinde” sözcükleri FNS-D11 metninde yoktur. FNS-D87'nin eski KMH/BCH istisnasını kaldıran yeni kararı aynen kalır. Kaynak: [FNS-D11](SINAN_KARARLARI_2026-11-02_FINANS.md), [EK21 FNS-D87](SINAN_KARARLARI_2026-11-16_DALGA4_EK21.md). |
| EK22 soru 47.4 seçeneği | Kaynakta seçilen seçenek **“Giriş tarihi TCMB döviz satış (Önerilen)”**dir. Seçenek başlığındaki eksik “döviz” sözcüğü böyle okunur; açıklama ve FNS-D92'deki döviz satış türü değiştirilmez. Kaynak: [SINAN-CEVAP 47, 47.4](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6912114197). |
| EK22 çevrim atfındaki FNS-D73 adı | FNS-D73'ün kaynak başlığı **“Maliyet çevriminde kur”**dur. FNS-D83 ve FNS-D92 ayrı kimliklerdir; bu başlık düzeltmesi onların numarasını veya hükmünü değiştirmez. Kaynak: [EK12 FNS-D73](SINAN_KARARLARI_2026-11-15_DALGA2_EK12.md), [EK22 FNS-D92](SINAN_KARARLARI_2026-11-16_DALGA4_EK22.md). |
| SAT-D419 ticari mal / mamul | Kaynakta iki ayrı varsayılan vardır: **ticari mal 621**, **mamul 620**. Tek hesapmış gibi okunmaz. Finans eşlemesini değiştirme yetkisi W5-5.3'te mevcut Finans eşleme iznine bağlanır ve değişiklik denetlenir. Bu kayıt hesap eşlemesi kurmaz veya grant vermez. Kaynak: [EK25 SAT-D419](SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md), [W5-5.3](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6920278065). |

## Eski “henüz merge edilmemiş” listeleri

EK16–EK25'teki PR bağımlılık listeleri, yazıldıkları andaki durumu anlatır. Devirdeki
15 karar PR'ının tamamı main'e alınmıştır: #1165, #1171, #1175, #1179, #1180, #1182,
#1184, #1185, #1186, #1187, #1188, #1190, #1191, #1192 ve #1193.
Bu doğrulamanın main head'i `8b680d1c7a3886fba2766de9b2443e2c6b916c71`'dir.
Kaynak: [Issue #1189 son dört merge makbuzu](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6921286433)
ve [hafif kontrol ek kaydı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6921392736).
Tarihsel karar metinleri geriye dönük düzenlenmez; PR durumları ileride yeniden değişebilir.

## Cevabı olmayan uygulama soruları

Aşağıdakiler karar değildir. Kaynaklar birlikte gösterilir; cevap gelene kadar ilgili açık
para, formül, numaralama veya saklama seçimi tahminle etkinleştirilmez. Bağımsız işler sürer.

| Kimlik | Kaynak ve açık soru |
|---|---|
| BELGE-Q1 | [EK18 FNS-D79](SINAN_KARARLARI_2026-11-16_DALGA4_EK18.md) SAT-D54/SAT-D170 ile kesin önceki iş gününü ve arife kuralını söyler; [EK23 FNS-D98](SINAN_KARARLARI_2026-11-16_DALGA4_EK23.md) ve [48.1 cevabı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6914140908) işlem tarihindeki TCMB döviz satışını söyler. **Siparişin otomatik kuru SAT-D54/SAT-D170 önceki iş günü algoritmasıyla mı, doğrudan işlem tarihinin yayınıyla mı bulunacak?** |
| BELGE-Q2 | [SAT-D322 (3)–(4)](SINAN_KARARLARI_2026-11-13_URETIM_MALIYETI_VE_EKSI_STOK.md) irsaliyede farkın tamamını en ağır kaleme (eşitse ilk kaleme) yazar; [EK24 SAT-D409](SINAN_KARARLARI_2026-11-16_DALGA5_EK24.md) fatura satırlarına teorik kg oranında dağıtım söyler. **SAT-D409 yalnız faturadaki dağıtımı mı değiştiriyor; irsaliyedeki SAT-D322 (3)–(4) aynen kalıyor mu?** Eşit en ağır fatura satırında artığın hangi satıra yazılacağı da açıkça teyit edilmelidir. |
| BELGE-Q3 | [EK24 KIM-D53](SINAN_KARARLARI_2026-11-16_DALGA5_EK24.md) N-12 numaralarını önek + yıl + sıra biçiminde tanımlar. [ALS-D36](SINAN_KARARLARI_2026-11-02_ALIS_EK3.md) alış siparişi numarasında eski algoritmayla yeniden başlanabilmesini söyler. **KIM-D53 alış siparişi numarasını da kapsayıp ALS-D36 algoritmasının yerini alıyor mu?** |
| BELGE-Q4 | [EK23 KLN-D111](SINAN_KARARLARI_2026-11-16_DALGA4_EK23.md) süreyi Ayarlar'dan okutur; [EK25 KLN-D114](SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md) 90 gün sonra pasife almayı söyler. **90 gün Ayarlar'dan değiştirilebilir başlangıç değeri mi, sabit pasife alma süresi mi?** Kalıcı silme kapalı kalır; audit bildirimin yaşam döngüsünden ayrıdır. |
| BELGE-Q5 | [EK25 SAT-D419](SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md) ticari mal 621 / mamul 620 varsayılanlarını ayırır. **SMM farkında ticari mal / mamul ayrımı hangi mevcut ürün niteliği veya kayıt türünden okunacak?** Bu kayıt ürün sınıfı, hesap planı veya şirket eşlemesi seçmez. |

## Sınırlar

- Bu dosya yalnız kaynaklı belge düzeltmesi ve cevap bekleyen soru kaydıdır; yeni karar numarası üretmez.
- Eski dosyalar, karar sicili, master, backlog durumu ve `OPEN-*` kayıtları değiştirilmez.
- Para/formül/kur, stok, üretim, yetki, migration ve saklama politikası için eksik cevap tahmin edilmez.
- Deployment, secret, environment, grant/rol ve production aktivasyonu insan kapısında kalır; Finance STOP değişmez.
- İki bağımsız exact-head DOCS incelemesi beklenir; yazar kendi inceleme imzasını vermez.
