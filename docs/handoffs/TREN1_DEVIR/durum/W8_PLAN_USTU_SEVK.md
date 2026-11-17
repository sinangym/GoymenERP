# W8 SAT-D417 plan üstü sevk istisnası

Doğrudan kullanıcı cevabı: “100 kg kalıp 20 kg için ayrı istisna kaydedilsin, satış müdürü,”. [6963301286](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6963301286) oturum cevabının açıkça işaretlenmiş ajan aktarımıdır; soru6963231247'ye yanıt verir. Eski karar dosyaları yerinde değiştirilmez.

- Talimatın plan miktarı korunur. Ek miktar ayrı şirket/talimat/satır kaydında hazırlanır; satış müdürü onaylar. Yükleme/sevk kullanımı gerçek miktar ve açık istisna kaydıyla izlenir. Gerçekte yüklenmeyen miktar sevk edilmez; sipariş, risk ve stok owner sınırları korunur.
- Yeni merkezi kayıt adı `logistics.shipments.plan-excess.approve`, sabit `LogisticsShipmentPlanExcessApprove`: yalnız All ve CompanyWideOnly kaydı. Gerçek rol/grant veya Critical sınıfı seçilmedi. Bu ad genel Lojistik okuma, yükleme veya sevk hakkını kendiliğinden vermez.
- Mevcut yükleme yetkisi hazırlığı, ayrı dar onay yetkisi yalnız kendi purpose listesini/detayını/onayını koruyacak. Kullanıcı yeni maker-checker kuralı seçmedi; başka Finans kuralları bu akışa kopyalanmaz.
- Identity kayıt testlerinin mevcut deny-first/şirket geneli/seed-grant-yok vektörleri yeni adı da kapsar. Kaynak derlemesi **QUEUED**; testler çalıştırılmadı.
- Gerçek Logistics model/EF/komut/ekran/test kaynakları aynı STK-S3/W8 kapsamında owner tarafından hazırlanıyor. İki yeni teknik shell yüzeyi staged kalacak; eski sekiz fason staged deklarasyonu korunacak. Merkezi mount/pin/son birleşik derleme ayrıca yapılır.

Bu kayıt tamamlanma veya K3 onayı değildir. Source durum172/174; runtime/SQL/browser/CI ve aktivasyon **NOT_RUN**.
