# Dağıtım, Yedek, RPO ve RTO

## Geçici hedefler

- RPO: 15 dakika
- RTO: 4 saat
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
