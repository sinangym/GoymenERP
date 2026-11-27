# Sinan Kararları — 27 Kasım 2026 (AI geri bildirimi)

Kaynak: Bu oturumdaki iki seçenekli soruya Sinan'ın doğrudan cevapları. Mevcut `T2/WP-C9` ve `T2/WP-B2` kapsamını netleştirir; yeni paket, rol/grant, AI sağlayıcısı veya canlı aktivasyon kararı değildir.

## Doğrudan cevaplar

- Nedenler: “Önerini onaylıyorum. Belirttiğin kapalı neden listesi kullanılsın. Kabulde neden zorunlu olmasın; ret, düzeltme ve kabulü geri almada zorunlu olsun. İlk sürümde serbest metin tutulmasın. Geri bildirimler AI performansını iyileştirmek için kullanılsın; işletme kurallarını kendiliğinden değiştirmesin.”
- Sayımlar: “Önerini onaylıyorum. Her yanıt, ay sonundaki en güncel geri bildirimine göre yalnız bir kategoride sayılsın. Değişiklik geçmişi korunsun. Geri bildirim verilmemiş yanıtlar ayrıca gösterilsin. Önceki kabulün geri alınması gerçek ERP işlemini değiştirmesin. Kabul oranı tek başına AI doğruluğunun kanıtı sayılmasın.”

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| AI-D51 | Geri bildirim nedenleri | Kapalı liste: yanlış bilgi, yanlış/eksik kaynak, eksik cevap, güncelliğini yitirmiş bilgi, diğer. Kabulde neden isteğe bağlıdır; ret, düzeltme ve önceki kabulü geri almada zorunludur. İlk sürümde serbest metin saklanmaz. Geri bildirim AI performansını iyileştirmek için kullanılır; işletme kuralları otomatik değiştirilmez. | C9 owner doğrulaması, B2 kullanıcı arayüzü |
| AI-D52 | Tekil aylık sayım | Her yanıt ay sonundaki en güncel kullanıcı geri bildirimine göre yalnız bir kategoride sayılır; değişiklik geçmişi korunur. Geri bildirim verilmemiş yanıtlar ayrıca gösterilir. Önceki kabulü geri alma gerçek ERP işlemini değiştirmez. Kabul oranı tek başına AI doğruluğunun kanıtı değildir. | C9 defteri, A5 ölçülmüş sayımlar |

## Korunan sınırlar

- AI-D25 kapalı listesi ve mevcut insan onayı kapıları korunur. Geri bildirim ERP onayı, fiş düzeltmesi, ters kayıt veya ödeme komutu değildir.
- K10/No159 kapsamındaki 90 günlük ayrı geri bildirim/soru-cevap saklama kararı, kimlik ayrımı ve mevcut hukuk/hold kapıları değişmez. Bu cevaplar imha veya saklama süresi uzatma yetkisi vermez.
- Yeni onay eşiği, başarı yüzdesi, rol ataması veya otonomi seviyesi seçilmedi. Gerçek SQL, CI ve bağımsız K3 kabulü ayrıca tamamlanır.
