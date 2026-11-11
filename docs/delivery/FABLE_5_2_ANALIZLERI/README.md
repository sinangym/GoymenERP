# Fable 5.2 Analizleri

Bu klasör, GöymenERP'nin Fable 5.2 ile yapılan salt-okunur analizlerini diğer oturumların ve ajanların görebilmesi için saklar. Belgeler sohbette teslim edildikten sonra kullanıcının isteğiyle gövdeleri değiştirilmeden işlendi.

| Belge | Tarih | Kapsam | Başlangıç commit'i |
|---|---|---|---|
| [01 — Mevcut durum ve yol haritası](01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md) | 10 Kasım 2026 | Hedef sistem, modül durum tablosu, altı uçtan uca akış, F-001…F-032 bulguları, aşamalı plan, OP-01…OP-16 devir listesi, beş sahip sorusu | `9091e7d` |
| [02 — Doğrulanmış geliştirme planı](02_DOGRULANMIS_GELISTIRME_PLANI_2026-11-11.md) | 11 Kasım 2026 | 1. aşama bulgularının kod ve kararlarla yeniden doğrulanması, bağımlılığa göre geliştirme sırası, uygulanabilir ayrıntıda ilk üç paket | `9091e7d` |

## Nasıl kullanılır

- 02, 01'in üstündedir; çelişkide 02 geçerlidir. 01'in başındaki not düzeltilen bulguları listeler.
- Kaynak önceliği `AGENTS.md`'deki gibidir: kullanıcının en son kararı > tarihli `SINAN_KARARLARI_*` > `KARAR_SICILI.md` > master rapor ve feature spec > bu analizler.
- Bu belgeler karar, onay, görev ataması ya da `OPEN-*` kapatma değildir. Hangi paketin uygulanacağını kullanıcı seçer.
- Satır numaraları başlangıç commit'ine aittir. Uygulamadan önce güncel `HEAD` ile karşılaştırın; Issue #66 ve açık PR listesinden aynı dilime başka bir şeritte başlanmadığını teyit edin.

## Güncel öncelik özeti (02'den)

| Sıra | Paket | Bulgu ve karar | Büyüklük | Ön koşul |
|---|---|---|---|---|
| 1 | Hazırlık denetimi kapsamı | F-022 | Küçük, K1/K2 | Yok |
| 2 | Lojistik sevk edeni bildirir (`ShippedBy`) | F-006, B-33; SAT-D216, SAT-D222 | Orta, K3, migration | Stok & Lojistik dalgası teyidi |
| 3 | Alış siparişinde hazırlayan ≠ onaylayan | F-015; ALS-D3 | Orta, K3, migration | A2a-5'ten öne alma teyidi |
| 4 | Üretim çıktısı soy ağacı | F-008 | Orta–büyük, K3 | Model seçimi |
| 5 | Maliyet zinciri | F-011 | Büyük, K3 | Birim çevrimi ve para birimi kararları |

Karar bekleyen P0'lar paket değil, soru olarak duruyor: rol atama (F-001), giriş sağlayıcısı (OPEN-013), resmî fatura verisinin sahibi (F-005), ürün çalışma zamanının açılması (B-06).

## Uygulayıcı ajan için çalışma kuralı (aynen)

> "Uygulamaya başlamadan önce mevcut HEAD ve çalışma ağacını raporun başlangıç sürümüyle karşılaştır. İlgili kod değişmişse yalnız etkilenen bulguları yeniden doğrula. Fable raporunu tartışılmaz gerçek kabul etme. Yalnız kullanıcı tarafından uygulama için seçilen paketi gerçekleştir; diğer paketlere kendiliğinden geçme. Bu rapor production, migration, seed, gerçek grant, finans aktivasyonu, merge veya deployment onayı değildir."
