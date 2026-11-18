# W8 runtime constructor şekli kapanışı

## Sınır ve kaynak

- Başlangıç: `73ba4a3128747dea3ca936e568419dfe58c902fa`. İlk mimari başarısızlık `3e85cbbb1` kaynak koşusundaki `Platform_non_counterparty_bypass_surfaces_match_the_reviewed_inventory` sonucudur. Sonlu kapsam: bu sonuçta yer alan 142 runtime dosyasındaki 772 konum; yeni bir işletme dilimi değildir.
- Tarayıcının context tipi geçen bir dosyada `new(...)` ifadelerini ihtiyatlı biçimde aynı context'e bağlaması, gerçek DTO/exception/serializer oluşturmalarını da işaretlemektedir. Düzeltme yalnız gerçek hedef tipin açık yazılmasıdır. Guard/allowlist, SQL, EF/model, izin, sözleşme şekli ve kurallar değişmez.

## İlk kaynak checkpoint'i

- Ayrıntılı atanan 31 dosya/185 konum kümesinde 22 dosyadaki 180 gerçek DTO/yardımcı nesne ifadesinin tipi açık yazıldı. Dört açık gerçek DbContext oluşturması ve `DocumentBrandingService.NewContext` içindeki gerçek target-typed context oluşturması aynen korundu.
- Ek olarak `FinanceCreditAccountService` içindeki dört yeni DTO ve `FounderRecoveryService` içindeki bir yeni Audit DTO ifadesi açık yazıldı. W5 kaynak commit'i `24fd735d753ffae0c69f774785f71c7c6125c30d` ile karşılaştırılarak bu iki dosyanın eski dörder ifadesine dokunulmadı. Diğer üç W5 pending-context dosyası değiştirilmedi; beş kaydın eski sayıları ve K3 bekleyen durumu korunur.
- Toplam 24 runtime dosyası, 185 tip adı eklemesi. Depo dışı kesin ekleme listesi geri çıkarıldığında başlangıç kaynak metni birebir elde edilir; argüman, string, akış veya gerçek context oluşturması değişmedi. Adlar gerçek dönüş/parametre/row/record bildirimlerinden doğrulandı; bilinmeyen hedef tipe tahmin uygulanmadı.
- `git diff --check`: exit0. Kaynak derlemesi ve guard koşumu **QUEUED**; ağır süreç lease'i koordinatördedir. Bu checkpoint henüz guard PASS veya bütün runtime kapanışı değildir.
- Kalan ilk-hata konumları aynı sonlu kapsam içinde ele alınacaktır. `InventoryTransferReceiptDocuments.cs` diğer sahibin dar sabit adı düzeltmesi bitene kadar bu şeritte düzenlenmez.

## SDK anlamsal tür doğrulaması

- İlk kaynak checkpoint’i: `3d71fba719fabbfe75d4ac7ff99586bb46740525`. Kalan ilk-hata dosyaları, depoda zaten kullanılan SDK/Roslyn ve yerel proje referanslarıyla incelendi; yeni paket veya ağ kaynağı kullanılmadı.
- İncelenmesine izin verilen 136 dosyada kalan 535 target-typed constructor ifadesinin tamamı gerçek hedef tür ve constructor sembolüne çözüldü; bilinmeyen/hatalı tür sayısı 0. Bir gerçek `DocumentsDbContext` oluşturması değişmedi. Diğer 534 ifadeye 111 runtime dosyasında yalnız çözülen tür adı eklendi.
- Bu checkpoint ve önceki checkpoint birlikte 135 runtime dosyasında 719 tür adı eklemesidir. Her eklemenin kaldırılması, düzenleme öncesi dosyanın BOM/satır sonları dahil baytlarını birebir geri verir. Constructor argümanları, stringler, kontrol akışı ve mevcut gerçek context oluşturma ifadeleri korunmuştur.
- W5’in beş pending-context dosyasının eski siteleri değişmedi. Diğer yazarlara ayrılan `InventoryTransferReceiptDocuments.cs`, `SalesStore.OfficialCancellations.cs`, `PurchasingStore.IncomingInvoices.cs` ve `PurchasingStore.InvoiceImports.cs` bu anlamsal düzenlemenin dışında bırakıldı.
- SDK yardımcı derlemesinin ilk denemesi yalnız depo dışındaki response-file `-out` bağımsız değişkeni nedeniyle exit1 verdi; ilk log korundu. Düzeltilmiş yardımcı derlemesi ve anlamsal çözümleme exit0. Bu sonuç uygulama derlemesi veya mimari test PASS değildir.
- Ekleme/geri çıkarma bayt doğrulaması ve `git diff --check`: exit0. API/Integration kaynak derlemesi ve mimari guardın birleşik kaynakta koşumu **QUEUED**. Mevcut davranış testleri değiştirilmedi; DTO adını tekrar eden yapay test eklenmedi.
