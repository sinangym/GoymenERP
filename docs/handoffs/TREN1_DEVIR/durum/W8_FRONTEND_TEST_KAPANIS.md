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
