# Oturum, model ve paralel teslim planı

Durum: 2026-09-13 güncellemesi. Sinan artık Sipariş ve Finans için iki yeni Backend–Claude çiftini açıkça başlatmıştır; mevcut Business ve koordinatör Astra olarak kalır, Backend varsayılanı Spark'tır. Gerçek başlangıç ve göreve özgü promptlar [başlangıç kaydındadır](START_ORDERS_FINANCE.md). Aşağıdaki genel analizdeki önceki tek çift/karar bekleme önerisi, bu iki modüle verilmiş son yetkiyi durdurmaz. Yeni kural sistemi, kanonik backlog kopyası veya production yetkisi değildir.

## 1. Önerilen karar

**Bütün ERP'nin bağlantılarını birlikte tasarla; bütün ekranları bitirip backend'i sona bırakma.** Mevcut Master/kararlar ortak sistem haritasıdır; yeni baştan kapsamlı bir ERP analizi yazılmayacak. Business yalnız mevcut kaynakların boşluklarını ve sıradaki gerçek bağımlılıkları netleştirir. Ayrıntılı çalışma bir küçük kullanıcı akışı önde gider.

Seçilen düzen: aynı Business + aynı koordinatör + Sipariş ve Finans için iki ayrı Backend/Claude çifti. İkisi başlangıç hazırlığı yapar; uygulama yalnız gerçek bağımlılığı hazır küçük akışta çalışır. Hazır olmayan çiftin sürekli durum üretmesi beklenmez. Eski Cari/Ürün/Organizasyon sahipleri yalnız somut yeni öncül ihtiyacını alır. Bütün modülleri açmak, aynı anda bütün modülleri bitirmek değildir.

## 2. Neden önerilen uzun seri döngü uygun değil?

`Bütün spec'ler -> bütün frontend'ler -> Codex analizi -> Business -> frontend revizyonu -> tekrar analiz -> en son backend` sırası, arayüz keşfi ile uygulama kabulünü karıştırır.

| Risk | Bu projedeki somut karşılığı | Erken karşılık |
|---|---|---|
| Ekran gerçek API'yi varsayar | Cari'de request optional iken response required; roles=0; hata/ACK farkı | Backend ilk toplantıdan itibaren contract ve örnek yanıtları üretir |
| Mutlu yol doğru, yazma güvenliği eksik | Unknown sonuç, aynı-key retry, geç yanıt, company switch | İlk command ile yetki/version/idempotency ve gerçek pending testler birlikte gelir |
| Browser ortamı son güne kalır | C006 producer/consumer manifest farkı ve yüklenemeyen test | Mevcut runtime/fixture sözleşmesi erken tüketilir; gerçek bağlantı erkene alınır |
| Ekrandan veri mimarisi çıkarılır | Sipariş durumu, tahsis, stok hareketi ve para olayı farklı sahiplerde | İş olayı, kayıt sahibi ve transaction sınırı ekrandan önce açıklanır |
| Birbirini tekrar eden genel review | Kapanmış bulgu ve aynı başarılı kanıt tekrar okunur | İlgili bulgu/fark + zorunlu final-source review; tek izleyici |

Business'ın iki model çıktısına katılması veritabanı constraint, eşzamanlılık veya gerçek rollback kanıtı değildir. Aynı görevi başka modele geçirip yeniden okutmak bağımsız non-author review üretmez. Doğru ön analiz hataları azaltabilir; tek seferde hatasız backend garantisi vermez.

UI-first'in yararlı kısmı korunur: seçilmiş bir akışta gezinme, terminoloji, tablo yoğunluğu, form yerleşimi ve kullanıcı beklentisini görmek için kısa wireframe/prototip. Prototip ürün kabulü değildir; gerçek olmayan bakiye/stok/yetki verisi uygulamada çalışıyor gibi sunulmaz. Bütün modüllere üretim kalitesinde mock ekran yapmak başlangıç şartı değildir.

## 3. Daha hızlı gerçek sıra

```text
Business: mevcut ERP haritası + sıradaki küçük akışın kabul örnekleri
                              |
             Backend + Claude: tek kısa sözleşme/UX uzlaştırması
                              |
             +----------------+----------------+
             |                                 |
       Backend: API + SQL                Claude: ekran + client
       + hedef testler                   + ilgili UI/DOM testleri
             |                                 |
             +-------- erken gerçek bağlantı --+
                              |
              ilgili negatif/retry/entegrasyon kabulü
                              |
               gerekli review + CI -> coordinator teslim
```

Bu, backend tamamen bitmeden frontend'e başlama yasağı değildir. Aynı dilimin sözleşmesi belirliyse iki taraf paralel hazırlanır. Önceden bağımlı başka bir özelliğin main'e birleşme şartı varsa atlanmaz; hazırlık, o özelliği uygulanmış saymaz. Frontend'in ilk canlı denemesi için bütün ekranların tamamlanması beklenmez.

Başlangıç uzlaştırmasında: aktör/şirket/global kapsam, ana kayıt ve sahibi, durum geçişi, request/response/null/version, stable hata kodu, idempotency ve audit etkisi, normal/negatif kabul. Codex teknik uygulanabilirlik; Claude UX/consumer; Business iş anlamı kontrol eder. Bu not mevcut spec/PR içine sığar, yeni imza/manifest töreni açılmaz.

Contract değişirse yalnız etkilenen alan/ekran/test güncellenir. Business her kod değişikliğini tekrar onaylamaz; iş anlamı veya kabul değişirse devreye girer. Birbirini tekrar eden yorumlar yerine tek bulgu ve sahibi izlenir. İki davranışsız turda somut neden/escalation; tüm sistem analizi başa sarılmaz.

## 4. Hangi konuşma kullanılacak?

| Rol | Tercih | Sınır |
|---|---|---|
| CODEX-ORCHESTRATOR | Aynı mevcut görev | İkinci koordinatör/merge sahibi yok |
| ChatGPT-BUSINESS | Aynı mevcut görev | Bütün sistem bağlantılarını görür; uygulama ayrıntısında bir akış önde |
| Cari Backend/Claude | Cari'nin kalan ayrı akışları ve gerçek regresyonları için aynı görevler | Kapanan L04 tekrar açılmaz; bütün Cari bitti denmez |
| Ürün Backend/Claude | Sipariş ön koşulu gerçekten Ürün işi ise mevcut Ürün görevleri | Eski U01/U02 kabulü yeniden istenmez |
| Organizasyon Backend/Claude | Organizasyon'a ait yeni yetkili akış varsa aynı görevler | Eski teslimleri durum turuyla uyandırma |
| Gerçekten yeni modül | Kullanıcı seçiminden sonra bir yeni Backend ve bir yeni Claude görevi | Aynı repo, güncel main'den farklı worktree/branch; bir akışlık kısa başlangıç |

Bu projedeki uzun konuşmalar nedeniyle yeni modül için eski Cari sohbetini Sipariş'e dönüştürmek yerine temiz bir çift önerilir; her modülde veya her küçük akışta zorunlu yeni sohbet kuralı değildir. Mevcut kısa bağlamlı bir görev açık yeniden atama ve ayrı worktree ile kullanılabiliyorsa yalnız bağlam sıfırlamak için ikinci yazar açılmaz. Yeni sohbet kendiliğinden kalıcı öğrenme taşımaz; [LESSONS](LESSONS.md), ilgili gerçek kaynaklar, son kararlar ve ortak girdi aktarılır. Tam sohbet geçmişi kopyalanmaz; eski dallar/kanıtlar silinmez. Yeni sohbet açmanın sınırsız kota veya garanti düşük maliyet sağladığı varsayılmaz.

Önerilen yeni görev adları yalnız isim taslağıdır: `CODEX-BACKEND-SATIS-ILK-AKIS`, `CLAUDE-FRONTEND-SATIS-ILK-AKIS`. Gerçek backlog/akış seçilmeden bu görevler açılmaz. Ön koşul Ürün ise bunlar yerine mevcut Ürün çifti atanır.

İkinci aktif çift için beş koşul birlikte: kabul/kararlar hazır; gerekli öncüller hazır; ayrı dosya/migration/contract sahipliği; çakışmayan ortam/port; review/entegrasyon kapasitesi. Koşullar yoksa daha fazla üretim görevi açmak yerine eldeki akış bitirilir. Dış araçla mesaj gönderilemiyorsa alıcıya iletilmemiş tek mesaj bildirilir; başarılı teslim varsayılmaz.

## 5. Spark / Astra dağılımı

13 Eylül 2026'da açılarak kontrol edilen [resmî model sayfası](https://learn.chatgpt.com/docs/models), Spark'ı hızlı kod iterasyonu için metin girdili araştırma önizlemesi, Astra'yı karmaşık çok adımlı işler için güçlü model olarak tanımlar. [Astra model bilgisi](https://developers.openai.com/api/docs/models/gpt-6-astra) karmaşık coding/reasoning kullanımını destekler. Aşağıdaki rol dağılımı bizim projeye özel önerimizdir; ERP üzerinde ölçülmüş model karşılaştırması değildir.

| İş | Önerilen model / başlangıç eforu | Neden / yükseltme koşulu |
|---|---|---|
| Koordinatörün ilk bağımlılık/kapsam planı; karmaşık kanıt veya sahiplik çelişkisi | Astra High | Sistemin bütününü ve sonuçların sınırlarını birlikte değerlendirir |
| Aynı koordinatörün kesin hazır görevi iletmesi, CI sonucunu kaydetmesi, dar checklist takibi | Astra; kullanıcı tercihiyle aynı model | Kısa değişiklik odaklı mesaj ve tek izleme; tasarruf için yeni model değişim töreni yok |
| Business'ın modüller arası iş modeli, para/stok/durum ve kabul muhakemesi | Mevcut ChatGPT-BUSINESS; hesabında uygunsa Astra High | Business yerini yeni Codex sohbetiyle değiştirme; erişilebilir model uygulamada ayrıca doğrulanır |
| Backend'in yeni transaction/migration/permission/cross-module contract tasarımı ve zor teşhis | Astra High | Hata etkisi geniş; ilk teknik riski erken ele al |
| Backend'in kabul edilmiş örüntüde dar uygulaması, test/fixture ve mekanik düzeltme | Spark Medium; gerekirse High | Açık girdi/çıktı ve hedef test vardır; semantik değişim çıkarsa Astra değerlendirmesi |
| Frontend, ekran görüntüsü, UX, browser | Mevcut Claude | Rol/araç sahipliği korunur; Spark'ın metin-only sınırını görsel kabul için yok sayma |
| Yüksek risk bağımsız inceleme | Uygun yetkinlikte non-author reviewer; Codex tarafında Astra önerilir | K1/K2 tek, dar K3 iki bağımsız reviewer; model adı bağımsızlık kanıtı değildir |

Her satır için yeni kalıcı sohbet açılmaz. Aynı görevde modeli aşama başında değiştirmek, aynı dosyaya iki yazar açmaktan farklıdır; model değiştirme bu turda yapılmadı. Model değiştirmek araç erişimi veya kullanıcı yetkisi kazandırmaz. Kullanılan istemci/modelde gereken araçlar mevcut değilse dar görev uygun mevcut sahibine açık devirle aktarılır; izin reddi başka model/araçla aşılmaz.

Her zaman Extra High/Max seçilmez. İlk görev başına orta efor, karmaşık tasarımda High öneridir; gerçek zor sorun gerektirmedikçe efor yükseltilmez. Her küçük araç çağrısında model değiştirme veya başarılı işi güçlü modele baştan tekrar yazdırma yok. Spark'ın daha hızlı metin üretmesi daha kısa toplam ERP teslimi demek değildir; hatalı iş ve yeniden üretim de tüketimdir.

Bu inceleme hesaba özel kalan limitleri veya görevlerin token maliyetini ölçmedi. API token fiyatı ChatGPT/Codex abonelik kredi hesabıyla aynı değildir. Spark'a limitsiz/free, yeni konuşmaya yeni kota veya belirli bir tasarruf yüzdesi atfedilmez. Bekleyen her görev için ayrı model sorgu döngüsü açmak yerine mevcut olay/iş tamamlanma bildirimi kullanılır.

## 6. Bugün hedefi

Mevcut kaynaklardan **bütün ERP'nin bugün eksiksiz ve production-ready bitmesi taahhüt edilemez**. Tam Cari/Ürün kalan kapsamı, Sipariş/Stok/Üretim/Finans zincirleri, formüller, restore ve gerçek işletme kabulü aynı şey değildir. Deadline uğruna testleri sona taşıma, kabul kapsamını gizlice küçültme veya açık iş kararı seçme yok. Yeni modül bağımlılıkları ve işlem sayısı görülmeden saat/gün tahmini verilmez.

Bugün için önerilen ve Sinan'ın seçimine sunulan sınırlı çalışma hedefi:

1. Mevcut gerçek teslim/main CI kapanışını koru. Main `f6fa07d` için bu tur okunan dört push workflow'unun tamamı completed/success; L04 bekleyen CI diye yeniden görev açma.
2. Business, mevcut Master/backlog'a referansla kısa modüller arası akış/bağımlılık özeti + ilk Sipariş akışının hazır/eksik listesini verir. Bütün modüllerin bütün ekranlarını yeniden yazmaz. Bu bir genel akış kontrolü; haftalarca detay üretme kampanyası değil.
3. Tek küçük akış seçilir: öncül eksikse ilgili mevcut Ürün/Ayarlar işi; hazırsa kaynaklı ilk Sipariş akışı. İşin kapsamı kullanıcı/koordinatörce yetkilendirilince aynı Backend ve Claude paralel uygular. Bütün W1 görevlerini done göstererek geniş backlog kapısı atlanmaz.
4. Hedef, bu akışın gerçek API/DB üzerinde teslimine ilerlemektir; gün sonunda bitmediyse gerçek kalan iş yazılır. Tamamlanınca yeni bağımsız hazır akış mevcut yetki sınırında seçilebilir; bütün ERP tamamlandı denmez.

Kapsam dışı tutulan şey sonraki modülün zorunlu ön koşuluysa o modül de hazır sayılmaz. Banka/e-belge/SSO gibi insan kapılarını hız için üretim davranışına çevirmek yok. Mobilin toplu kabulü önceki kullanıcı kararıyla ayrı kalır; masaüstü klavye/a11y ve mevcut sözleşmedeki kontroller sürer.

## 7. Hazırlanan görevlerin sırası

- **Gerçek başlangıç:** kullanıcı kararıyla Sipariş/Finans görev mesajları ve mevcut Business/koordinatör mesajları gönderilir; kesin kimlik ve çalışma durumu START_ORDERS_FINANCE kaydında tutulur. Mesaj gönderimi alıcı kabulü veya uygulama başarısı değildir.
- **Uygulama:** iki yeni modülün ilk hazır akışları; mevcut modüldeki gerekli öncül kendi çiftine gider. Ortak koordinatör/Business kimliği aynı. Bu dosyadaki eski karar-sonrası anlatımı artık yeni kullanıcı kararı için ek bekleme kapısı değildir.
- **İletişim:** Backend sözleşme/SQL etkisini Claude ve Business'a; Claude UX/decoder sorusunu Backend'e, iş anlamı farkını Business'a; Business yalnız semantik çelişkiyi çözer; coordinator sahiplik/sıra/kanıt/merge'yi yönetir. Her cevap tüm ekibe ve tüm eski sohbete tekrar gönderilmez.
- **Kapanış:** gerçek davranış, source/CI/review, ilgili SQL/browser, cleanup ve kalan kapsam bir defa teslim edilir. Kaynak değişmemişken kapanmış test/review yeni rapor üretmek için tekrarlanmaz. Eski başlıklara veya donör PR'lara geri dönülmez.

Kopyalanacak dört rol mesajı [PROMPTS](PROMPTS.md)'tedir. Her göreve hepsini değil yalnız ortak girdi + kendi rolü + ilgili [modül eki](MODULE_BRIEFS.md) verilir. Bu dosya kabul edilmiş politika yerine geçirilmez; karar sonrası gerekli değişiklikler mevcut sahiplerin normal yolu üzerinden uygulanır.
