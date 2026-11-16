# W8 Stok / Lojistik

## STK-S19 — PARTIAL

- Kararlar/spec: STOK F017 §3.7, ALIS F017 §3.7–3.8; ALS-D86, ALS-D91, ALS-D92, ALS-D93; 7B kısmi yeniden açma kararı.
- Inventory owner checkpoint commit'i: `67fad5246`.
- Inventory owner kaynağı: orijinal kabul ve dönüşüm bağı korunur. Ayrı değişmez tersleme kaydı, yalnız orijinal miktar/kg oranıyla tam temsil edilebilir kısmı izler. Yeniden açılan + kapasite nedeniyle bırakılan + fiziksel/talep bağı artık bulunmayan pay = geri alınan paydır. Önceden Released pay canlanmaz; LinkFailed tüketilmiş sayılmaz.
- Fiziksel tahsis geri alma, aynı kimlikli gelecek ayırmayı talep ve tedarik tavanına sığan miktar kadar açma, kalan uygun payı `acceptance-reversed-no-supply` ile bırakma; kapalı satış veya aktif olmayan fiziksel tahsiste gelecek ayırmaya dokunmama uygulanmıştır.
- Participant açık host işlemini ödünç alır. Host bütün satış satırlarını, ardından alış satırı birleşimini kilitler; taze kümenin altküme kontrolü ve yalnız yazı öncesinde üç sınırlı tekrar vardır. Purchasing'in yazı sonrası kaynağı aynı bağlantı ve işlem üzerinden okunur. Factory'nin gerçek Purchasing owner implementoruna merkezi kaydı entegrasyonda tamamlanacaktır.
- Migration: `20261116210057_AddInventoryGoodsReceiptReversals`, EF üretimi. Down ilk işlemde **THROW 51950** ile tersleme geçmişi veya kind-16 hareket varken reddeder. Şirket filtreleri ve aynı şirketli özgün kayıt FK'leri bulunur. Yeni ham SQL veya filtre bypass'ı yoktur; yeni context construction K3 incelemesinde değerlendirilmelidir.
- Birim/model test kaynakları; gerçek SQL'de kısmi açma + önceki Released payın korunması + replay, rollback + aynı anahtarla başarı, yanlış güncel talep ürünü, stok bulunmayan bucket ve Down guard fact kaynakları yazılmıştır. SQL fact'lerindeki güncel Sales/Purchasing owner cevapları açıkça sentetiktir; birleşik gerçek host kanıtı yerine geçmez.
- Dar bağımsız kaynak okumasında ham owner exception'ının 500'e kaçması ve güncel talep ürün kimliği çitinin eksikliği bulundu; ikisi de kapatıldı. Özgün dönüşüm sırası immutable command cevabından okunur; bugünkü terminle geçmiş yeniden sıralanmaz.
- Runtime birim/SQL/CI: **NOT_RUN**. Inventory.UnitTests, Purchasing.UnitTests ve Api.IntegrationTests checkpoint derlemeleri exit 0, 0 uyarı/hata. Ardından yeni Inventory reversal participant denetimi Audit owner transaction portuna geçirildi: doğrudan Audit table yazısı/interceptor yok, aynı işlemde own Save + WritePending, hata halinde DiscardPending ve fresh-batch guard vardır. Bu son ekin derlemesi ortak W7 Audit writer `HasPendingDrafts` kaynağını ve yeni Purchasing implementorunun entegrasyonunu bekler.
- Kalan: gerçek Purchasing undo/correction host ve API zinciri, bütün çağıranların kilit/replay/authority uyumu, yeni kabulde yeniden açılan kimliklerin önce tüketilmesi, birleşik owner SQL yarış/fault kanıt kaynakları. Çok kabulün aynı genel satırda tek net olarak işlenmesi tamamlanmadan batch DONE değildir.

## ALC-S6 — PARTIAL

- Paylaşım commit'i: `5343b1c45` — Purchasing'in tek kullanımlı aynı-işlem acceptance adjustment owner sözleşmesi; özgün kabul hedefi, replay-first, gerçek net açık miktar, post-write supply okuması, yeni kabul kimliği/miktarı.
- Purchasing domain/persistence ayrı owner tarafından uygulanır; bu ekip o owner dosyalarına yazmaz. Fiziksel kg ile satın alma birimindeki geri alınan miktar ayrı gerçeklerdir; tartı toleransından dönüşüm katsayısı türetilmez.
- Mevcut fatura maliyeti / elle maliyet istisnası onay kapıları korunur. Bekleyen veya reddedilen maliyet istisnası fiziksel hareket ve sipariş açılması üretmez.
- API-44 route ve service/host kaynakları yazıldı: ayrı ölçülmüş kg ve satın alma miktarı, eski kabul kimliği, expected version, neden ve optional gerçek yeni kabul. Ters + yeni aynı işlemde işlenir; geçici yeniden açma bütçesi owner'ın gerçek `NewAcceptedQuantity` sonucudur. Yeni kabul önce ters kaydın yeniden açtığı kimlikleri tüketir, mevcut link-failure uyarı/sweep yolu korunur.
- Immutable başarılı adjustment cevabı ve terminal ret aynı Purchasing.Attempts command/key tekilliğini paylaşmalıdır. Replay bugünkü siparişin restore edilmesine dayanmaz; owner Refused cevabı IsReplay ve özgün correlation ile döndürür. Terminal kayıt yarışı kazananı aynı immutable owner okuyucusundan çözülür. Bu ortak kayıt root owner implementorunun entegrasyon yükümlülüğüdür.
- Sentetik service authority/company/actor, terminal replay/payload uyuşmazlığı, teknik hata sonrası aynı anahtar ve canonical wire testleri; host ön kilit sırası, tüm alış birleşimi, küme büyümesi ve cancellation test kaynakları yazıldı. Runtime **NOT_RUN**.
- Kalan: gerçek kabul geri alma/düzeltme ve replacement-expected iade girişlerinin owner zincirine bağlanması; pending onay kaydında gerekli satın alma miktarı/sürüm gerçeklerinin saklanması; eski replay payload uyumu. Runtime **NOT_RUN**.

## Merkezi entegrasyon listesi

- Yeni migration ve THROW 51950; recovery/release migration sayıları.
- Yeni SQL fact tam adları; ilişkisel envanter ve test grupları.
- InventoryDbContext ve değişen ilgili kaynak fingerprint'leri; yeni context construction için gerekçeli K3 değerlendirmesi.
- `ReversePurchaseGoodsReceiptV1` OpenAPI route/kontrat üretimi; gerçek owner factory ve `IPurchaseReceiptReversalTransactionV1` DI kayıtları. Owner factory dört bağımsız handle/company/token girdisini aynı scope Audit collector/writer ile gerçek Purchasing participant'a bağlar.
- Hiçbir grant, rol ataması, production aktivasyonu, dış sevk/ETTN kapısı veya açık karar durumu değiştirilmedi.
