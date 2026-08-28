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

Doğrulayıcı şunları fail-closed uygular:

- JSON duplicate key ve `NaN`/`Infinity` gibi standardın dışındaki sayılar reddedilir;
- şema alanları ve yer tutucu biçimleri birebir doğrulanır;
- sayısal hacim, concurrency, ölçüm ve eşik varsayılanları reddedilir;
- provider/edition/collation/isolation/topoloji için somut varsayılan reddedilir;
- kimlik doğrulama malzemesine benzeyen anahtar ve değerler reddedilir;
- owner, source, timestamp, commit, tool version, seed/hash, tekrar üretim ve
  redaksiyon kanıtı eksik bırakılamaz.

Bu doğrulama yalnız boş ve karar-nötr şablon bütünlüğünü kanıtlar. Doldurulmuş
kanıtın doğruluğu, ortam parity'si, test sonucu veya herhangi bir kabul kapısının
kapanması anlamına gelmez.
