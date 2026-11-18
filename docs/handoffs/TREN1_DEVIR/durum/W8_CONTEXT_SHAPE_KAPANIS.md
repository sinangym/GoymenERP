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
