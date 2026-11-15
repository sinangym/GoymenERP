# Sinan Kararları — 2026-11-15 (Dalga 2 ek 2: cari pasife alma, grup çapında pasife alma, tolerans içi satır kapanışı)

Bu belge önceki karar dosyalarını silmez; ALS-D99, ALS-D100, ALS-D101 kararlarını ve ASSUMPTION-ALS-EVT-1
varsayımını ekler. SAT-D192 (cari aktif/pasif yetkisi Muhasebe Müdürü, Genel Müdür ve Admin'de), ALS-D10 (tolerans
içinde sistem kapatmayı önerir ve sorar; kapanan satır yeniden açılabilir) ve ALS-D12 (tolerans dışı kapanışta
gerekçe zorunlu) aynen geçerlidir.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 23). Sorular
Alış/Cari takımının dalga 2 raporundaki NEEDS-SINAN maddelerinden geldi. "(Önerilen)" işaretleri soranındır.
Serbest metin cevap aynen alıntılanmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 23.1 | Bir cari, açık bakiyesi (borç/alacak) veya açık belgesi (açık sipariş, kapanmamış fatura) varken pasife alınabilsin mi? Bugün kontrol yok; yalnız yetki, gerekçe ve denetim kaydı var. | **Uyar, gerekçeyle izin ver (Önerilen)** | "Sistem açık bakiyeyi/belgeleri listeler ve uyarır; Muhasebe Müdürü gerekçe yazarak yine de pasife alabilir. Pasif cariye yeni belge açılamaz, eski belgeler kapanabilir." Seçilmeyenler: "Bakiye/belge varsa engelle" — bakiye sıfırlanmadan ve açık belgeler kapanmadan pasife alma reddedilir (kasa hesabındaki FNS-D34 gibi); "Kontrol yok" — bugünkü gibi, yetkili kişi her durumda pasife alır, sistem bakmaz. |
| 23.2 | Bir cariyi tek seferde tüm şirketlerde (grup çapında) pasife alma komutu olsun mu? Bugün cari her şirkette ayrı ayrı pasife alınıyor. | Serbest metin, aynen: **"Şimdilik şirket şirket ve grup komutu genel müdür seçenekleri birleştir"** | Birleştirilen seçenekler: "Şimdilik şirket şirket (Önerilen)" — grup çapında tek komut yok; her şirkette Muhasebe Müdürü ayrı pasife alır, gerekirse sonra eklenir; ve "Grup komutu, Genel Müdür + Admin" — tek komutla bütün şirketlerde pasife alır, yetki yalnız Genel Müdür ve Admin'de, her şirket için ayrı denetim kaydı. Seçilmeyen: "Grup komutu, Muhasebe Müdürü" — aynı tek komut, ama Muhasebe Müdürü de kullanabilir (yalnız yetkili olduğu şirketlerde uygulanır). |
| 23.3 | Satın alma siparişinde, eksik teslim tolerans içindeyken satırı kapatırken gerekçe yazmak zorunlu olsun mu? (Tolerans dışında gerekçe zaten zorunlu, ALS-D12.) | **Tolerans içinde tek tık (Önerilen)** | "Sistem kapatmayı önerir, kullanıcı tek tıkla onaylar, gerekçe istenmez; denetim kaydı yine tutulur. Tolerans dışında gerekçe zorunlu kalır." Seçilmeyen: "Her zaman gerekçe zorunlu" — tolerans içinde de dışında da kapatmak için gerekçe yazılır (bugünkü uygulama). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| ALS-D99 | Açık bakiye/belge varken cariyi pasife alma (23.1) | (1) Pasife alma komutu açık bakiyeyi ve açık belgeleri **engellemez**. (2) Komuttan önce sistem açık bakiyeyi ve açık belgeleri listeler ve uyarır; açık bakiye/belge varken pasife alma **gerekçe** ister ve uyarının gösterildiği denetim kaydına yazılır. (3) Pasif cariye **yeni belge açılamaz**; pasife alınmadan önce açılmış belgeler kapanabilir (teslim, fatura, tahsilat/ödeme sürer). (4) Yetki SAT-D192'deki gibidir. (5) Açık bakiye ve açık belge bilgisi sahip modüllerin okuma portlarından gelir; Cari modülü başka modülün tablosunu okumaz. Uyarıda hangi belge türlerinin sayılacağı ve listenin biçimi **uygulama ayrıntısıdır (geri alınabilir)**. | Counterparty pasife alma komutu (ALC-S11), Finans bakiye okuma portu, Satış/Alış açık belge okuma portları, Cari ekranı |
| ALS-D100 | Grup çapında pasife alma (23.2) | (1) Şirket bazında pasife alma kalır: her şirkette SAT-D192 yetkilileri (Muhasebe Müdürü, Genel Müdür, Admin) o şirketteki cari hesabını ayrı pasife alır. (2) Buna ek olarak **grup çapında tek komut** eklenir: cariyi bütün şirketlerdeki hesaplarında pasife alır. Bu komutun yetkisi yalnız **Genel Müdür + Admin**'dedir; Muhasebe Müdürü bu komutu kullanamaz. (3) Grup komutu her şirket için ayrı denetim kaydı ve ayrı `counterparty.party.deactivated.v1` olayı üretir; ALS-D99 uyarı ve gerekçe kuralı her şirket için uygulanır. (4) İzin ayrı bir izin adıdır; grant insan kapısındadır. Komutun tek işlemde mi yoksa şirket başına mı yazılacağı ve kısmi başarısızlıkta davranış **uygulama ayrıntısıdır (geri alınabilir)**; bir şirkette yazılamayan pasife alma sessizce atlanmaz, sonuçta raporlanır. | Counterparty modülü yeni grup komutu ve izni, Cari ekranı |
| ALS-D101 | Tolerans içi eksik teslimde satır kapanışı (23.3; ALS-D10'u tamamlar) | (1) Eksik teslim tolerans bandının içindeyse sistem satırı kapatmayı önerir; kullanıcı **tek tıkla** onaylar, **gerekçe istenmez**. (2) Denetim kaydı yine tutulur (kim, ne zaman, kalan miktar, bant içi olduğu). (3) Tolerans dışında gerekçe zorunludur (ALS-D12). (4) Kapanan satır gerekçeyle yeniden açılabilir (ALS-D10). (5) Tolerans eşikleri OPEN-004'teki geçici varsayımdır; bu karar eşik seçmez. | Purchasing satır kapanışı (ALC-S4), Satın alma siparişi ekranı |

## Varsayım (geri alınabilir)

| No | Konu | Varsayım |
|---|---|---|
| ASSUMPTION-ALS-EVT-1 | `purchasing.order-line.closed.v1` olay içeriği | Kararlaştırılan {CompanyId, PurchaseOrderLineId, Closed, Version} alanlarına ek olarak `PurchaseOrderId` taşınır. Ek alan tüketicinin satırı siparişe bağlamasını kolaylaştırır, para/stok/yetki etkisi yoktur ve sürümlü sözleşme testinde sabitlenir. Sinan veya bir tüketici modül itiraz ederse çıkarılır. |

## Sınırlar

- Bu belge yeni rol açmaz ve hiçbir kişiye izin vermez. Yeni izinlerin (grup çapında pasife alma, satır
  kapanışı/yeniden açma) persona dağılımı yukarıda yazıldığı gibidir; gerçek grant insan kapısındadır.
- OPEN-004 tolerans eşikleri kapanmaz; bu belge eşik sayısı seçmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
