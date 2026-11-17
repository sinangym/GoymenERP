# W8 mevcut kaynak durumu — son paket

- Son yayımlanan tren planı: **135/174 (%77,6)**.
- Yerel entegrasyon planı: **151/174 (%86,8) kaynak DONE**, **23 açık**.
- Bu güncelleme yeni kapsam veya yeni dalga açmaz. W8 son pakettir.
- 16 kapanışın gerçek commitleri yerel entegrasyon head'inin atası olarak doğrulandı; owner raporlarında kaynak derleme sonuçları vardır. Tüm yeni değişikliklerin birleşik dalga derlemesi, pin üretimi ve hafif kontrolleri henüz tamamlanmadığı için tren fast-forward/push yapılmadı.
- DONE kaynak kapsamıdır; Unit/gerçek SQL/HTTP/Vitest/browser/CI kabulü NOT_RUN. Sağlayıcı veya insan kapısı kapalı yüzey kullanılabilir sayılmaz.

## Bu kayıtla kaynak kapanışı sayılanlar

| Dilim | Kaynak commit | Owner raporu |
|---|---|---|
| STK-S11 | `667822a10851041c20759088f536735f26387ce1` | W8_stok_lojistik.md |
| STK-S13 | `b6ba56727e54910511d00db5fb0c4cb96c4bd36c` | W8_stok_lojistik.md |
| STK-S19 | `67fad5246354daf3cc4f0c484c90a3412f00bdbd` | W8_stok_lojistik.md |
| STK-S21 | `dfef53c6a40ab8acd3d9cd3f654c0470f2e842f0` | W8_stok_lojistik.md |
| STK-S29 | `c820c37d6ea846e0dbd7bfe56b5534bc6feeae0b` | W8_stok_lojistik.md |
| STK-SC36-b | `4bd80d9dc153f3692ecc1db8341e5146737f559f` | W8_stok_lojistik.md |
| STK-SC36-c | `c86f24b46f771de3e1cdccd59efc862619968db9` | W8_stok_lojistik.md |
| URT-S4 | `4d9b8bfca52560ea539ae936412b42184f61fcb9` | W8_uretim.md |
| URT-S5 | `2120b758e7f41fb5aeb7c75bcf1d2327e72b3937` | W8_uretim.md |
| URT-S8 | `cd584d806e0c7e1e97b9e933d5cf25da13280a85` | W8_uretim.md |
| URT-SC32 | `4be7e2b4aee8ddd18ba8923ad57a9ce66697e817` | W8_uretim.md |
| URT-SC33 | `59f30eb7ec53022f47289d71ac0a8a9abece0769` | W8_uretim.md |
| URT-SC36 | `87b44d2625742631e3e886800b4fcd0e44008d33` | W8_uretim.md |
| FIN-S17 | `5edcd80b09aa6a1b11fac33e558ba6d658c978a1` | W8_finans.md |
| FNS-SC32 | `5ee3b2d05768150805f20d343fad144c0afd2dcc` | W8_finans.md |
| FIN-SC36 | `a4a747383111efa48060937637cf887e9022d1ff` | W8_finans.md |

## Açık işlerin kapanış sırası

1. KIM-S19 gerçek makbuz/dekont numaralama: kaynak + gerçek EF hazır; son kaynak derleme zinciri sürüyor.
2. SAT-SC34 gerçek ERP resmî iptal kaydı: owner derlemeleri başarılı; merkez host/route/subscription bağlantısı ve gerçek dış sağlayıcı sınırı korunacak.
3. STK-S3 kullanılmayan fason ayırma: ayrı yetkili bırakma komutu yazılıyor.
4. STK-SC36-a: ilk alış maliyetinde invoice-line başına tek yuvarlama, gerçek fiziksel source bağı, posted attribution/Mixed/SAT422 bağlantısı tamamlanacak.
5. FIN-S14 gerçek muhasebe değeri/eşleme ve FIN-SC47-KREDI bileşen eşleme/fiş akışı tamamlanacak.
6. Kalan kimlik/alış/satış dilimlerindeki tarihsel PARTIAL raporlar gerçek merkez tüketicileriyle eşleştirilecek; gerçek eksik varsa düzeltilecek. SAT-S12 için SAT-D265(5) bağımsız golden/main kapısı açıktır; ayrı tek istem hazırlanmıştır.
7. Bütün kaynak kapsamı bittikten sonra birleşik derleme/pin/hafif kontroller, tren kaydı; ardından kullanıcının yetkilendirdiği son tam kabul koşumu ve tek taslak K3 PR. İnsan/sağlayıcı kapısı kod yüzdesine gizlenmez.
