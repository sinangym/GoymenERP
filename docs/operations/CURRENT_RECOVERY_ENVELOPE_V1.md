# R05.1 — mevcut sürümün kaynak düzeyinde kurtarma zarfı v1

Bu araç, release manifest ve onun adını verdiği recovery inventory **baytlarından**
tek bir kaynak sözleşmesi üretir. Başarılı sözleşme doğrulaması; kaynak kabulü,
çalışan ortam izolasyonu, yedek seti, tutarlı kurtarma noktası veya restore kanıtı
değildir. Çıktı daima `SOURCE_ONLY_NOT_REHEARSED`, `deployable=false` ve
`recoveryEligible=false` taşır.

Dayanak: [D-RESTORE=A gerçek kullanıcı makbuzu, madde 5](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6210091477)
ve [R05.1 karar/kapsam paketi](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6207345638).
Gerçek restore/prova R05.2/R05.3 ve isimli ortam/veri sahiplerinin ayrı yetkisindedir.

## Girdiler ve güven sınırı

- Yalnız kabul edilmiş `goymenerp.release-manifest.v1` ve
  `goymenerp.recovery-surface-inventory.v1` sözleşmeleri desteklenir. R02.1-B/v2
  kaynakları mevcut sayılmaz; v2 etiketi fail-closed reddedilir.
- Tam kapalı v1 şemaları doğrulanır; birkaç seçili alan veya sayım yeterli değildir.
  Manifest/inventory kaynak kimliği, inventory'nin gerçek bayt hash'i, sayımlar,
  benzersiz context/migration/depo kimlikleri ve tutarlı dosya referansları bağlanır.
- Yeni araç, mevcut recovery generator'ın yalnız sabitlenmiş şema doğrulama
  fonksiyonunu kullanır. Doğrulanan kaynak baytları bellekte derlenir; generator'ın
  `main`/envanter üretimi/Git veya servis fonksiyonları çağrılmaz, pyc yüklenmez.
  Motorun Git-blob SHA256 değeri güncel accepted main
  `8c8559a41fc6d43386ebc0787f28f445651058f0` kaynağından sabittir. İki mevcut şemanın
  Git-blob SHA256 değerleri `b6ed7ca5496f8f34314a0a4b2d3f13467bb5ffe7`
  kaynağından değişmeden kalır. Yalnız checkout CRLF → LF normalizasyonu uygulanır.
  Değişen/eksik kaynak reddedilir; ayrı inceleme olmadan yeni sürüme veya farklı
  bir doğrulayıcıya geçilmez.
- `tools/contract_schema_subset.py`, yeni paket, beşinci helper veya merkezi
  generator değişikliği yoktur. Null kanıt yuvaları mevcut motorun desteklediği
  `const: null` ile kapalıdır; desteklenmeyen `type: null` eklenmez.
- Araç, girdi JSON'unun anlattığı source commit/tree'nin gerçekten kabul edilmiş
  kaynak olduğunu veya envanterin gerçek checkout'u eksiksiz taradığını kanıtlamaz.
  Koordinatörün exact-source/admission makbuzu ayrıca gerekir. Kendi içinde tutarlı
  sentetik/verisi değiştirilmiş bir çift de yalnız sözleşme/bayt tutarlılığı sağlar;
  bunun için çıktı `sourceAdmission=NOT_VERIFIED` olarak kalır.

## Zarfın taşıdığı ve taşımadığı kanıt

| Yüzey | Kaynak düzeyinde taşınan referans / açık kapı |
|---|---|
| Uygulama | Commit/tree, manifest sürümü ve iki girdi dosyasının tam bayt SHA256'sı; build/imaj/uyumluluk kabulü yok. |
| Veritabanı | Envanterdeki context referansları; migration sayısı ve sıralı `path + TAB + sha256 + LF` deklarasyon özeti. Bu özet mevcut generator'ın farklı reviewed fingerprint'iyle karıştırılmaz. |
| Kalıcı depolar | Bütün beyan edilmiş idempotency/outbox/inbox/delivery satırlarının kimlik, context, entity, tablo ve kaynak referansları; her biri için yakalanmamış high-water/key-set uzlaştırma yuvası. İş kaydı veya gerçek checkpoint yok. |
| Documents | Sabit sözleşmedeki anahtar **adları**, nesne anahtar deseni ve yol sınıfları; gerçek konum/değer, secret veya nesne içeriği yok. Nesne manifesti `NOT_CAPTURED`. |
| Dış etkiler | Worker, dispatcher, e-posta, webhook, banka/e-belge, nesne callback'i ve diğer egress için `REQUIRED_DISABLED_NOT_VERIFIED`; gerçek `DISABLED`, `NOT_PRESENT` veya `NOT_STARTED` kanıtı üretilmez. |
| Kurtarma hedefi | Tekil izole hedef gerekliliği; hedef oluşturulmaz, bağlantı veya provider seçilmez. |
| Kabul | Kaynak admission, runtime izolasyonu ve tutarlı recovery point doğrulanmamış; yedek yakalanmamış; prova yapılmamış. |

Sayım sabitleri rapordan kopyalanmaz. Girdi coverage'ı gerçek v1 envanter şemasına
ve girdi listelerine göre kontrol edilir; rapordaki eski 18/96/21 sayılarını yeniden
üretmek tek başına kaynak kanıtı değildir. Şemalar değişirse yeniden source review
gerekir. Süre, saklama değeri, provider, RPO/RTO veya yeni işletme politikası seçilmez;
OPEN-014/015/017/018/021 kapıları korunur.

## Kalıcı depoların sözleşme gereklilikleri

| Tür | Gereklilik |
|---|---|
| idempotency | DB ile tutarlı geri dönüş; saklanan sonucu replay et, iş etkisini yeniden çalıştırma. |
| outbox | DB ile geri dönüş; ayrı doğrulamaya kadar dispatch etmeden tut. |
| inbox | Dedup kimliğini koru; aynı mesaj ikinci kez işlenmesin. |
| delivery | DB ile geri dönüş; high-water/sıra ve dış teslim makbuzuyla uzlaştır. |

`reconciliation.status=NOT_CAPTURED`, `highWaterMark=null`, `keySetSha256=null`
gerçek veri/başarı yerine açık eksik kanıt yuvalarıdır. Bu sürümde doldurulmuş kanıt
gibi sunulamazlar; R05.2/R05.3'ün yetkili kanıt sözleşmesi ayrıca gelir.

## Daha sonra yetkili slotta kullanım

Önce upstream manifest ve recovery inventory, kendi kabul edilmiş exact-source
üretici/proof akışlarından alınır. Bu belge onları otomatik çalıştırmaz ve slot açmaz.
Operatör, byte-preserving dosyaları ayrı kanıt alanında tutar; eski PowerShell metin
redirect'iyle UTF-16'ya dönüştürülmüş bir dosya aynı bayt kaydı sayılmaz.

```text
python -I -S -B tools/validate_current_recovery_envelope.py build --release-manifest <manifest.json> --recovery-inventory <inventory.json>
python -I -S -B tools/validate_current_recovery_envelope.py validate --release-manifest <manifest.json> --recovery-inventory <inventory.json> --envelope <envelope.json>
```

`build` kanonik UTF-8 JSON'u stdout'a verir; `validate` yalnız sözleşme/bayt kontrol
özetini verir. Exit 0 bu kontrolün sonucudur, recovery veya admission PASS değildir.
Exit 1 sözleşme/input reddi; exit 2 argüman veya dosya/çıktı erişim hatasıdır. Hatalar
payload, secret, kaynak JSON veya kullanıcı dosya yolunu yazdırmaz. Ağ, SQL, object
store, ortam yapılandırma değerleri veya provider komutu kullanılmaz. CLI en fazla
8 MiB JSON, 64 seviye derinlik ve sınırlı container/node bütçesi kabul eder; bunlar
ayrıştırma kaynak sınırlarıdır, ERP saklama veya veri politikası değildir.

Test kaynağı:
`tests/current_recovery_envelope/test_validate_current_recovery_envelope.py`.
İlgili komut yalnız ayrı izinli test slotunda çalıştırılır:

```text
python -I -S -B -m unittest discover -s tests/current_recovery_envelope -p test_*.py -v
```

## Sonraki prova için kapılar

İsimli ortam/veri sahipleri; exact-source kabulü; gerçek ve birbirleriyle tutarlı DB,
Documents ve kalıcı işlem kayıtları; ölçülmüş uyumluluk; yakalanmış checkpoint ve
dış teslim makbuzları; izole ağ; etkisiz/default-deny kimlikler; doğrulanmış kapalı
worker/connector/egress gerekir. Eksik/bilinmeyen kanıtla trafik veya dış etki açılmaz.
Bu bir prova sırası tasarımıdır; yedek, restore, sandbox veya trafik açma komutu değildir.

Bu source-only teslimde unit/CLI/generator/build/restore/SQL/browser/scanner/CI
**NOT_RUN**. K3 iki bağımsız exact-head inceleme, insan/CI ve gerçek kabul kapıları
açıktır. Kaynak hazır olmak R05.1/R05.2/R05.3 kabulü, 18/128 artışı veya production
yetkisi değildir. Finance STOP, AI A-contract-only ve mevcut permission/grant sınırları
korunur.
