# C006 geçici Cari A/B ortamı

Yalnız `CARI-AB-20260905-006-009`, run `c006-0695c15ec110`, instance
`8aff5b7a-e33f-48fc-aea3-4f8c6a17c4bb` için yerel kabul yardımcısıdır.
Normal API projesi bu hostu referans almaz. Backend/domain/permission/migration
kaynakları değiştirilmez. Gerçek API ve mevcut migration/RBAC seed zinciri kullanılır.

Hazırlık sonucunun tek hazır olma kaydı:
`C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-orchestration\evidence\C-DESKTOP-006\environment.json`.
Dosya yoksa veya `readyForBrowser` true değilse browser yazma yolculuğu başlamaz.
Manifestin SHA, PID, başlangıç zamanı ve kaynak kimlikleri kontrol edilmelidir.

API `http://127.0.0.1:15080`, UI için izin verilen origin
`http://127.0.0.1:15173` veya `http://localhost:15173`.
Testing ortamında mevcut DevelopmentBootstrap yöntemi kullanılır:
`X-Development-User: group-bootstrap-admin` ve tek `X-Company-Id`.
A: `10000000-0000-0000-0000-000000000001`,
B: `10000000-0000-0000-0000-000000000002`.
Bu sentetik bootstrap adı bir parola veya üretim kimliği değildir.
Gerçek RBAC kontrolleri korunur; company-a-user B'ye, company-a-viewer create'e yetkili değildir.

Smoke kaydı `C006-SMOKE-0695c15ec110` önekini kullanır. Claude'un yolculuk
kaydının LegalName değeri `F009-0695c15ec110` ile başlamalıdır.
Baseline, smoke sonunda bir Party, iki A/B account ve birer create/link audit/command içerir.
Ana browser yolculuğundan hemen sonra (başka başarılı create/link senaryolarından önce)
tek yeni Party için doğrulama yapılır. Yeni Party sayısı açıkça artırılabilir;
beklenmeyen kayıtlar veya baseline değişikliği başarısız sayılır.

PowerShell'de aynı kullanıcı altında, parola içermeyen komutlar:

```powershell
& 'C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-acceptance-backend-006\tools\cari-acceptance-006\control.ps1' -Action snapshot
& 'C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-acceptance-backend-006\tools\cari-acceptance-006\control.ps1' -Action verify -ExpectedNewParties 1
```

İki komut da yalnız bu yeni veritabanında sabit SELECT sorguları çalıştırır ve
JSON'u stdout'a verir; çıktı F009'un kendi evidence dizinine kaydedilebilir.
Parola setup tarafından yeni üretilir, yalnız bu kullanıcıya ait DPAPI dosyasında
saklanır ve yardımcı tarafından içeride tüketilir. Eski secret/env/token dosyası
kullanılmaz; parola argv veya manifestte bulunmaz.

Başlatma tek başına hazır olma değildir: `-Action start` alt süreci başlattıktan sonra
sürecin kendi `host-started.json` kaydını yazmasını sınırlı süre bekler, o kaydın aynı
süreci gösterdiğini ve API portunu o sürecin tuttuğunu doğrular. Doğrulanamayan bir
başlatma komutu sıfırdan farklı çıkışla biter; hiçbir şey durdurulmaz veya silinmez.

Çalışan hostu yalnız kendi kaydıyla durdurmak için (devam eden çalışma için
`-ResumeC012` ile aynı komut):

```powershell
& 'C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-acceptance-backend-006\tools\cari-acceptance-006\control.ps1' -Action stop
& 'C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-acceptance-backend-006\tools\cari-acceptance-006\control.ps1' -Action release-check
```

Stop yalnız kayıtlı PID'i, kayıtlı süreç adını ve kayıtlı başlangıç zamanını birlikte
doğrulayan süreci durdurur; ikinci çağrı durduracak bir şey bulmaz ve hata değildir;
PID yeniden kullanılmışsa durdurma reddedilir. `release-check` her iki kanıt klasöründeki
(C006 ve C012 devam) kayıtlı hostların durumunu bildirir.

Claude ve koordinatör işi bitirdikten sonra yalnız C006 kaynaklarını kaldırmak için
(önce `-Action stop`, gerekiyorsa `-ResumeC012` ile):

```powershell
& 'C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-acceptance-backend-006\tools\cari-acceptance-006\control.ps1' -Action teardown
```

Teardown kayıtlı container ID, volume/network label ve kayıtlı hostların durumunu
doğrular; kayıtlı bir host hâlâ çalışıyorsa hiçbir şey silmez ve kendisi süreç durdurmaz.
Başka kaynağa, Docker Desktop'a veya F009 UI sürecine dokunmaz.
Setup ve smoke yinelenebilir genel reset komutları değildir; mevcut kayıtları korur.
Bu ortam veya HTTP smoke tek başına ERP/browser/UX kabulü ya da fresh CI değildir.
