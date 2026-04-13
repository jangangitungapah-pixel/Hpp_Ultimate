# Deploy HPP Ultimate ke Koyeb + Postgres

Project ini sekarang mendukung dua mode storage:

- `SQLite` untuk lokal/dev
- `Postgres` untuk deploy

Prioritas konfigurasi database:

1. `ConnectionStrings__Postgres`
2. `DATABASE_URL`
3. fallback ke `App_Data/hpp-ultimate.db`

## 1. Siapkan database Postgres di Koyeb

Buat database Postgres di Koyeb dan salin connection string-nya.

App menerima dua format:

- Npgsql connection string biasa:

```text
Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require
```

- URL style:

```text
postgresql://user:password@host:5432/database?sslmode=require
```

## 2. Migrasikan data lokal SQLite ke Postgres

Jika kamu sudah punya data operasional lokal di file:

`Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/App_Data/hpp-ultimate.db`

jalankan app sekali dari mesin lokal sambil mengarah ke Postgres Koyeb.

PowerShell:

```powershell
$env:ConnectionStrings__Postgres="Host=YOUR_HOST;Port=5432;Database=YOUR_DB;Username=YOUR_USER;Password=YOUR_PASSWORD;SSL Mode=Require"
dotnet run --project .\Hpp_Ultimate\Hpp_Ultimate\Hpp_Ultimate\Hpp_Ultimate.csproj
```

Perilaku migrasinya:

- jika Postgres masih kosong dan file SQLite lokal ada, app akan mengimpor state lama secara otomatis
- jika Postgres sudah berisi data, import tidak dijalankan
- setelah import selesai, app akan membaca dan menulis langsung ke Postgres

## 3. Push repo ke GitHub

Koyeb akan build dari root repo menggunakan `Dockerfile` yang sudah ada.

## 4. Buat Web Service di Koyeb

Di Koyeb:

1. pilih `Create Web Service`
2. hubungkan repository GitHub ini
3. gunakan `Dockerfile` dari root repo
4. set environment variable:

```text
ConnectionStrings__Postgres=Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require
```

Opsional:

```text
ASPNETCORE_ENVIRONMENT=Production
```

Tidak perlu set `PORT` manual kalau Koyeb sudah memberikannya, karena container ini sudah membaca nilai `PORT` otomatis saat start.

## 5. Redeploy

Setelah variabel environment tersimpan, deploy service. Perubahan berikutnya cukup `git push`, lalu Koyeb akan rebuild dari repo.

## Catatan penting

- image deploy tidak membawa file `App_Data` lokal karena sudah dikecualikan di `.dockerignore`
- source of truth produksi nanti ada di Postgres, bukan SQLite
- untuk backup manual, tetap gunakan fitur backup JSON dari aplikasi

## Referensi resmi

- Koyeb build from Git: https://www.koyeb.com/docs/build-and-deploy/build-from-git
- GitHub Pages hanya untuk static site: https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages
