# Göymen ERP konuşma bilgisi — codex-goymen-erp-cari-contract-activation-reconciliation-c955c55c215b

Bu paket, bağlı konuşmanın görünür kesitindeki Göymen ERP ilerleme değerlendirmelerini, Cari C-02 screen kimliği uzlaştırmasını ve C-01 normal-runtime owner-source/activation sınırlarını normatif olmayan aday bilgi olarak taşır. Kanonik kararları, Master v3'ü, backlog durumunu veya uygulama davranışını değiştirmez.

## Ana sonuçlar

- C-02'nin shell-facing canonical screen kimliği C-02'dir; create-subset ayrı scopeVariant metadata'sıdır. C-02A yalnız business/UX alt-akış etiketidir.
- Exact create tuple cari.create + C-02 + create-subset + /cari/yeni + cari.parties.create biçimindedir; C-02:create-subset route/screen literal'i yasaktır.
- PR #177 owner-source düzeltmesini, PR #179 ise ayrı Codex-owned executable guard'ı main'e taşımıştır.
- PR #175'in company-link capability'si fail-closed staged durumdadır; yeni rol/grant veya runtime aktivasyonu değildir.
- PR #183 disposable gerçek-browser/SQL davranış kanıtıdır; normal provider activation kanıtı değildir.
- C-01A için hedef owner-source SingleCompany + ImplementationAvailable=true; C-01B group/multi-company renderer kapalıdır. Global production cutover no_go kalır.
- Canlı main'deki normal Counterparty provider list/create/link/detail capability'lerinin tamamını ImplementationAvailable=false yayımlar.
- PR #184 açık Draft'tır ve normal-runtime kanıt kapısını tanımlar; bu paket PR'ı merge-ready veya activation GO olarak sınıflandırmaz.

## Canlı repository doğrulaması

Repository tabanı main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97 olarak doğrulandı. PR #175, #177, #179 ve #183'ün merge durumları ile normal provider'ın dört kapalı declaration'ı bu SHA üzerinde kontrol edildi. PR #184'ün canlı head/base/Draft durumu 4 Eylül 2026 ölçümüdür ve sonraki hareketlerde yeniden çözülmelidir.

## Kapsam sınırı

Bağlı konuşma kesiti 58. mesajdan önce kırpılmıştır; tam platform konuşma kimliği ve mesaj zamanları verilmemiştir. Diğer Codex, Claude ve ChatGPT konuşmaları bu oturumdan erişilebilir değildir. Bu nedenle paket visible_only ve partial'dır; aynı çalıştırmanın diğer kaynak paketlerinin yerine geçmez.
