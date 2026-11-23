# W8 SAT-D417 plan üstü sevk istisnası

Doğrudan kullanıcı cevabı: “100 kg kalıp 20 kg için ayrı istisna kaydedilsin, satış müdürü,”. [6963301286](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6963301286) oturum cevabının açıkça işaretlenmiş ajan aktarımıdır; soru6963231247'ye yanıt verir. Eski karar dosyaları yerinde değiştirilmez.

- Talimatın plan miktarı korunur. Ek miktar ayrı şirket/talimat/satır kaydında hazırlanır; satış müdürü onaylar. Yükleme/sevk kullanımı gerçek miktar ve açık istisna kaydıyla izlenir. Gerçekte yüklenmeyen miktar sevk edilmez; sipariş, risk ve stok owner sınırları korunur.
- Yeni merkezi kayıt adı `logistics.shipments.plan-excess.approve`, sabit `LogisticsShipmentPlanExcessApprove`: yalnız All ve CompanyWideOnly kaydı. Gerçek rol/grant veya Critical sınıfı seçilmedi. Bu ad genel Lojistik okuma, yükleme veya sevk hakkını kendiliğinden vermez.
- Mevcut yükleme yetkisi hazırlığı, ayrı dar onay yetkisi yalnız kendi purpose listesini/detayını/onayını koruyacak. Kullanıcı yeni maker-checker kuralı seçmedi; başka Finans kuralları bu akışa kopyalanmaz.
- Identity kayıt testlerinin mevcut deny-first/şirket geneli/seed-grant-yok vektörleri yeni adı da kapsar. Kaynak derlemesi **QUEUED**; testler çalıştırılmadı.
- Gerçek Logistics model/EF/komut/ekran/test kaynakları aynı STK-S3/W8 kapsamında owner tarafından hazırlanıyor. İki yeni teknik shell yüzeyi staged kalacak; eski sekiz fason staged deklarasyonu korunacak. Merkezi mount/pin/son birleşik derleme ayrıca yapılır.

Bu kayıt tamamlanma veya K3 onayı değildir. Source durum172/174; runtime/SQL/browser/CI ve aktivasyon **NOT_RUN**.

## Merkezi montaj kaynağı

- Provider1.6 iki yeni staged deklarasyon taşır: `logistics.shipments.plan-excess.prepare` mevcut talimat detayının read+loads.record kontrolüdür; ayrı route değildir. `logistics.shipments.plan-excess.approve` yalnız yeni dar izinle `/lojistik/plan-ustu-sevk-onay` purpose sayfasıdır; genel Lojistik okuma yetkisi istemez.
- `PlanExcessApprovalPage`, Logistics route tablosu ve shell nav/capability eşlemesi hazırlanmıştır; hazırlama kontrolü global route/nav kataloğuna eklenmez. Normal ve fason fiziksel formlar owner kaynaklarıyla bu kontrolü tüketir.
- Ana uygulama `MapLogisticsPlanExcessEndpoints()` çağrısını yapar; ayrıca Add kaydı gerekmez. Endpoint ve sayfa owner kaynak checkpointine bağımlıdır. Bu merkez checkpoint **derlenmedi/QUEUED**; bağımlılık gelmeden tamamlandı sayılmaz.
- Eski Logistics host testindeki11/tümü-published beklentisi gerçek önceki transfer/fason kaynaklarını da koruyan exact22/tam12-published kümesine hizalandı. İki yeni surface false, sekiz eski fason false kalır. İzin/route/nav ve kontrolün ayrı route olmaması kaynak testleriyle korunur; test silinmez veya gevşetilmez.
