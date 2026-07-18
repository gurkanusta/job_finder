# Junior İş İlanı Radar + Telegram Bildirim Botu

Farklı kaynaklardan **junior backend / junior yazılım geliştirici** ilanlarını tarar,
yeni bir ilan bulduğunda **Telegram üzerinden** bildirim gönderir.
**Otomatik başvuru / CV gönderme YOKTUR** — sadece bulur ve bildirir.

GitHub Actions'ta zamanlanmış (her 3 saatte bir) çalışır; sürekli açık bir sunucuya gerek yoktur.

---

## Kaynaklar

| Kaynak | `--source` adı | Yöntem | Not |
|---|---|---|---|
| Greenhouse | `greenhouse` | Resmi JSON API | En stabil |
| Lever | `lever` | Resmi JSON API | En stabil |
| LinkedIn | `linkedin` | Gmail'e gelen **Job Alert** mailini IMAP ile okuma | LinkedIn scrape edilmez |
| Telegram kanalları | `telegram` | Resmi Bot API `getUpdates` | Bot kanala üye olmalı |
| Kariyer.net | `kariyer` | HTML scrape (best-effort) | Kırılgan, bloklanabilir |
| Techcareer.net | `techcareer` | HTML scrape (best-effort) | Kırılgan |
| Coderspace | `coderspace` | HTML scrape (best-effort) | Kırılgan |

Her kaynak izole çalışır: biri patlarsa diğerleri çalışmaya devam eder.

---

## 1) Telegram bot kurulumu (BotFather)

1. Telegram'da **@BotFather**'a yaz → `/newbot`.
2. Bot adı ve kullanıcı adı ver. Sonunda sana bir **token** verir:
   `123456789:AAExxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx` → bu **`TELEGRAM_BOT_TOKEN`**.
3. **Chat ID'yi öğren** (bildirimin sana gelmesi için):
   - Bota Telegram'da `/start` yaz (herhangi bir mesaj gönder).
   - Tarayıcıda aç: `https://api.telegram.org/bot<TOKEN>/getUpdates`
   - Dönen JSON'da `"chat":{"id":123456789,...}` → bu sayı **`TELEGRAM_CHAT_ID`**.
   - (Kendine özel grup istiyorsan grubu kur, botu ekle, grupta bir mesaj yaz; grup id'si `-100...` ile başlar.)

### Telegram topluluk kanalı modülü için (opsiyonel)
- Botu ilgili **kanal/gruba ekle**.
  - **Kanal** ise: botu **admin** yap (bot ancak admin'se `channel_post` görebilir).
  - **Grup** ise: BotFather'da `/setprivacy` → **Disable** (yoksa bot grup mesajlarını göremez).
- İzlemek istediğin kanalları `config.json` → `telegramChannels` listesine `@kullaniciadi` olarak ekle
  (boş bırakırsan botun üye olduğu tüm sohbetler taranır).

---

## 2) Gmail App Password (LinkedIn mail-parse için)

LinkedIn'de native **Job Alert** kur (Entry level filtresi, İstanbul/Türkiye, günlük e-posta açık).
Bot bu mailleri IMAP ile okuyacak.

**Ön koşul:** Google hesabında **2 Adımlı Doğrulama (2-Step Verification) açık olmalı.**
Kapalıysa App Password seçeneği hiç görünmez.

1. https://myaccount.google.com/security → **2-Step Verification** açık mı kontrol et.
2. https://myaccount.google.com/apppasswords → yeni App Password oluştur (ör. isim: "jobfinder").
3. Google sana 16 haneli bir şifre verir (boşluklu gösterilir, boşlukları silebilirsin).
   - Gmail adresin → **`GMAIL_IMAP_USER`**
   - 16 haneli App Password → **`GMAIL_APP_PASSWORD`**
4. Gmail'de IMAP erişimi açık olmalı (Gmail → Ayarlar → Yönlendirme ve POP/IMAP → IMAP'i etkinleştir).

> Not: LinkedIn'in günlük alert maili genelde **birden fazla ilanı** tek mailde listeler.
> Parser mail body'sindeki **tüm** ilan linklerini çıkarır, sadece ilkini değil.

---

## 3) `config.json` nasıl doldurulur

```jsonc
{
  "matching": {
    // Başlıkta geçmesi gereken seviye ve rol kelimeleri (küçük harf yaz).
    "levelKeywords": ["junior", "jr", "yeni mezun", "entry level", "graduate", "stajyer"],
    "roleKeywords":  ["backend", "yazılım geliştirici", "software developer", ".net developer", "developer"],
    // Bunlardan biri başlıkta geçerse ilan ELENİR.
    "excludeKeywords": ["senior", "sr.", "lead", "kıdemli", "manager", "principal", "staff"],
    // true  = başlıkta HEM seviye HEM rol kelimesi olmalı (ör. "Junior Backend").
    // false = seviye VEYA rol kelimesi yeterli (daha çok sonuç, daha çok gürültü).
    "requireBothGroups": true
  },

  // İzlemek istediğin şirketler (Greenhouse/Lever kullananlar).
  "companies": [
    { "name": "Trendyol", "ats": "greenhouse", "token": "trendyol" },
    { "name": "BirŞirket", "ats": "lever", "token": "birsirket" }
  ],

  // Scrape edilecek arama sonucu sayfaları.
  "scrapeTargets": [
    { "site": "Kariyer.net", "url": "https://www.kariyer.net/is-ilanlari/junior+backend" }
  ],

  // Taranacak Telegram kanalları (bot üye/admin olmalı). Boş = tüm sohbetler.
  "telegramChannels": ["@ornek_kanal"],

  "linkedin": {
    "imapHost": "imap.gmail.com",
    "imapPort": 993,
    "lookbackDays": 2,
    "fromContains": "linkedin.com"
  },

  "seenRetentionDays": 60
}
```

### Şirketin board token'ını bulma
- **Greenhouse:** Kariyer sayfası `job-boards.greenhouse.io/SIRKET` veya `boards.greenhouse.io/SIRKET`
  formatındaysa `token` = `SIRKET`. API testi:
  `https://boards-api.greenhouse.io/v1/boards/SIRKET/jobs`
- **Lever:** Kariyer sayfası `jobs.lever.co/SIRKET` formatındaysa `token` = `SIRKET`. API testi:
  `https://api.lever.co/v0/postings/SIRKET?mode=json`

Bu iki URL tarayıcıda JSON dönüyorsa token doğrudur.

---

## 4) GitHub Secrets

Repo → **Settings → Secrets and variables → Actions → New repository secret** ile ekle:

| Secret adı | Değer |
|---|---|
| `TELEGRAM_BOT_TOKEN` | BotFather token'ı |
| `TELEGRAM_CHAT_ID` | Senin chat/grup id'in |
| `GMAIL_IMAP_USER` | Gmail adresin |
| `GMAIL_APP_PASSWORD` | 16 haneli App Password |

> **Token'lar asla `config.json`'a veya koda yazılmaz.** Sadece Secrets → environment variable.

Workflow zaten `permissions: contents: write` ile tanımlı; `seen-jobs.json`'u repoya geri commit eder.
Cron `.github/workflows/job-check.yml` içinde (varsayılan: her 3 saatte bir). Elle test için
Actions sekmesinden **Run workflow** (workflow_dispatch) butonunu kullan.

---

## 5) Lokal test (`dotnet run`)

`.NET 8 SDK` gerekir (veya .NET 9 SDK ile de `net8.0` derlenir).

```powershell
# Secret'ları geçici olarak ayarla (PowerShell)
$env:TELEGRAM_BOT_TOKEN = "..."
$env:TELEGRAM_CHAT_ID   = "..."
$env:GMAIL_IMAP_USER    = "..."
$env:GMAIL_APP_PASSWORD = "..."

# Tüm kaynakları çalıştır (gerçekten Telegram'a gönderir, seen-jobs.json'a yazar)
dotnet run --project JobFinder/JobFinder.csproj

# Sadece tek bir kaynağı test et
dotnet run --project JobFinder/JobFinder.csproj -- --source greenhouse

# DRY-RUN: Telegram'a GÖNDERMEZ, seen-jobs.json'a YAZMAZ (sadece ekrana basar)
dotnet run --project JobFinder/JobFinder.csproj -- --dry-run

# Birden fazla kaynak + dry-run
dotnet run --project JobFinder/JobFinder.csproj -- --source greenhouse,lever --dry-run

# Mevcut kaynakları listele
dotnet run --project JobFinder/JobFinder.csproj -- --list-sources
```

### Dedup'ı doğrulama (önemli senaryo)
İki kez üst üste çalıştır; ikinci seferde **"YENİ ilan sayısı: 0"** görmelisin
(aynı ilanlar tekrar bildirilmiyor):

```powershell
dotnet run --project JobFinder/JobFinder.csproj -- --source greenhouse
dotnet run --project JobFinder/JobFinder.csproj -- --source greenhouse   # → 0 YENİ
```

---

## Dosya yapısı

```
job_finder/
├── JobFinder/                 # .NET 8 konsol projesi
│   ├── Program.cs             # orchestration + CLI + dedup akışı
│   ├── Models/                # JobPosting, AppConfig
│   ├── Matching/JobMatcher.cs # junior filtresi (Türkçe-duyarlı)
│   ├── Config/ConfigLoader.cs
│   ├── Storage/SeenJobsStore.cs
│   ├── Notifications/TelegramNotifier.cs
│   └── Sources/               # Greenhouse, Lever, LinkedIn, Telegram, WebScrape
├── config.json               # ayarlar (token'lar HARİÇ)
├── seen-jobs.json            # bildirilmiş ilanların dedup anahtarları
├── .github/workflows/job-check.yml
└── README.md
```

## ToS / güvenlik kuralları
- LinkedIn doğrudan scrape edilmez; sadece kendi gelen kutundaki alert maili okunur.
- Scraper'lar makul User-Agent kullanır, istek sıklığı düşüktür.
- Telegram modülü resmi Bot API kullanır (scrape değil).
- Otomatik başvuru / CV gönderimi yoktur.
