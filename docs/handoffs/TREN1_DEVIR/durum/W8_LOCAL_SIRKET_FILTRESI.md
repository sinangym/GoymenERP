# W8 tracked maliyet kayıtlarında açık şirket filtresi

Durum: SOURCE_DONE, entegrasyon derleme/testi bekliyor. Kaynak tabanı
`a8f4440ec88da785ee08ff5d2fa4500e72bfb4e8`; GOV-D9 ve AGENTS CompanyId değişmezi.
Yeni dilim, dalga veya işletme politikası açılmadı.

- `InventoryStore.ExactCostPools`, `InventoryStore.MovementCosting` ve
  `InventoryManufacturingCostGenerationParticipant` içindeki pool, recognition ve
  generation `.Local` seçimlerinde mevcut aktif şirket kimliği açıkça karşılaştırılır.
  EF `.Local` global sorgu filtresini uygulamadığından, aynı konum/ürün/döviz veya
  hareket/nesil kimlikleri taşıyan başka şirketin tracked kaydı seçilmez.
- Önceden açık CompanyId koşulu bulunan fiziksel/pending/alış-etkisi seçimleri korunur.
  Hesap, değer, yuvarlama, sıra, transaction ve SQL değişmedi; allowlist/pin eklenmedi.
- `PurchaseReceiptReversalHost` dosyasındaki kullanılmayan `System.Data.Common`
  importu kaldırıldı; gerçek GetDbConnection owner-transaction köprüsü korunur.
- Mevcut `InventoryExactCostPoolModelTests` içine iki theory/dört örnek yazıldı:
  TL ve USD'de aynı anahtarlı foreign tracked poollar aktif şirketin maliyetini
  sağlayamaz veya onun güncellemesini alamaz; foreign recognition/generation da
  aktif şirketin henüz kanıtlanmamış pending maliyetini gizleyemez.
- Testler gerçek `ReadCostPoolAsync`/`WriteCostPoolAsync` yolunu InMemory üzerinde
  kullanır. Bu yalnız ChangeTracker.Local izolasyon birim kanıtı için yazılmıştır;
  SQL transaction/constraint/host kabulünün yerine geçmez.

`git diff --check`: exit 0. Derleme, test, gerçek SQL, Node, HTTP ve tarayıcı: NOT_RUN;
entegrasyon sahibinin ağır işlem sırası bekleniyor. İşletme tutarı/izin/grant/rol,
migration/model, production, OPEN durumu ve Finance STOP değiştirilmedi.
Bu rapor yazar desteğidir; bağımsız exact-head/K3 incelemesi değildir.
