# Sistem-of-Record Matrisi

| Veri/olay | Tek gerçeklik sahibi | Not |
|---|---|---|
| Hukuki taraf ve cari master | Cari | Şirket hesapları ayrı |
| Ürün/hizmet masterı | Ürün & Hizmet | İşlemler snapshot/referans kullanır |
| Fiziksel stok | Stok olay defteri | Pozisyon türetilmiş projeksiyon |
| Tahsis | Merkezi Tahsis Motoru | Stok hareketi değildir |
| Üretim planı/gerçekleşme | Üretim | Stok etkisi sahip servis komutuyla |
| Satın alma talebi | Satın Alma | İhtiyaçtır, taahhüt değildir; stok veya arz etkisi yok |
| Satın alma siparişi (tedarikçiye taahhüt) ve kalanı (gelecek arz) | Satın Alma | Kalan bugün kodda taahhüt − kabul; hedef formül `INV-006` (Onaylı − İptal − KabulNet − KesinRed/İade); fiziksel/serbest stok değildir (master §25.5); Stok `IPurchaseSupplyReferencePortV1` ile okur, kopyalamaz |
| Alış kabul payı (sipariş satırına bağlı) | Satın Alma | Aynı kabulün fiziksel girişi (lot, etiket, lokasyon, hareket) Stok olay defterindedir; ikisi tek host transaction'ında yazılır (`PurchaseGoodsReceiptHost`). Ölçülen kg Stok giriş miktarıdır; kantar/kapı kaydı Lojistik'indir, alış kabulüne bağı hedef (plan B-7, MOD-07) |
| Alış faturası eşleşmesi (hedef) | Satın Alma | Henüz yok (plan A5; ALS-D7); resmî fatura Mikro'da kalır; tedarikçi borcu mevcut "Açık kalem" satırındaki sahipte (Cari Finans) |
| Yükleme/sevkiyat/tartım | Lojistik | Plan ve fiilî ayrı |
| Dosya ve belge sürümü | Belge Kasası | Modüller referans taşır |
| Açık kalem | Göymen ERP Cari Finans | Resmî muhasebeyle mutabakat |
| Para girişi/çıkışı | Finans/Hazine | Dağıtım ayrı ilişkidir |
| Resmî e-fatura/e-arşiv | Mevcut resmî sistem | Taslak/status ERP’de |
| Resmî muhasebe fişi/yasal defter | Mevcut resmî sistem, ilk faz | ERP taslak ve mutabakat |
| Kullanıcı/yetki/onay | Sistem Yönetimi | AI yalnız taslak oluşturur |
| Arama indeksi/rapor | Türetilmiş | Hiçbir zaman SoT değildir |
