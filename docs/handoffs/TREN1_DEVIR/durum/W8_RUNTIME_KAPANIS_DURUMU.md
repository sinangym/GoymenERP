# W8 runtime kapanış ara durumu — 18 Kasım

Sabit kapsam **174/174 kaynak DONE**, W8 son pakettir. Yeni dalga veya dilim açılmaz.
Bu belge bir ara koşum kaydıdır; final exact-head kabulü, SQL, tarayıcı, CI veya K3
onayı değildir. Yayımlanmış tren henüz 135/174 kaynak checkpoint'indedir; yeni kaynak
ve kapanış düzeltmeleri entegrasyon dalındadır.

| Kanıt | Gerçek sonuç / sınır |
|---|---|
| İlk tam 18 UnitTests koşumu, `a8f4440ec` | 13.168 test: 13.091 PASS, 77 FAIL, 0 skip; 18 build exit0. İlk proje dağılımı 5 PASS / 13 FAIL. |
| Son modül tekrarları | 18 projenin 17'si kendi son koşusunda yeşil. Bunlar farklı kaynak checkpoint'leridir; final birleşik koşum değildir. |
| Purchasing, `203d0536b` | 882/884 PASS, 2 FAIL, 0 skip. İki assertion düzeltmesi `78cdaba10` ile alındı; tekrar QUEUED. |
| Manufacturing, `203d0536b` | 1.424/1.424 PASS, 0 skip. |
| PDF, `203d0536b` | 77/79 PASS, 2 FAIL, 0 skip. Aynı içerikte rastgele font adı yüzünden bayt farkı; kaynak düzeltmesi ve iki ek regresyon `be81bae4c`, tekrar QUEUED. |
| RCSI CLI araç testleri, `203d0536b` | 71/72 PASS, 1 FAIL, 0 skip. Windows symbolic-link fixture izni; gerçek junction fixture düzeltmesi `78cdaba10`, tekrar QUEUED. Gerçek SQL/RCSI kabulü değildir. |
| Inventory takipli maliyet izolasyonu, `031728b6f` | Integration projesinde 4/4 PASS, 0 skip; mevcut InMemory sağlayıcısı, gerçek SQL değildir. |
| Mimari bypass guard grubu, `031728b6f` | 338/340 PASS, 2 FAIL. Recovery dalındaki iki gerçek Purchasing kaynak düzeltmesi henüz birleşmediği için iki fingerprint farklı; ayrıca Product price-list tasarım factory girdisi `203d0536b` ile gerekçeli eklendi. Final tekrar QUEUED. |
| Frontend, `203d0536b` | TSC exit0. Tam Vitest çalışıyor; ilk başarısızlıklar korunuyor, sonuç PASS değildir. |
| Recovery | Ayrı, sabit kaynak dalında gerçek üretici exit0; uzun tam Python paketi çalışıyor. Entegrasyon sonrası final kaynak pinleri ayrıca üretilir. |
| Hafif doğrulayıcılar | Backlog ve bootstrap exit0; backlog 64 test/skip0, bootstrap 56 test/9 mevcut koşullu skip, backend grup 28 test/skip0, OpenAPI drift 16 test/2 mevcut koşullu skip. |
| İlişkisel envanter Python paketi | İlk koşumda Work'un dört yeni gerçek bildirim fact'ini eski exact beklenti saymadığı için 1 FAIL. Kaynak/envanter doğrulanan `586176b32` beklenti düzeltmesi birleşti; kök tekrar 48/48 PASS, skip0, exit0. Gate gevşetilmedi. |
| Gerçek SQL, tarayıcı, final CI, iki bağımsız K3 | Henüz tamamlanmadı. Yerel SQL yapılandırılmamıştır; yeni DB/container/service oluşturulmadı. |

İlk başarısız native log/exit/TRX dosyaları korundu; yeni denemeler ayrı dosyalara
yazılır. Test atlama, silme, karantina veya SQL yokluğunu başarı sayma uygulanmaz.
Sıradaki sıra: mevcut hataları düzeltme ve tekrar → final üreticiler/pinler → birleşik
kapanış kanıtı → normal tren fast-forward → tek taslak K3 PR. Yazar merge etmez.
