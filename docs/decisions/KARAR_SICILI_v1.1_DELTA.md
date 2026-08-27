# Göymen ERP Karar Sicili — v1.1 Delta

**Tarih:** 27 Ağustos 2026  
**Kural:** Bu dosyadaki kararlar v1.0 kararlarını bozmaz; aynı konuda daha yeni bağlayıcı karar olarak uygulanır.

| DecisionId | Alan | Karar | Durum | Öncelik | Kaynak | Sahip | Kabul kanıtı |
|---|---|---|---|---|---|---|---|
| DEC-RPT-001 | Raporlama | R-06 Rapor Taslaklarım kullanıcıya özel isimli, sürümlü, kopyalanabilir, güncellenebilir, arşivlenebilir ve yeniden çalıştırılabilir semantik rapor tanımıdır. | KILITLI | K1 | Kullanıcı kararı 2026-08-27 | Sinan Göymen | Taslak veri sonucu/snapshot saklamaz. |
| DEC-RPT-002 | Raporlama | Rapor taslağı her çalıştırmada güncel veri, güncel CompanyContext ve güncel RBAC/ABAC yetkisiyle değerlendirilir; eski taslak eski yetkiyi korumaz. | KILITLI | K0 | Kullanıcı kararı 2026-08-27 | Sinan Göymen | Paylaşım tanıma erişimdir; satır verisi yetkisi ayrıca çözülür. |
| DEC-RPT-003 | Raporlama | Paylaşım, zamanlama, XLSX/PDF üretimi ve yeniden çalıştırma ayrı izin ve audit olaylarıdır; zamanlanmış çalıştırma geri alınmış yetkiyi by-pass edemez. | KILITLI | K1 | Kullanıcı kararı 2026-08-27 | Sinan Göymen | ReportRun ve ReportArtifact izlenir. |
| DEC-RPT-004 | Raporlama | R-07 AI ile Rapor Oluşturucu ham SQL üretmez; yalnız doğrulanmış alan, ölçü, filtre, gruplama, grafik ve çıktı tanımından oluşan şemalı semantik rapor tanımı üretir. | KILITLI | K0 | Kullanıcı kararı 2026-08-27 | Sinan Göymen | Sorgu deterministik semantic query compiler tarafından üretilir. |
| DEC-AI-009 | AI | OpenAI entegrasyonunda ana düşük seviye yol Responses API + Structured Outputs + strict şemalı function tools; çok adımlı orkestrasyon gerektiğinde Agents SDK kullanılabilir. | KILITLI | K1 | Kullanıcı kararı + güncel OpenAI resmi dokümantasyonu 2026-08-27 | Sinan Göymen | API/model sürümü konfigürasyondur; tool şeması sürümlenir. |
| DEC-AI-010 | AI | ChatKit yalnız isteğe bağlı arayüz hızlandırıcısıdır; ERP iş kuralları ve yetki backendde kalır. Agent Builder kullanılmaz. | KILITLI | K1 | Kullanıcı kararı + OpenAI Agent Builder sunset duyurusu 2026-08-27 | Sinan Göymen | Agent Builder bağımlılığı oluşturulmaz. |
| DEC-RPT-005 | Raporlama | Rapor tanımı, sürüm, çalıştırma, paylaşım, zamanlama ve dışa aktarım kullanıcı/şirket/kapsam/korelasyon bilgileriyle audit edilir. | KILITLI | K1 | Kullanıcı kararı 2026-08-27 | Sinan Göymen | Üretilen artifact veri kesim zamanı ve ReportVersion taşır. |
