# Big-Bang Cutover Runbook

1. Son tam prova sonuçlarını ve rollback süresini onayla.
2. Yeni kayıt giriş saatini ve bakım penceresini ilan et.
3. Eski sistemleri yazmaya kapat; tam DB/dosya yedeği al.
4. Final delta çıkar, checksum ve batch manifest üret.
5. Yeni ERP’ye yükle; sayı, kg, adet, tutar, açık işlem ve belge hash mutabakatı yap.
6. CompanyId, yetki, sipariş, stok, tahsis, üretim, finans ve belge smoke testlerini çalıştır.
7. Production’ı aç; ilk işlemleri audit/korelasyonla izle.
8. Kritik fark varsa rollback kararını önceden belirlenmiş zaman sınırında uygula.
9. Başarı halinde eski uygulamayı salt okunur/arşiv durumuna al.
10. Cutover raporu ve nihai SoT tarihini kaydet.
