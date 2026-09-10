# GöymenERP — ortak geliştirme başlangıç paketi

10 Eylül 2026 • Tek ortak kaynak önerisi: **GitHub + yerel çalışma kopyası**

Durum: `PROPOSAL_NOT_EXECUTION_AUTHORITY`. Bu paket son kullanıcı kararlarını kayıt altına alır, kapsamı mevcut tek backlog'a uzlaştırılacak çalışma dilimlerine ayırır ve ilk teknik dilimi hazırlar. Tüm ERP'nin kodlandığı veya bütün şema/API/test kodlarının tamamlandığı anlamına gelmez. Karar/governance merge'i insan incelemesindedir.

## Önce ne okunacak?

1. Depo kökündeki `AGENTS.md` ve onun yönlendirdiği güncel governance.
2. [Son kullanıcı kararları](../../decisions/SINAN_KARARLARI_2026-09-10.md).
3. [Son rapor ve başlangıç planı](SON_RAPOR_VE_BASLANGIC_PLANI.md).
4. [Teslim envanteri](TESLIM_ENVANTERI.json) ve [karar–işlev–test eşlemesi](KARAR_ISLEV_TEST_ESLEMESI.json).
5. [İlk teknik dilim: cari rol uzlaştırması](ILK_DILIM_CARI_ROL_UZLASTIRMA.md).
6. [AI iş bölümü ve dört tam prompt](AI_CALISMA_PLANI_VE_PROMPTLAR.md).

Son açık kullanıcı kararı daha eski dokümanla çelişirse üstündür. Bu paket mevcut `task_manifest.json`, `DELIVERY_BACKLOG_v1.json`, `AUTONOMOUS_PROGRESS.md` ve `DECISION_PACK.md` yerine ikinci otomatik görev sistemi kurmaz. İlgili sahip, farkları mevcut kayıtlara uzlaştırır.

## Nasıl başlayacağız?

Önce koordinatör mevcut cari PR'ları ve bu karar paketini uzlaştırır. ChatGPT-BUSINESS bir dilim önden kabul/spec hazırlarken Codex backend'i, Claude aynı dilimin frontend'ini geliştirir. Ayrı worktree, belirli dosya/sözleşme sahipliği ve tek yazıcı kuralı kullanılır. Çalışmayan buton, sahte metrik veya sadece mock veriyle tamamlanmışlık gösterilmez.

Her role [prompt dosyasındaki](AI_CALISMA_PLANI_VE_PROMPTLAR.md) kendi tam bloğu verilir. Bu mesajların verilmesi sınırsız oturum, API erişimi, yeni merge yetkisi veya otomatik production onayı oluşturmaz. Kesintide son HEAD/görev/kanıt/sonraki adım kalıcı kayda yazılır; devamda bütün analiz baştan yapılmaz.

GitHub erişimi olan AI dosyaları doğrudan okur. Erişimi olmayan ChatGPT oturumuna bu klasör ve karar dosyası yüklenir; güncel branch/commit de bildirilir. Ajanlar birbirinin sohbetini veya yerel dosyasını kendiliğinden görüyor varsayılmaz. Kaynak kodu sohbetler arasında elle parçalar halinde taşımak ana çalışma biçimi değildir.

## Girdilerin zamanlaması

Altyapı/ayar/yetki/izlenebilirlik şimdi tasarlanır. Gerçek formül ve toleranslar ilgili hesaplama kabulünden; antet/kullanıcı/cihaz örnekleri ilgili kullanıcı/ortam kabulünden; canlı sırlar, restore hedefleri ve işletim onayı canlıya geçişten önce tamamlanır. Bütün bilgileri sona bırakmak doğru değildir; bütününü bugün toplamak da geliştirme ön koşulu değildir. Ayrıntı [rapor §4](SON_RAPOR_VE_BASLANGIC_PLANI.md).

## Doğrulama

Paket için yapısal doğrulama komutu:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/planning/2026-09-10/Validate-DeliveryPackage.ps1
```

Bu kontrol JSON kimlikleri, paket bağımlılık grafiği, karar–paket eşlemesi, mevcut dilim referansları ve yerel belge bağlantılarını doğrular. Ürün unit/SQL/browser/security/restore testlerini çalıştırmaz. Gerçek ürün kanıtları ilgili dilimde üretilir. Doğrulama sonucu ve sınırlar [teslim notundadır](DOGRULAMA_VE_DEVIR.md).

## Kaynak ve güncellik

Başlangıç `origin/main`: `9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`.
Önceki yerel v6.1 kaynağının dosya yolu/hash'i karar kaydındadır. Önceki raporlar değiştirilmedi.
Güncel koordinasyon: [GitHub Issue #66](https://github.com/sinangym/GoymenERP/issues/66).
Bu paket statik kapsam/şartname kaydıdır; değişen uygulama ilerlemesi mevcut ortak ilerleme sisteminde tutulur.

Gizli bilgi, gerçek parola veya müşteri veri dökümü bu pakete eklenmez. Kullanıcının istediği mevcut sistem mantığı referans alınır; bu teslim veri aktarımı değildir.
