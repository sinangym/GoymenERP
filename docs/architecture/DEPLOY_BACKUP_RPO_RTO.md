# Dağıtım, Yedek, RPO ve RTO

## Hedefler

- RPO: 15 dakika
- RTO: 4 saat
- Günlük tam yedek + 15 dakikada bir işlem günlüğü yedeği; ayda bir geri yükleme denemesi
  (`KLN-D36`, `docs/decisions/SINAN_KARARLARI_2026-11-14_NS_CEVAPLARI.md`). Bu bir hedeftir;
  ölçümle kilitlenmesi, saklama süresi ve tesis dışı hedef açıktır (`OPEN-017`, `OPEN-014`).
- On-prem/hibrid, VPN erişimli
- Tesis dışı şifreli yedek
- Nesne deposu sürümleme

## Ortamlar

Dev, Test, Staging/UAT, Production ve gerektiğinde Eğitim.

## Production kapısı

- Veritabanı ve belge deposu beraber geri yüklenmiş
- Checksum/mutabakat geçmiş
- Migration ve rollback manifesti doğrulanmış
- Secret ve yapılandırma ayrı yönetilmiş
- Backup alarmı ve restore runbook çalışmış

## Uygulama

Betikler, doğrulayıcı ve adım adım işletim: `docs/operations/YEDEK_GERI_YUKLEME_RUNBOOK_v1.md`
(`infra/backup/`, `tools/verify_backup_restore.py`). Repo ve CI yedek almaz, geri yükleme
yapmaz ve zamanlanmış iş etkinleştirmez; bunlar insan kapısıdır.
