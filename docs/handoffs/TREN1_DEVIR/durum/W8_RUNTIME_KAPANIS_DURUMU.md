# W8 runtime kapanış ara durumu — 18 Kasım

Sabit kapsam **174/174 kaynak DONE**, W8 son pakettir. Yeni dalga veya dilim açılmaz.
Bu belge bir ara koşum kaydıdır; final exact-head kabulü, SQL, tarayıcı, CI veya K3
onayı değildir. Yayımlanmış tren henüz 135/174 kaynak checkpoint'indedir; yeni kaynak
ve kapanış düzeltmeleri entegrasyon dalındadır.

| Kanıt | Gerçek sonuç / sınır |
|---|---|
| İlk tam 18 UnitTests koşumu, `a8f4440ec` | 13.168 test: 13.091 PASS, 77 FAIL, 0 skip; 18 build exit0. İlk proje dağılımı 5 PASS / 13 FAIL. |
| Son modül tekrarları | 18 projenin tamamı kendi son koşusunda yeşil. Bunlar farklı kaynak checkpoint'leridir; final birleşik koşum değildir. |
| Purchasing, `1b55f5e10` | 884/884 PASS, 0 skip, build0/test0. İlk iki assertion hatası ve native kanıtları korundu. |
| Manufacturing, `203d0536b` | 1.424/1.424 PASS, 0 skip. |
| PDF, `e83c0cf62` | İzole kaynakta build0 ve 82/82 PASS, skip0; aynı kaynak birleşti. Eski 2/4 failure kanıtları korundu. Font ve iki kendi-catalog XMP UUID farkı giderildi; exact-byte, font, uzunluk/xref/trailer kontrolleri korundu. Final birleşik tekrar ayrıca gerekir. |
| RCSI CLI araç testleri, `edab5c3c9` | Build0 ve 72/72 PASS, skip0. İlk fixture ve sonraki xUnit2009 hataları silinmedi. Aynı path-bound kontrolleri `Assert.StartsWith` kullanır; gerçek junction ve pre-secret ret kontrolü çalıştı. Gerçek SQL/RCSI kabulü değildir. |
| Inventory takipli maliyet izolasyonu, `031728b6f` | Integration projesinde 4/4 PASS, 0 skip; mevcut InMemory sağlayıcısı, gerçek SQL değildir. |
| Mimari bypass guard grubu, `031728b6f` | 338/340 PASS, 2 FAIL. Recovery dalındaki iki gerçek Purchasing kaynak düzeltmesi henüz birleşmediği için iki fingerprint farklı; ayrıca Product price-list tasarım factory girdisi `203d0536b` ile gerekçeli eklendi. Final tekrar QUEUED. |
| Frontend | İlk tam `203d0536b`: TSC0; 614 dosya, 550 PASS / 64 FAIL; 9.813 test, 9.571 PASS / 242 FAIL, exit1, 2061.96 saniye. Hatalı/değişmiş 70 dosyanın `edab5c3c9` tekrarı: TSC0, 1.181/1.185 test PASS, 4 FAIL; 66/70 dosya PASS, exit1, 391.37 saniye. Tam JSON/loglar korundu; final tam koşum değildir. |
| Recovery | Eski sabit `c8d7d7c3e` gerçek üretici0 ve 36/36 Python PASS. Birleşik ölçüm99 ve yedi typed-constructor metadata hizalaması sonrası temiz `3123ef92f` gerçek üretici exit0; recovery ve release tam paketleri ayrı donuk çalışma alanında sırayla çalışıyor. Final kabul değildir. |
| Hafif doğrulayıcılar | Backlog ve bootstrap exit0; backlog 64 test/skip0, bootstrap 56 test/9 mevcut koşullu skip, backend grup 28 test/skip0, OpenAPI drift 16 test/2 mevcut koşullu skip. |
| İlişkisel envanter Python paketi | İlk koşumda Work'un dört yeni gerçek bildirim fact'ini eski exact beklenti saymadığı için 1 FAIL. Kaynak/envanter doğrulanan `586176b32` beklenti düzeltmesi birleşti; kök tekrar 48/48 PASS, skip0, exit0. Gate gevşetilmedi. |
| Gerçek SQL, tarayıcı, final CI, iki bağımsız K3 | Henüz tamamlanmadı. Yerel SQL yapılandırılmamıştır; yeni DB/container/service oluşturulmadı. |

İlk başarısız native log/exit/TRX dosyaları korundu; yeni denemeler ayrı dosyalara
yazılır. Test atlama, silme, karantina veya SQL yokluğunu başarı sayma uygulanmaz.
Sıradaki sıra: mevcut hataları düzeltme ve tekrar → final üreticiler/pinler → birleşik
kapanış kanıtı → normal tren fast-forward → tek taslak K3 PR. Yazar merge etmez.
