<!-- ERP000M_EVIDENCE_INPUT_PACK_README_V1 -->

# ERP-000M PoC Kanıt Girdi Paketi

- Implementation state: `NO-GO / BLOCKED`
- Decision closure: `NONE`
- Artifact purpose: `DECISION-NEUTRAL INPUT TEMPLATE`

Bu dizin, `ERP-000M_SQL_SERVER_POC_READINESS.md` içindeki karar öncesi kanıt
girdilerini standartlaştıran boş şablonları içerir. Paket bir provider, edition,
collation, isolation seviyesi, topoloji, performans eşiği, kapasite değeri veya
production davranışı seçmez; `ADR-003`, `ADR-010`, `OPEN-014`, `OPEN-015` ve
`OPEN-017` kararlarını kapatmaz.

## Dosyalar

- `ENVIRONMENT_INVENTORY_TEMPLATE.md`: sahipli ve kaynak gösterilebilir ortam/
  capability envanteri.
- `WORKLOAD_PROFILE_TEMPLATE.json`: anonimleştirilmiş hacim, concurrency ve
  operation-class girdileri.
- `EVIDENCE_MANIFEST_TEMPLATE.json`: üretilen kanıt, hash, tekrar üretim ve
  redaksiyon referansları.

## Yer tutucu sözleşmesi

Her kullanıcı girdisi aşağıdaki biçimde karar verilmemiş bir yer tutucu olarak
teslim edilir:

```text
__REQUIRED_INPUT__:OWNER__
__REQUIRED_INPUT__:OPEN-015:CONCURRENT_ACTOR_COUNT__
```

Şablonlara gerçek değer, bağlantı bilgisi, kimlik doğrulama malzemesi veya hassas
şirket verisi eklenmez. Doldurulmuş çalışma kopyaları ayrı, erişim kontrollü kanıt
alanında tutulmalı; repository'ye yalnız kabul edilmiş redakte kanıt ve referansı
alınmalıdır.

## Kendi hash alanı sözleşmesi

Kendi SHA-256 değerini taşıyan üç artefakt için bağlayıcı preimage sözleşmesi
`ERP000M-SELF-SHA256-V1`'dir:

| Artefakt | Kanonik alan | Preimage sentinel değeri |
|---|---|---|
| Ortam envanteri | Markdown `inventory_hash_sha256` değer hücresi | `__REQUIRED_INPUT__:ENVIRONMENT_INVENTORY_HASH_SHA256__` |
| İş yükü profili | JSON `/evidence/workload_profile_hash_sha256` | `__REQUIRED_INPUT__:WORKLOAD_PROFILE_HASH_SHA256__` |
| Kanıt manifesti | JSON `/reproducibility/manifest_hash_sha256` | `__REQUIRED_INPUT__:MANIFEST_HASH_SHA256__` |

Üretim ve doğrulama algoritması şöyledir:

1. Artefaktın self-hash alanı dışındaki bütün içeriği son haline getirilir.
2. Girdi yolu `resolve` edilmeden lexical mutlak yola çevrilir. Drive/UNC kökünden
   dosyanın doğrudan üst dizinine kadar mevcut her yol bileşeni no-follow `lstat`
   ile denetlenir; kök, kendi parent'ına eşit olduğunda yürüyüş güvenli biçimde
   biter. Herhangi bir ata bileşen symbolic link, reparse point veya dizin dışıysa;
   son bileşen symbolic link, reparse point, dizin ya da özel dosyaysa doğrulama
   fail-closed reddedilir. Windows'ta drive/UNC anchor'ı dışındaki `:` içeren
   alternate-data-stream/named-stream yolları da normal paket dosyası sayılmaz ve
   reddedilir. Ata ve dosya kimliği okumadan önce/sonra aynı kalmalıdır.
3. Her artefakt tek descriptor üzerinden yalnız bir kez okunur. Windows'ta
   descriptor `CreateFileW` ile yalnız `FILE_SHARE_READ` paylaşımı verilerek açılır;
   descriptor açıkken yeni write/delete açılışları paylaşım kuralıyla reddedilir.
   POSIX'te son bileşen için varsa `O_NOFOLLOW` ve okumadan önce/sonra no-follow
   kimlik/metadata denetimi uygulanır; POSIX için desteklenmeyen exclusive-writer
   garantisi ileri sürülmez.
4. Dosya UTF-8 ve BOM'suz olmalıdır. Satır sonu, JSON anahtar sırası/boşluğu,
   Markdown boşluğu veya Unicode içeriği normalize edilmez; bunların tamamı kanıt
   byte'larının parçasıdır.
5. Artefakt kimliği ve kanonik alan yapısal olarak doğrulanır. Alan tam bir kez
   bulunmalıdır; eksik, tekrarlı, escaped, kısa, küçük harfli veya hex dışı hash
   değeri fail-closed reddedilir.
6. Yalnız kanonik alandaki değer, yukarıdaki artefakta özgü sentinel ile
   değiştirilir. Dosyanın başka yerindeki aynı metni değiştiren global replace
   uygulanmaz.
7. Ortaya çıkan exact UTF-8 byte dizisinin SHA-256 değeri hesaplanır ve 64 karakter
   büyük harf hex olarak kanonik alana yazılır. Bundan sonra başka byte değişmez.
8. Doğrulama, kalıcı dosyayı tek yakalanmış byte dizisi olarak okur, aynı tek alanı
   sentinel'a döndürür, SHA-256 değerini yeniden hesaplar ve kalıcı değerle
   karşılaştırır.

Bu sözleşme yalnız descriptor açıkken yakalanan exact byte dizisini ve o diziden
hesaplanan digest/hash bağlarını doğrular. Descriptor kapandıktan sonraki dosya
değişmezliğini garanti etmez. İçe aktarma, karar kanıtı kullanımı veya başka her
tüketim sınırında paket yeniden doğrulanmalıdır.

Paket mühürleme sırası ortam envanteri, iş yükü profili ve kanıt manifestidir.
Manifestteki `environment.inventory_hash_sha256` ve
`workload.profile_hash_sha256`, kaynak dosyalardan yeniden hesaplanan değerlerle
aynı olmalıdır. `*_ref` alanları digest'e bağlı olmayan kararlı referanslardır;
manifest digest'i daha erken bir artefakta gömülerek döngü kurulmaz.

## Doğrulama

Repository kökünden yalnız Python standart kütüphanesiyle çalıştırılır.
Windows için:

```powershell
py -3 tests/erp000m_evidence/validate_erp000m_evidence.py
py -3 -m unittest discover -s tests/erp000m_evidence -p "test_*.py"
```

Linux/macOS için:

```bash
python3 tests/erp000m_evidence/validate_erp000m_evidence.py
python3 -m unittest discover -s tests/erp000m_evidence -p 'test_*.py'
```

Erişim kontrollü alandaki doldurulmuş üçlü paket read-only doğrulanabilir:

```text
python3 tests/erp000m_evidence/validate_erp000m_evidence.py --verify-filled-pack ENVIRONMENT.md WORKLOAD.json MANIFEST.json
```

Doğrulayıcı şunları fail-closed uygular:

- JSON duplicate key ve `NaN`/`Infinity` gibi standardın dışındaki sayılar reddedilir;
- şema alanları ve yer tutucu biçimleri birebir doğrulanır;
- sayısal hacim, concurrency, ölçüm ve eşik varsayılanları reddedilir;
- provider/edition/collation/isolation/topoloji için somut varsayılan reddedilir;
- kimlik doğrulama malzemesine benzeyen anahtar ve değerler reddedilir;
- owner, source, timestamp, commit, tool version, seed/hash, tekrar üretim ve
  redaksiyon kanıtı eksik bırakılamaz;
- opt-in doldurulmuş paket modunda üç self-hash kalıcı byte'lardan yeniden
  hesaplanır ve manifestin ortam/iş yükü hash bağları doğrulanır; bu sonuç yalnız
  o çağrıda yakalanan byte dizileri için geçerlidir ve her tüketim sınırında yeniden
  üretilmelidir.

Varsayılan doğrulama yalnız boş ve karar-nötr şablon bütünlüğünü kanıtlar. Doldurulmuş
paket modu da yalnız byte bütünlüğünü ve hash bağlarını kanıtlar. Kanıtın işsel
doğruluğu, ortam parity'si, test sonucu veya herhangi bir kabul kapısının kapanması
anlamına gelmez.
