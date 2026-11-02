# Sinan Kararları — 2026-11-02 (belirlenemeyen yarım gün, kur okuma, döviz limitte TL çevrimi, fason fiyat zorunluluğu)

Bu belge önceki karar dosyalarını silmez; SAT-D198…SAT-D201'i ekler. Kur kaydı PR'ının (#906) ve fason sipariş PR'ının
(#898) K3 review'larında açık kalan soruları cevaplar. EK24 SAT-D169/D171, EK7 SAT-D47, SAT-D53 ve EK29 SAT-D195 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **EK24 SAT-D171'in uygulama ayrıntısı:** arife/yarım gün durumu otomatik belirlenemezse "SAT-D169'daki kişiler kuru
  gerekçeyle düzeltebilir" ifadesi yerine, yetkili kişiler **o günü** tam gün veya yarım gün olarak gerekçeli işaretler
  (SAT-D198). Takvimin otomatik olması ilkesi aynen kalır; işaretleme yalnız otomatik kaynağın belirleyemediği gün için
  istisnadır.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: bir tur, dört soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Kur: arife/yarım gün durumu otomatik kaynaktan belirlenemezse (kaynak henüz bağlı değil) ne olsun? Şu an güvenli tarafta: kur 'belirlenemedi' diyor, faturada/riskte kur seçilmiyor. | Muhasebe işaretlesin (Önerilen) | "Otomatik kaynak (Resmî Gazete/Cumhurbaşkanlığı genelgesi) asıl kaynak olur. Belirlenemeyen nadir günlerde Muhasebe/Muhasebe Müdürü/GM/Admin o günü 'tam gün' veya 'yarım gün' olarak gerekçeli işaretler, kayıt altında kalır. Takvimi sen tutmazsın, yılda birkaç gün çıkar." (seçilmeyenler: "Kuru doğrudan düzeltsinler" — "Gün işaretlenmez; belirlenemeyen günde aynı kişiler o faturanın kurunu elle gerekçeli girer."; "Tam gün say" — "Belirlenemeyen günü normal iş günü say, otomatik devam et. Hızlı ama arife kaçarsa yanlış kur riski var.") |
| 2 | Kurları (Ayarlar > Kurlar) kim görebilsin? | Satış+Muhasebe+Müdürler (Önerilen) | "Satış Uzmanı, Satış Müdürü, Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin, Finans personaları görür. Düzeltme yine yalnız Muhasebe/Muhasebe Müdürü/GM/Admin." (seçilmeyenler: "Herkes" — "Sisteme giren her kullanıcı kurları görür."; "Yalnız Muhasebe ve üstü" — "Muhasebe, Muhasebe Müdürü, GM, Admin.") |
| 3 | Risk limiti döviz cinsinden tanımlı bir müşteride TL tutarlar limite nasıl çevrilsin? | Kontrol günü kuruna böl (Önerilen) | "TL tutar ÷ o günün TCMB kuru (ör. 43.000 TL ÷ 43 = 1.000 EUR). Kur çözülemezse risk 'hesaplanamadı' kalır." (seçilmeyenler: "Limiti TL'ye çevir" — "Döviz limiti o günün kuruyla TL'ye çevrilip TL üzerinden karşılaştırılır."; "Döviz limiti olmasın" — "Risk limitleri hep TL tanımlansın.") |
| 4 | Fason siparişte fiyat (kg başı) girilmeden sipariş onaya gönderilebilsin mi? | Hayır, fiyat zorunlu (Önerilen) | "Fason satırında kg fiyatı yoksa onaya gönderilemez." (seçilmeyen: "Evet, sonra girilebilir" — "Normal sipariş gibi fiyatsız da gönderilebilir; fiyat sonra revizyonla girilir ve yine onaya gider.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D198 | Belirlenemeyen arife/yarım gün | Arife/yarım gün bilgisi otomatik resmî kaynaktan alınır (SAT-D171 aynen). Otomatik kaynak bir günü belirleyemezse **Muhasebe, Muhasebe Müdürü, Genel Müdür veya Admin** o günü **tam gün** ya da **yarım gün** olarak **gerekçeyle işaretler**; işaret kayıt altında (audit'li) kalır (soru 1). İşaret yapılana kadar o gün için kur seçilmez ("kur belirlenemedi", fail-closed). Uygulama ayrıntısı (geri alınabilir): işaret, otomatik kaynağın kesin cevabının üzerine yazılmaz; yalnız belirlenemeyen güne uygulanır; daha önce kesilmiş faturanın kuru değişmez (SAT-D172). Hangi resmî kaynağın bağlanacağı ve bu dış bağlantının açılması insan kapısındadır | Kur kaydı (#906 takip dilimi) |
| SAT-D199 | Kur okuma | Kurları **Satış Uzmanı, Satış Müdürü, Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin ve Finans personaları** (Finans Uzmanı, Finans Müdürü) görür (soru 2). Kur düzeltmesi SAT-D169/SAT-D188 gereği aynen kalır (soru 2'nin açıklaması: Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin). Gerçek grant ataması insan kapısıdır | Persona kartı, `settings.exchange-rates.read` |
| SAT-D200 | Döviz limitte TL çevrimi | Kredi limiti döviz cinsinden olan müşteride TL tutarlar limit para birimine **kontrol gününün TCMB kuruna bölünerek** çevrilir (ör. 43.000 TL ÷ 43 = 1.000 EUR) (soru 3). Kur çözülemezse risk "hesaplanamadı" kalır (fail-closed). SAT-D53'ün iki toplamlı gösterimi ve SAT-D47'nin kontrol günü kuru aynen kalır. Uygulama ayrıntısı (geri alınabilir): limit para birimi ile tutarın para birimi farklı iki dövizse (ör. USD tutar, EUR limit) çapraz kur bu kararla açılmaz; o tutar "hesaplanamadı" kalır ve gerekirse ayrıca sorulur | Sales risk (API-06/API-07) |
| SAT-D201 | Fason fiyatı zorunlu | Fason siparişte **kg fiyatı girilmemiş** hizmet satırı varsa sipariş **onaya gönderilemez** (soru 4). SAT-D195 aynen kalır (liste yok, kg başı elle, normal onay) | Fason sipariş (#898 takip) |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`, dış kaynak bağlantısının açılması) değiştirmez. OPEN-012 açık kalır.
