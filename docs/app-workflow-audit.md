# Audit dan Dokumentasi Flow Kerja Aplikasi HPP Ultimate

## Tujuan dokumen

Dokumen ini menjelaskan:

1. Alur kerja aplikasi berdasarkan implementasi codebase saat ini.
2. Keterkaitan antar modul dan data yang dipakai setiap langkah.
3. Temuan audit arsitektur dan operasional yang penting untuk diketahui sebelum aplikasi dipakai lebih luas.

Scope audit ini mengacu ke implementasi di:

- `Hpp_Ultimate/Hpp_Ultimate/Program.cs`
- `Hpp_Ultimate/Hpp_Ultimate/Services/*`
- `Hpp_Ultimate/Hpp_Ultimate/Components/Pages/*`
- `Hpp_Ultimate/Hpp_Ultimate/Components/Layout/*`

## Ringkasan singkat

HPP Ultimate sekarang sudah mencakup alur inti berikut:

1. Login dan kontrol akses admin/staff.
2. Pengaturan bisnis dan manajemen akun.
3. Master bahan baku.
4. Ledger stok bahan baku.
5. Master resep dan kalkulasi HPP.
6. Master produk jadi dan mapping resep-ke-produk.
7. Batch produksi yang mengonsumsi bahan baku.
8. POS dengan checkout persisten.
9. Histori penjualan, void transaksi, dan laporan.
10. Backup, restore, export, dan import data.

Secara fungsi, aplikasi sudah bisa dipakai untuk workflow operasional dasar:

`login -> setup bisnis -> kelola bahan -> susun resep -> link ke produk jadi -> produksi -> jual di POS -> review penjualan/laporan -> backup data`

## Arsitektur kerja saat ini

### 1. Penyimpanan data

Pusat data aplikasi ada di `SeededBusinessDataStore`.

Karakteristik utamanya:

- Disimpan ke file SQLite lokal: `App_Data/hpp-ultimate.db`
- Diregistrasikan sebagai singleton di `Program.cs`
- Menyimpan banyak koleksi state di memory lalu dipersist sebagai JSON ke tabel `AppState`
- Menjadi source of truth untuk:
  - produk
  - bahan baku
  - histori harga
  - resep
  - ledger stok
  - BOM
  - batch produksi
  - transaksi penjualan
  - user
  - audit trail
  - business settings
  - auth session

Implikasinya:

- State aplikasi sangat terpusat.
- Service layer bekerja seperti domain service di atas satu in-memory state store.
- UI refresh lewat event `Store.Changed`.

### 2. Pola akses

Pola umum tiap modul:

1. Page Blazor memanggil service.
2. Service memvalidasi session/role lewat `WorkspaceAccessService`.
3. Service membaca atau mengubah `SeededBusinessDataStore`.
4. Store mempersist perubahan ke database.
5. Audit trail dicatat bila aksi bersifat mutasi.

### 3. Otorisasi

Model role saat ini:

- `Admin`
- `Staff`

Perilaku akses:

- Hampir semua modul operasional membutuhkan user yang sudah login.
- Pengaturan bisnis, user management, backup/restore, dan data ops dibatasi untuk admin.
- Guard halaman dilakukan di layout, bukan lewat middleware auth/cookie tradisional.

## Peta modul dan fungsi

### `/login`

Fungsi:

- Login dengan email atau username.
- Membentuk session aktif.
- Update `LastLoginAt`.

Service utama:

- `AuthService`

Data yang berubah:

- `Users`
- `AuthSession`
- `AuditLogEntries`

### `/pengaturan`

Fungsi:

- Atur identitas usaha.
- Atur mata uang, satuan default, pembulatan, dan pajak.
- Staff hanya read-only.

Service utama:

- `SettingsService`

Data yang berubah:

- `BusinessSettings`

### `/pengaturan/akun`

Fungsi:

- Edit profil user aktif.
- Ubah password sendiri.
- Logout.
- Admin bisa membuat, mengubah, dan menonaktifkan user lain.

Service utama:

- `AuthService`

Data yang berubah:

- `Users`
- `AuthSession`
- `AuditLogEntries`

### `/produk`

Fungsi:

- Kelola master bahan baku.
- Simpan netto, unit, harga pack, konversi unit, brand, dan status.
- Import/export material.
- Menjaga histori harga material saat harga berubah.

Service utama:

- `RawMaterialCatalogService`

Data yang berubah:

- `RawMaterials`
- `MaterialPrices`
- `AuditLogEntries`

### `/gudang`

Fungsi:

- Menjaga ledger stok bahan baku.
- Catat `OpeningBalance`, `StockIn`, `StockOut`, dan `Adjustment`.
- Atur minimum stock.

Service utama:

- `WarehouseService`

Data yang berubah:

- `StockMovements`
- `RawMaterials.MinimumStock`
- `AuditLogEntries`

### `/resep`

Fungsi:

- Menyusun formula bahan.
- Mengelompokkan material per group.
- Menambahkan biaya non-material seperti overhead/produksi.
- Menjaga resep aktif atau draft.

Service utama:

- `RecipeCatalogService`

Data yang berubah:

- `Recipes`
- `AuditLogEntries`

### `/hpp-calculator`

Fungsi:

- Simulasi HPP berdasarkan resep aktif.
- Menguji output aktual.
- Menghitung dampak margin, pembulatan, dan pajak.

Service utama:

- `HppCalculatorService`

Data yang dibaca:

- `Recipes`
- `RawMaterials`
- `BusinessSettings`

### `/produk-jadi`

Fungsi:

- Menjaga master barang jual.
- Menentukan harga jual default.
- Menghubungkan satu produk jadi ke satu resep aktif.
- Sinkronisasi BOM per unit dari resep ke produk.

Service utama:

- `ProductCatalogService`

Data yang berubah:

- `Products`
- `ProductRecipes`
- `BomItems`
- `AuditLogEntries`

### `/produksi`

Fungsi:

- Menyusun draft batch produksi.
- Memvalidasi kecukupan stok bahan.
- Menulis batch produksi aktual.
- Mengurangi stok bahan lewat `ProductionUsage`.
- Menyimpan biaya labor dan overhead per batch.

Service utama:

- `ProductionService`

Data yang berubah:

- `ProductionBatches`
- `StockMovements`
- `LaborCosts`
- `OverheadCosts`
- `AuditLogEntries`

### `/pos`

Fungsi:

- Menampilkan hanya produk jadi yang siap dijual.
- Membentuk cart transaksi.
- Checkout transaksi persisten.
- Menampilkan struk transaksi terbaru.

Service utama:

- `SalesService`

Data yang berubah:

- `Sales`
- `SaleLines`
- `AuditLogEntries`

Catatan stok:

- Stok produk jadi tidak ditulis ke ledger terpisah.
- On-hand produk jadi dihitung dari:
  - total produksi
  - dikurangi total penjualan completed

### `/penjualan`

Fungsi:

- Histori transaksi.
- Filter tanggal, status, dan pencarian.
- Preview struk.
- Void transaksi.

Service utama:

- `SalesService`

Data yang berubah:

- `Sales.Status`
- `Sales.VoidReason`
- `Sales.VoidedAt`
- `AuditLogEntries`

### `/laporan`

Fungsi:

- Ringkasan penjualan periodik.
- Daily sales.
- Top products.
- Margin by product.
- Material usage dari batch produksi.
- Price trends material.

Service utama:

- `ReportingService`

Data yang dibaca:

- `Sales`
- `SaleLines`
- `ProductionBatches`
- `StockMovements`
- `MaterialPrices`

### `/backup-data`

Fungsi:

- Backup penuh state aplikasi.
- Restore backup.
- Export resep.
- Export transaksi JSON/CSV.
- Import resep dan transaksi.

Service utama:

- `DataOpsService`

Data yang berubah:

- Semua domain state saat restore/import
- `AuditLogEntries`
- `AuthSession` dapat dikosongkan setelah restore

## Flow kerja operasional yang direkomendasikan

### Flow A: Inisialisasi awal sistem

1. Login sebagai admin.
2. Buka `Pengaturan`.
3. Isi identitas usaha, mata uang, pembulatan, pajak, dan satuan default.
4. Buka `Pengaturan Akun`.
5. Buat user tambahan bila ada admin/staff lain.
6. Lakukan backup awal setelah konfigurasi dasar selesai.

### Flow B: Menyiapkan master data

1. Input semua bahan baku di `Katalog Material`.
2. Pastikan netto, unit dasar, dan harga pack benar.
3. Bila data banyak, gunakan import.
4. Masuk ke `Gudang`.
5. Catat saldo awal dan minimum stock bahan.
6. Masuk ke `Resep`.
7. Buat formula resep lengkap dengan bahan dan biaya tambahan.
8. Gunakan `HPP Calculator` untuk mengecek logika biaya.
9. Buka `Produk Jadi`.
10. Buat master produk jual.
11. Hubungkan produk ke resep aktif agar BOM per unit tersinkron.

### Flow C: Menjalankan produksi

1. Pastikan stok bahan tersedia di `Gudang`.
2. Masuk ke `Produksi`.
3. Pilih produk jadi yang sudah linked ke resep.
4. Periksa validasi kekurangan bahan.
5. Tentukan kuantitas batch.
6. Masukkan biaya labor dan overhead bila ada.
7. Simpan batch produksi.

Output flow ini:

- batch produksi tercatat
- stok bahan berkurang
- biaya batch tersimpan
- stok produk jadi bertambah secara komputasional

### Flow D: Menjalankan penjualan

1. Masuk ke `POS`.
2. Pilih produk jadi yang tersedia.
3. Tentukan qty dan harga jual.
4. Tambahkan ke keranjang.
5. Pilih metode bayar.
6. Checkout.

Output flow ini:

- transaksi penjualan tersimpan
- struk bisa dipreview
- stok produk jadi turun secara komputasional

### Flow E: Pasca penjualan

1. Buka `Penjualan` untuk review transaksi.
2. Void transaksi bila diperlukan.
3. Buka `Laporan` untuk melihat performa periodik.
4. Jalankan backup berkala dari `Backup & Data Ops`.

## Ketergantungan antar modul

Urutan dependensi inti:

1. `Pengaturan` memengaruhi pembulatan dan pajak.
2. `Katalog Material` menjadi basis untuk `Resep`.
3. `Gudang` menjadi basis kesiapan bahan untuk `Produksi`.
4. `Resep` menjadi basis untuk `HPP Calculator` dan `Produk Jadi`.
5. `Produk Jadi` membutuhkan resep aktif untuk sinkronisasi BOM.
6. `Produksi` membutuhkan:
   - produk jadi
   - resep aktif
   - BOM
   - stok bahan
7. `POS` membutuhkan:
   - produk jadi aktif
   - resep aktif
   - hasil produksi
8. `Penjualan` dan `Laporan` bergantung pada transaksi POS.
9. `Backup & Data Ops` dapat menyalin atau mengganti seluruh state di atas.

## Temuan audit utama

### 1. Kritis: session login bersifat global untuk seluruh aplikasi

Temuan:

- `SeededBusinessDataStore` diregistrasikan sebagai singleton.
- `AuthSession` disimpan sebagai satu field global di store.
- Layout dan service membaca session dari store global tersebut.

Implikasi:

- Aplikasi saat ini secara efektif hanya punya satu session aktif untuk semua browser/user.
- Jika user A login lalu user B login dari browser lain, session bisa saling menimpa.
- Ini bukan model multi-user yang aman untuk deployment bersama.

Dampak ke flow:

- Flow kerja saat ini aman hanya untuk penggunaan single-operator atau demo lokal.
- Untuk lingkungan multi-user nyata, auth harus dipindah ke session/cookie/identity per user.

### 2. High: state aplikasi dipersist sebagai JSON blob per domain di satu tabel

Temuan:

- Data tidak dimodelkan sebagai tabel relasional normal untuk tiap entitas.
- Semua domain utama diserialisasi ke tabel `AppState`.

Implikasi:

- Sederhana untuk development lokal.
- Lebih sulit untuk scaling, concurrent edits, query analitik, dan recovery parsial.
- Audit dan integritas data lintas domain bergantung pada disiplin service layer.

### 3. Medium: stok produk jadi belum berupa ledger eksplisit

Temuan:

- On-hand produk jadi dihitung dari produksi dikurangi penjualan completed.
- Tidak ada `finished goods stock movement ledger` yang eksplisit seperti bahan baku.

Implikasi:

- Mudah untuk perhitungan dasar.
- Lebih sulit untuk audit stok jadi bila nanti ada retur, adjustment, spoilage, atau transfer.

### 4. Medium: workflow pembelian belum ada

Temuan:

- Gudang saat ini menerima stok lewat input manual `StockIn`.
- Belum ada modul supplier, PO, receiving, atau costing dari pembelian aktual.

Implikasi:

- Operasional dasar bisa jalan.
- Namun proses replenishment belum punya jejak pembelian end-to-end.

### 5. Medium: restore/import adalah operasi sangat kuat

Temuan:

- Restore backup bisa mengganti seluruh state.
- Restore juga dapat menutup session aktif lama.

Implikasi:

- Benar untuk recovery.
- Tetapi secara SOP harus dibatasi kuat ke admin dan sebaiknya dipakai dengan prosedur backup yang disiplin.

### 6. Medium: clear operational data membersihkan lebih luas dari ringkasannya

Temuan:

- Operasi clear data membersihkan produk, material, resep, stok, produksi, dan juga penjualan.
- Ringkasan hasil clear saat ini tidak menonjolkan jumlah sales yang terhapus.

Implikasi:

- User admin bisa salah mengestimasi cakupan data yang benar-benar dibersihkan.

## SOP penggunaan yang disarankan

### Untuk admin

1. Atur business settings lebih dulu.
2. Buat user staff setelah pengaturan dasar stabil.
3. Jaga master material dan minimum stock tetap bersih.
4. Backup sebelum import besar atau restore.
5. Hindari restore saat operator lain sedang aktif.

### Untuk staff operasional

1. Jangan langsung mengubah master jika isu hanya di stok.
2. Gunakan `Gudang` untuk koreksi kuantitas bahan.
3. Gunakan `Produksi` untuk setiap batch nyata.
4. Gunakan `POS` hanya untuk produk yang memang sudah diproduksi.
5. Gunakan `Penjualan` untuk review dan void, bukan edit data transaksi manual.

## Flow data singkat per transaksi nyata

Contoh alur satu produk sampai terjual:

1. Admin menambahkan material `Cabai`.
2. Gudang mencatat saldo awal `Cabai`.
3. User membuat resep `Sambal`.
4. Admin membuat produk jadi `Sambal Botol`.
5. Produk `Sambal Botol` di-link ke resep `Sambal`.
6. Operator mencatat batch produksi 20 botol.
7. Sistem mengurangi stok bahan sesuai BOM batch.
8. POS menjual 3 botol.
9. Sistem menyimpan transaksi penjualan.
10. On-hand produk jadi dihitung menjadi `20 - 3`.
11. Laporan menampilkan revenue, profit, dan penggunaan bahan.

## Kesimpulan audit

Secara fungsional, aplikasi sudah membentuk satu workflow bisnis yang utuh dari bahan baku sampai penjualan dan backup.

Namun secara arsitektur, ada satu batas utama yang harus dianggap serius:

- aplikasi belum aman untuk skenario multi-user bersamaan karena session aktif masih global di singleton store

Jika target pemakaian masih desktop lokal atau operator tunggal, flow saat ini sudah layak dipakai.

Jika target berikutnya adalah deployment bersama banyak user, prioritas teknis berikutnya harus:

1. memindahkan auth/session ke model per-user
2. mempertimbangkan persistence yang lebih relasional
3. menambahkan ledger stok produk jadi yang eksplisit
4. menambah workflow purchasing bila operasional gudang ingin lengkap
