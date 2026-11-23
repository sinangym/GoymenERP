# W8 frontend ilk koşu kapanışı

Kaynak tabanı: `203d0536b0a02532c2d771d855ba6baeaeff9187`. Kapsam, bu kaynağın ilk tam Vitest koşusunda başarısız olan üretim dışı frontend testleri ve aşağıdaki kanıtlı bildirim hatasıdır. Üretim testleri ayrı yazarın kapsamındadır; yeni dalga veya işletme kararı yoktur.

## Kaynak düzeltmeleri

- `ExpensePaymentPage`: onay/işleme yanıtı durum değiştirdiğinde komut alt bileşeni kaldırılıyordu; onun içindeki başarı/tekrar oynatma bildirimi de kayboluyordu. Kesin sonuç bildirimi ödeme detayında tutulur; yeni komutta temizlenir. Mevcut şirket, aktör, oturum ve sayfa giriş anahtarları başka bağlama taşınmasını engeller. Önceki başarı, tekrar oynatma, anahtar ve gövde kontrolleri korunur; şirket/sayfa girişi değişimi ve sonraki belirsiz komutta eski başarının kaldırılması kaynak testleri eklendi. Yetki, sunucu tutarı ve komut sözleşmesi değişmedi.
- Satış havuzu test düzeneği gerçek `CompanySetupLink` yetki okumasına iki olumsuz yetkiyle cevap verir; bu shell okumasını satış iş isteği sayısına katmaz. Satış kaynak planı testindeki gerçek `Response` nesnesinin ikinci kez JSON'a sarılması kaldırıldı.
- Nakit virmanı, masraf ödemesi ve şirket vergi kimliği testleri mevcut `StateBlock` başlığını konu ekiyle tam olarak karşılaştırır. Muhasebe hesap planı/iş merkezi/AI 403 testleri gerçek merkezi yetki sınırının eski nesli kapatmasını doğrular; eski formu tutmak veya yetki sınırını gevşetmek için üretim kodu değişmedi.
- Cari şirket kartı okuma sayımı yalnız gerçek kart uç noktasını sayar; ek kimlik geçmişi okumasını kart cevabı saymaz. Cari 360 gizli/kullanılamayan cevap örnekleri mevcut katı Finans sözleşmesi gereği açık kalem tutarı içermez.
- Defter değeri testinin TCMB bülten tarihi işlem tarihinden önceki gündür; işlem günündeki bültenin reddi ayrıca korunur. Gerçek kaynak TL tutarı ve TCMB referansı ayrımı değişmez.
- Paket/profil dosyası örnekleri gerçek `FileReader` ile aynı sentetik dosyanın baytlarını okur. Kurulu JSDOM 27 `File` uygulamasındaki eksik `arrayBuffer` metodu test dosyası düzeneğinde tamamlanır; tarayıcı uygulamasına, paketlere veya dosya biçimi doğrulamasına yeni yol eklenmez.
- Kilitli alış termin, kabul düzeltme, lojistik iptal ve fason fatura girdileri `fieldset` üzerinden devralınan gerçek `:disabled` durumuyla ölçülür; aynı anahtar/gövde ve yeniden gönderme kontrolleri korunur.
- Referans listelerinde iki bağımsız boş cevabın ikisi beklenir. Profil biçim yardımını ölçen test yardımın bulunduğu formu açar. Transfer kayıp kararı ilgili tablo/satır/hücre ve gerekçesiyle tam doğrulanır.
- Shell Ürün grubu ile belge owner sözlüğünün exact beklentilerine mevcut kaynakta bulunan yeni kayıtlar eklenir; genel içerme veya sayı eşiğine gevşetme yapılmaz.

## Kanıt sınırı

Bu dalda .NET, Node, Vitest, TSC, SQL, tarayıcı veya CI başlatılmadı. Kaynak düzenlemeleri `NOT_RUN`; birleşik hedef ve tam koşuyu koordinatör yapacaktır. İlk tam koşunun logu tamamlanmadan toplam sonuç veya PASS iddia edilmez. Şu anki ortak tarayıcı depolama hatalarının son stack doğrulaması beklenmektedir; yerel gerçek Node sürümü `v26.7.0`, CI kaynak yapılandırması Node 24'tür. Ortam sürümü ve çalışma bayrakları değiştirilmedi.

Test atlama, silme, karantina, yeni para/formül/izin/grant, production veya Finance STOP değişikliği yoktur. Bu kaynak kapanışı bağımsız exact-head K3 incelemesi değildir.

## Tam ilk koşu sonrası ortak depolama düzeltmesi

İlk tam Vitest koşusu aynı `203d0536b` kaynağında 614 dosyanın 64'ünde ve 9813 testin 242'sinde başarısız oldu. Cari bağlantı düzeneğinin `setItem` erişiminde depolama nesnesinin `undefined` olması tam stack ile doğrulandı. Kurulu Vitest, JSDOM ortamı kurarken mevcut Node global özelliğini koruyordu; gerçek Node 26.7.0 depolama getter'ı tarayıcı depolaması yerine kalıyordu.

Ortak test kurulumu `localStorage` ve `sessionStorage` adlarını Vitest'in zaten oluşturduğu gerçek, izole JSDOM penceresinin Storage nesnelerine bağlar. Getter yapılandırılabilir kalır; depolama reddi/yazma hatası testlerinin mevcut stub ve spy davranışı korunur. Sahte depolama, dosya, runtime bayrağı veya paket değişikliği yoktur. Bu takip kaynağında da TSC/Vitest `NOT_RUN`; ilk başarısız koşunun sonuçları korunur.

## Kalan ilk-hata sözleşmeleri ve erişilebilirlik

- İlk koşunun `table-name` diff'indeki dokuz dosyanın on iki tablosuna mevcut Türkçe başlık/konu/fiş numarasıyla erişilebilir ad eklendi: Hesap Planı, SMM Eşleme, Stok Maliyet Önerileri, Üretim Maliyet Farkları, Kaynak Fişler, Krediler, Resmî Satış Faturaları, Eksik Uyarıları ve İş Merkezi Takvimi. Yeni tablolarda başlık hücrelerinin `scope="col"` bilgisi, panel komutlarının açık `type="button"` niteliği tamamlandı; gerçek submit kontrolleri korundu. Statik denetim baseline'ı ve exact eşitlik testi değiştirilmedi. Mevcut ekran testleri gerçek erişilebilir tablo adlarını da ölçer.
- Satış vergi cevabı okuyucusu, Settings'in `TaxMasterCalculatorV1.TryParsePercent` ve `CK_SettingsTaxRateListEntries_Percent` kaynak sınırına göre 0–100, en çok dört ondalık basamak kabul eder. Önceki okuyucu `101` gibi kaynağın üretemeyeceği bozuk cevabı kullanılabilir sayıyordu. Geçerli sınır değerlerini aynen taşıyan ve sınır dışını reddeden testler eklendi; frontend vergi hesabı yapmaz.
- Eskiyen satış kantar okuması testi gerçek iptal edilmiş `AbortSignal` gönderir; yalnız hata adından iptal edilmiş bağlam türetilmez. Paralel antiforgery isteklerinde bitiş sırası varsayımı yerine üç HTTP yönteminin her birinin kendi gövde/token değeri doğrulanır.
- İki faktör ve kabul düzeltme alanları üst `fieldset` üzerinden devralınan `:disabled` durumuyla ölçülür; iki faktör mock cevap kuyrukları testler arasında sıfırlanır. Gelecek tedarik ve üretim profili durumu testleri mevcut tam konu/durum metnini doğrular. Yeni gerçek alış import capability'si ve shell sırası/16 Inventory route exact beklentilere alınır.
- Etiket PDF fixture'ı mevcut yerel Node Blob uygulamasından gerçek sentetik bayt üretir; JSDOM'un eksik `Blob.text()` desteği başarı kanıtı yerine geçmez. Başarılı gerçek bayt, bozuk PDF'nin `malformed-response` olması ve 503 ayrımı korunur. Global Blob veya üretim kodu değiştirilmedi.

Bu eklerin kaynak kontrolleri `git diff --check` exit 0; TSC, Vitest ve tarayıcı henüz `NOT_RUN`. İlk koşunun 64 dosya/242 test başarısızlığı, yeni kaynak doğrulanmadan kapanmış sayılmaz.

## Hedef tekrar sonrası üç dar kapanış

Koordinatörün `edab5c3c9` birleşik kaynağındaki hedef koşusu: 70 dosyada 66 PASS / 4 FAIL; 1185 testte 1181 PASS / 4 FAIL. TSC exit 0, RCSI 72/72 PASS. Bu sonuç önceki düzeltmeleri kapsar; aşağıdaki yeni kaynağın henüz çalışma kanıtı değildir. Native ilk hata ve tekrar logları korunur. Dördüncü hata olan üretim planı fixture'ı ayrı yazarın kapsamındadır.

- Cari uyarısındaki aynı metin hem hata özeti hem checkbox'a bağlı alan hatasıdır. Test artık exact `role=alert` metnini, checkbox'ın `aria-invalid` durumunu ve `aria-describedby` ile bağlı exact alan metnini ayrı doğrular; onay kutusu işaretlenmeden sıfır istek ve sonraki exact gövde kontrolleri değişmez.
- Oturum testinin ilk `role=status` sorgusu `StateBlock` yükleme düğümünü erken yakalıyordu. `useShellOrchestrator` içindeki gerçek `hadSession` ve `LoginPage` notice davranışı değişmedi; test önce exact süresi dolmuş oturum başlığını, sonra onun status/expired niteliğini bekler. Yeniden girişle toparlanma ve giriş sayısı korunur. Bu hata bir depolama bayrağının tüketilmesi değildi.
- `th-scope` native diff'indeki 14 yeni dosyanın 122 başlığı kaynak bağlamıyla tamamlandı: 120 sütun başlığı, ek masraf/hizmet satır adları için iki satır başlığı. `listFormAudit` kodu, bütün kural testleri ve baseline aynı kaldı.
- Aynı statik sözleşmenin sonraki kuralları kaynak üzerinden birlikte incelendi. Beş Accounting/Finance panelindeki 14 komut düğmesine `type=button` eklendi; gerçek submit düğmeleri korunur. Beş Inventory/Organization sayfasındaki sekiz iç `main` alanı section oldu; ana sayfa landmark'ının sahibi yine shell'dir.
- Altı Purchasing HTTP okuyucusundaki genel başarı bayrağı yerine mevcut exact 200 veya 201 sözleşmesi açık kontrol edilir. Önceden var olan beklenmedik 2xx/malformed, 4xx ret, 409 yarış ve 5xx belirsiz işlem ayrımları korunur. Geçerli gövde olsa bile yanlış 201/202/204 yanıtını başarı saymayan kaynak testleri eklendi; shared receipt transportta 200/201 birbirinin yerine kabul edilmez.
- `IdentityEndpoints` giriş ve çıkışta yalnız 204 NoContent tanımlar. Antiforgery token yenileme çiti bu exact durumla hizalandı; iki uç için gerçek 204 sonrası yeni token, 200/201/202/401 sonrası aynı token ve yanıtın değişmeden iletilmesi kaynak testleri eklendi. Mevcut antiforgery tek tekrar, gövde ve yetki davranışları değişmedi.

Yeni number-input veya local-storage kaynağı yoktur; yorumlardaki storage adları denetim istisnası değildir ve koda dönüştürülmedi. Eski nested-main baseline girdileri değiştirilmedi. Üretimdeki `PaintSelectionEditor` tam HTTP durum kontrolü koordinatörün ayrı değişikliğidir. Buradaki aday taraması salt kaynak okumasıdır; gerçek TypeScript denetleyicisinin koşması yerine PASS iddia edilmez.

Bu takipte yalnız kaynak düzenleme ve `git diff --check` (exit 0) yapıldı; .NET/Node/TSC/Vitest/SQL/tarayıcı/CI başlatılmadı. Hedef doğrulama listesi: CariAccountStatusWarning, AppShellLogin, listFormAudit, antiforgery ve altı Purchasing API test dosyası; ardından koordinatörün tam frontend koşusu. Yeni test sonucu `NOT_RUN`; yeni dalga, iş politikası, izin, grant veya baseline genişletmesi yoktur.

## Birleşik tam tekrar

`c6168c8e23afa323af003dc79fdab3c9fbc00872` frontend kaynağı: lint exit0,
`npm run build` (TSC + Vite) exit0; tek işçi/sıralı Vitest **614/614 dosya,
9.854/9.854 test PASS, 0 FAIL, 0 skip, native exit0**, 1942.652 saniye.
Öncesindeki 11 dosya hedef grubu da143/143 PASS verdi. Tam koşum bütün Manufacturing
testlerini ve yeni transport/accessibility regresyonlarını kapsar; baseline veya
test atlama eklenmedi. İlk 64 dosya/242 FAIL ve bütün tekrar JSON/log/exit kayıtları
korundu. Lint uyarıları ve Vite chunk-size uyarısı silinmedi. Bu DOM/birim kanıtı,
gerçek tarayıcı, API, SQL, son CI veya bağımsız K3 incelemesi değildir.
