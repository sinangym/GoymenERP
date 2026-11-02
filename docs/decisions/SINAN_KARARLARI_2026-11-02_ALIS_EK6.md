# Sinan Kararları — 2026-11-02 Alış EK6 (geri çekme yetkisi)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda, A1-1b (talebin geri çekilmesi, PR #907)
uygulanırken sorulan seçenekli soruya (AskUserQuestion) verdiği **doğrudan cevap**. Claude yalnız soruyu ve
seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

`SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle) geçerlidir. Bu karar bir rol veya grant
tanımlamaz; izin adı (`purchasing.requests.withdraw`) sahip modülde konur, gerçek kullanıcılara atanması insan
kapısındadır.

## 1. Kararlar

| No | Konu | Soru | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|---|
| ALS-D47 | Geri çekme yetkisi: talebi oluşturan veya onaya gönderen geri çekebilir; başka kimse geri çekemez | "Satın alma talebini 'geri çek' kim yapabilsin?" | **"Oluşturan veya gönderen"** | EK5 §2 notu "oluşturan/gönderen" diyordu; PR #907'nin ilk hâli en dar okumayla yalnız gönderene izin veriyordu | ALS-D46, ALS-D2, No.161, plan API-01b |

## 2. Notlar (geri alınabilir)

- Geri çekme yalnız **Gönderildi** durumundaki talepte, talebi **oluşturan** (hazırlayan) **veya** onaya **gönderen**
  kişi tarafından yapılır; ikisi farklı kişiyse ikisi de geri çekebilir; başka kimse geri çekemez. Gerekçe zorunlu,
  audit'e yazılır (ALS-D46 notları aynen geçerlidir).
- Karşılaştırma kayıtlı kimliklerle yapılır: kayıtlı olmayan bir kimlik (A1-1 öncesi gönderilmiş talebin gönderen
  kaydı gibi) eşleşmez; o durumda yalnız kayıtlı olan kişi geri çekebilir; ikisi de kayıtlı değilse kimse geri
  çekemez (fail-closed).
- Uygulama: PR #907 (A1-1b).
