# tools/r2

Uploads Spectralis release artifacts to the `spectralis-cdn` Cloudflare R2 bucket (served at
`https://spectralis-cdn.deltavdevs.com`) and keeps that bucket from ever growing past a budget.

R2 has **no hard storage cap**. Past the 10 GB free tier it keeps accepting writes and bills for them
($0.015/GB-month). So the cap is enforced here, in two independent layers:

1. **`sync.mjs`**, the only normal way in. It plans before it writes (`budget.mjs`), refuses outright if the
   plan can't stay within budget, prunes to the newest releases, verifies every upload, and measures the real
   bucket afterwards. Default budget 5 GB; no flag or env var can raise it above 8 GB.
2. **The janitor Worker**, an hourly cron inside Cloudflare that doesn't trust the upload tool. If the bucket is
   ever over 8 GB for any reason (a bug, a dashboard upload, a leaked token) it deletes unrecognised objects and
   then old releases until it's back under 80% of that. It never touches feeds, installers or the newest two
   versions.

A Cloudflare Billing usage alert on R2 storage is the third layer and has to be set in the dashboard.

## Uploading

```powershell
$env:R2_ACCOUNT_ID = "<account id>"
$env:R2_ACCESS_KEY_ID = "<s3 key id>"
$env:R2_SECRET_ACCESS_KEY = "<s3 secret>"

npm ci
node sync.mjs --source ..\..\releases-velopack            # dry run, writes nothing
node sync.mjs --source ..\..\releases-velopack --apply
```

`deploy.ps1` runs this for you after the legacy CDN upload. Exit codes: 0 ok, 1 refused (nothing written),
2 written but the bucket measured over budget, 3 error.

## The janitor

```powershell
$env:CLOUDFLARE_API_TOKEN = "<token that can edit Workers>"
$env:CLOUDFLARE_ACCOUNT_ID = "<account id>"
node deploy-janitor.mjs                       # hourly at :17, watches spectralis-cdn
node deploy-janitor.mjs --dry-run-worker      # only logs what it would delete
```

## Layout

- `budget.mjs`: the rules (pure, shared by both layers)
- `sync-core.mjs`: the upload run, written against a small storage interface
- `sync.mjs`: wires that to R2's S3 API
- `janitor.mjs`: the Worker; `deploy-janitor.mjs` ships it
- `*.test.mjs`: `npm test`; includes a randomized property test that any plan reporting ok stays within budget at
  its peak and at the end

The old CDN at `cdn.deltavdevs.com` keeps being published to as well, because installed clients read the feed
URL their build shipped with.
