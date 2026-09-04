# Conflict Candidates

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: chatgpt-erp-analizi-plani-45d59712c9aa
Unresolved-Conflict-Count: 0

## Çözümlenmiş / superseded karşıtlıklar

### StokERP API kalıcı bağımlılığı

23 Ağustos tarihli kullanıcı-yüklemeli Master v3 ekinde hedef uygulamanın StokERP ile API üzerinden çalışacağı yazılıdır. Bu konuşmanın sonraki açık kullanıcı kararı, StokERP'nin ana proje olmayacağını ve yalnız bütün yararlı fikir/formül bilgisinin Göymen ERP'ye aktarılacağını söyler. Canonical `KARAR_SICILI.md` içindeki `DEC-GOV-001` de aynı sonraki kararı kilitli olarak taşır. Bu nedenle eski API-bağımlılığı kaydı `SUPERSEDED`, yeni karar `EXPLICIT_USER_DECISION` olarak normalize edilmiştir; çözümsüz conflict yoktur.

### Legacy müsait stok hesabı

StokERP mevcut sisteminde `Müsait = Kalan - Manuel Rezerv` davranışı doğrulanmış current-state bilgisidir ve üretim rezervini ayrıca raporlar. Donör analizindeki genişletilmiş serbest stok formülü hedef önerisidir. Bunlar CURRENT ve TARGET olarak ayrıldığı için conflict olarak sayılmamıştır.

### Sabit vardiya hesabı

StokERP kuyruk hesabındaki `08:00 + 480 dakika + tek vardiya` davranışı CURRENT kaydıdır. Çoklu vardiya/takvim/setup destekli üretim kontrol merkezi TARGET önerisidir; conflict değildir.
