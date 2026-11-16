# Finans — Dalga 6 kaynak raporu

| Dilim / karar | Durum | Kaynak değişikliği |
|---|---|---|
| FIN-SC47-KREDI / FNS-D109 | PARTIAL | `ec7111ccc`: yalnız finance.credits.approve Critical; propose/read değil. Yeni rol/grant yok. |
| FIN-SC47-KREDI / FNS-D88 | PARTIAL | `88e6c2cdc`: tanımsız kredi grubu strict JSON hash'i hesaplanmadan, yetki çitinden sonra validation_failed olarak reddedilir. Geçerli isteklerin hash'i değişmez; mevcut olumsuz teori korunur. Migration tablo listesi sıra bağımsız doğru küme olarak doğrulanır. |
| FIN-SC47-KREDI / FNS-D106 | TASLAK HAZIR | `0d32839c6`: dört grup için FINANCE_CREDIT_GROUP_FIELDS_DRAFT_v1.md. Ortak kilitli kurallar, önerilen alanlar ve cevap bekleyen zorunluluklar ayrıdır. Gruba özgü hareket, hesap eşlemesi veya grant açmaz; Sinan onayı bekler. |
| FIN-SC47 migration envanteri | KAYNAK DÜZELTİLDİ | `084634bbc`: açılış, açılış ters kayıt/hareket ve kredi migration'ları mevcut bank account discovery zincirine eklendi; snapshot uyumu kontrolü korunur. |
| FIN-SC36 / FNS-D81 | KAYNAK DÜZELTİLDİ | `ac1c0394c`: müşteri iadesi kullanıcının seçtiği taksit tutarlarını doğrular; prefill vade sırası ayrı assertion'da kalır. Planın saklama sırası seçilen miktarların eşitliğini bozmaz. |
| FIN-S9 / KIM-D1, FNS-D23 | KAYNAK DÜZELTİLDİ | `0bd786974`: save öncesi yetki kaybı testi önceki çit sayısına dayanmaz; audit stage olduğunda yetkiyi geri çeker, hiçbir business/command satırı kaydedilmemesini ister. |
| FIN-SC36 / FNS-D50 | KAYNAK DÜZELTİLDİ | `fefe06d3c`: kasa pasifleşme fixture'ı her geçişte version ilerletir; bekleyen öneri reddedilirken eski öneri kimliği/zamanı korunur. Geçmişi silerek guard aşılmaz. |

## Kalan kapsam

- FIN-SC36 banka bakiyesi: FNS-D102 iç defter formülü kilitlidir. Gerçek provider ve
  eşzamanlı çıkışları aynı işlemde koruyan entegrasyon henüz yazılmadı. Mevcut unavailable
  guard kaldırılmadı; olumlu mock kullanılabilirlik sayılmadı.
- FIN-S13 / FNS-D105 alış fatura üreticisi ve dispatcher köprüsü yoktur; sistem kabul
  aktivasyonu TK-31'e göre kapalı insan kapısında kalır.
- FIN-S14 banka/kasa yevmiye kaynakları; FIN-S17 dönemler arası devir: tamamlanmadı.
- FNS-SC32 DOGRULA: bakiye devir fişi portu ve teminat as-of bütünlüğü canlı kaynakta
  uçtan uca doğrulanmadan DONE işaretlenmez.
- Kredi arayüzü, banka+tür limit kaynağı/uyarısı, gruba özgü alanlar/hareketler ve fiş
  önerisi eksiktir. TK-30 boş eşleme/MappingMissing davranışı ve FNS-D106 onay kapısı korunur.
- FNS-D108 kapalı dönem sonrası açılış kredisi düzeltmesinin yeni akışı henüz kodlanmadı;
  W5-5.2'deki mevcut propose/approve yetkileri dışında yeni izin açılmaz.

## Doğrulama

`0cd9c5145` kaynak head'inde Finance.UnitTests projesi (bağımlılıklarıyla) derlendi:
**exit 0, 0 uyarı / 0 hata**. Bu bir test koşumu değildir; dalga sonu birleşik derleme de yapılacak.
Birim/gerçek SQL/HTTP/
Vitest/tarayıcı/CI koşumu **NOT_RUN**; eski altı kırmızının geçtiği iddia edilmez.
Test atlanmadı, silinmedi veya karantinaya alınmadı. Gerçek SQL kurulum/servis ayarı,
production, grant/rol, OPEN durumu ve Finance STOP değiştirilmedi.
