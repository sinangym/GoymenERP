# Sistem-of-Record Matrisi

| Veri/olay | Tek gerçeklik sahibi | Not |
|---|---|---|
| Hukuki taraf ve cari master | Cari | Şirket hesapları ayrı |
| Ürün/hizmet masterı | Ürün & Hizmet | İşlemler snapshot/referans kullanır |
| Fiziksel stok | Stok olay defteri | Pozisyon türetilmiş projeksiyon |
| Tahsis | Merkezi Tahsis Motoru | Stok hareketi değildir |
| Üretim planı/gerçekleşme | Üretim | Stok etkisi sahip servis komutuyla |
| Yükleme/sevkiyat/tartım | Lojistik | Plan ve fiilî ayrı |
| Dosya ve belge sürümü | Belge Kasası | Modüller referans taşır |
| Açık kalem | Göymen ERP Cari Finans | Resmî muhasebeyle mutabakat |
| Para girişi/çıkışı | Finans/Hazine | Dağıtım ayrı ilişkidir |
| Resmî e-fatura/e-arşiv | Mevcut resmî sistem | Taslak/status ERP’de |
| Resmî muhasebe fişi/yasal defter | Mevcut resmî sistem, ilk faz | ERP taslak ve mutabakat |
| Kullanıcı/yetki/onay | Sistem Yönetimi | AI yalnız taslak oluşturur |
| Arama indeksi/rapor | Türetilmiş | Hiçbir zaman SoT değildir |
