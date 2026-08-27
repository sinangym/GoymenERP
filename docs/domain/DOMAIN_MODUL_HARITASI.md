# Domain ve Modül Haritası

| Modül | Sahip olduğu temel veri | Başka modüllere sunduğu sözleşme |
|---|---|---|
| Platform | Şirket, kullanıcı, rol, görev, onay, audit | CompanyContext, Authorization, Approval, Audit |
| Cari | Taraf, CariHesap, adres, kişi, limit, sicil | Party/Account query ve risk girdileri |
| Katalog | Ürün, hizmet, birim, paket, fiyat | Product snapshot ve conversion service |
| Satış | Talep, teklif, satış siparişi, revizyon | Demand/fulfillment ihtiyacı |
| Satın Alma | Talep, sipariş, termin, kabul | Future supply ve accepted receipt |
| Stok | Stok olayı, pozisyon, etiket, blokaj | Available position, movement command |
| Tahsis | Kaynak–ihtiyaç ilişkisi | Allocation lifecycle |
| Üretim | Plan, reçete/formül snapshot, kuyruk, gerçekleşme | Planned/actual output events |
| Lojistik | Yükleme, araç, sevkiyat, tartım, transfer | Shipment/receipt realization |
| Belge | Blob, belge, sürüm, kayıt bağlantısı | Secure document reference |
| Cari Finans | Açık kalem, dağıtım | Open item lifecycle |
| Hazine | Banka/kasa, para kaynağı, çek/senet/POS/DBS | Financial source events |
| Muhasebe | Hesap planı, fiş, dönem, mizan | Accounting classification/status |
| Raporlama | Türetilmiş semantik projeksiyon | Read-only analytics |
| AI | Tool catalog, prompt/policy, suggestion/audit | Yetkili ve şemalı yardım |

## Sınır ilkesi

Bir modül başka modülün tablosuna doğrudan yazmaz. Yazma, sahip modülün komut servisi ve transaction/event sözleşmesi üzerinden yapılır.
