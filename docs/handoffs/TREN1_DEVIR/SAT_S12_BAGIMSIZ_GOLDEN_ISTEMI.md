# SAT-S12 bağımsız formül golden incelemesi

Hedef: yazar oturumundan ayrı ChatGPT oturumu. Bu, son K3 incelemesinin yerine geçmez.

```text
GöymenERP — bağımsız SAT-S12 satış marjı formül golden incelemesi.

Yazar şeridi değilsin. Kaynak aday dalı codex/tren-1-satis-kapanis-w8;
exact head e20f0ad14a44ecfe927060dbe0a2ff7e3eda9c8c.
Depo: sinangym/GoymenERP. Başta ve rapordan hemen önce remote head'i doğrula;
değişmişse eski head için onay üretme. Kod, test, branch, PR veya ayar değiştirme.

Önce AGENTS.md ve ilgili governance kurallarını oku. Bağlayıcı kaynakları
SINAN_KARARLARI_2026-11-09_SATIS_EK51.md ve
SINAN_KARARLARI_2026-11-10_SATIS_EK52.md içinde doğrula; özellikle
SAT-D259, SAT-D261, SAT-D263, SAT-D265, SAT-D267–275 ve daha yeni
ilgili kullanıcı kararlarını kontrol et. Dosya adını arayarak doğrula.
SAT-D265(5), formül golden'ının bağımsız ChatGPT şeridinde kilitlenip main'e
alınmasını şart koşuyor. Mevcut yazar testleri bu kabul yerine geçmez.

Karar metninden bağımsız beklenen sonuçları önce kendin kesin ondalık/rasyonel
aritmetikle çıkar; hesaplayıcıyı veya testlerin expected değerlerini oracle yapma.
Sonra şu kaynaklarla karşılaştır:
- Modules/Sales/GoymenERP.Modules.Sales.Domain/Drafts/SalesLineMargin.cs
- Modules/Sales/GoymenERP.Modules.Sales.Application/SalesDraftService.MarginCosts.cs
- gerçek Money değer nesnesi ve toplam hesaplayıcısı (yerlerini rg ile bul)
- tests/GoymenERP.Sales.UnitTests/SalesLineMarginCalculatorTests.cs
- ilgili margin snapshot birim ve gerçek-SQL test kaynakları.
İlk iki yol src/backend altındadır.

Net satışın hangi tutar olduğu, KDV/vade farkı/iskonto kapsamı, birim maliyetin
4 basamak iç hassasiyeti, yüzde paydası, yuvarlanmamış marjdan yüzde hesabı ve
Money yarımda sıfırdan uzağa kuralını kaynakla doğrula. Pozitif ve negatif marj,
sıfır/negatif net satış, .005 sınırı, yüzdeyi ara yuvarlamanın değiştirdiği örnek,
geçersiz/fazla hassas maliyet, birim/döviz uyuşmazlığı, eksik maliyet ve gerçek
sayısal taşma için bağımsız golden vektörleri üret. Mevcut kararda olmayan
maliyet veya döviz politikası seçme; belirsizliği açıkça bildir.

Kaynak-önce aşamasındayız: proje runtime Unit/SQL/HTTP/Vitest/browser/CI koşma.
Kendi bağımsız aritmetiğin serbesttir; uygulamayı çalıştırıp kendi beklenen
sonucunu uygulamadan üretme. Kod düzeltme, grant/rol/OPEN/production/Finance
STOP veya ayar aktivasyonu yok.

Çıktı Türkçe ve tek rapor olsun:
1. İncelenen tam SHA ve bağlayıcı karar kimlikleri.
2. Girdi, exact maliyet, exact marj, exact yüzde, gösterim tutarı/yüzdesi veya
   unavailable nedeni ve karar dayanağı sütunlu bağımsız golden tablo.
3. Varsa P0/P1/P2 bulguları dosya:satır ve somut karşı örnekle; yoksa açıkça yaz.
4. APPROVE veya REQUEST-CHANGES ve gerçek doğrulama sınırları.
5. Yazarın yeni, ayrı golden belgesine aynen aktarabileceği final golden metni.

Bir inceleme raporu tek başına main'de golden bulunduğunu kanıtlamaz. Golden
belgesi ayrıca mevcut DOCS kapısından geçmeli; bu inceleme kendi merge'ünü,
kendi imzasını veya son K3 onayını yetkilendirmez.
```
