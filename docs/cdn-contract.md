# Spectralis Release CDN

Downloads and update feeds live on `https://spectralis-cdn.deltavdevs.com`, a Cloudflare R2 bucket
(`spectralis-cdn`) served on its own subdomain. Warnings, the changelog and verified creators are
**not** here any more; they come from the backend ([content-api.md](content-api.md)).

See [api-contract.md](api-contract.md) for every other origin.

---

## Layout

Everything is at the bucket root. There are no folders.

| File | What it is |
|---|---|
| `releases.win-x64.json`, `releases.linux-x64.json`, `releases.osx-arm64.json`, `releases.osx-x64.json` | Velopack feeds. The app reads `releases.<rid>.json`. |
| `RELEASES-win-x64`, `RELEASES-linux-x64` | The older text feed (one `sha1 name size` line per package). |
| `Spectralis-<version>-<rid>-full.nupkg` | A full package. |
| `Spectralis-<version>-<rid>-delta.nupkg` | A delta from the previous version. |
| `Spectralis-win-x64-Setup.exe` | Windows installer. |
| `Spectralis-linux-x64.AppImage` | Linux AppImage. |
| `Spectralis-osx-arm64-Setup.pkg`, `Spectralis-osx-x64-Setup.pkg` | macOS installers (macOS stays on the 6.0.0 LTS line). |

The app's update client reads `https://spectralis-cdn.deltavdevs.com/releases.<rid>.json` (see
`VelopackUpdateService`). The website's download buttons point at the installers above.

Only the newest four release versions are kept. Older ones stay on the legacy CDN and in the local
backup; the feeds are pruned to match, so a client is never told to fetch a package that is gone.

---

## Publishing

`deploy.ps1` uploads to the legacy CDN (installed apps from 7.0.0 and earlier still read it) and then
runs the R2 upload. The R2 step needs `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY`
and can be skipped with `-SkipR2`.

The R2 upload is `tools/r2/sync.mjs`, which does a dry run unless given `--apply`:

```powershell
node tools/r2/sync.mjs --source releases-velopack            # shows the plan, writes nothing
node tools/r2/sync.mjs --source releases-velopack --apply
```

Packages already in the bucket at the same size are skipped, packages go up before feeds, and every
upload is verified by size. See [`tools/r2/README.md`](../tools/r2/README.md).

---

## The storage cap

R2 has no hard storage limit; past the 10 GB free tier it keeps accepting writes and bills for them.
So the cap is enforced in two independent layers:

1. **The upload tool.** It plans before writing and refuses if the plan can't stay inside its budget
   (5 GB by default, never more than 8 GB, whatever flag or variable is passed). It measures the real
   bucket afterwards and reports failure if it is over.
2. **A janitor Worker** (`spectralis-janitor`, hourly) inside Cloudflare. If the bucket is ever over
   8 GB for any reason, it deletes unrecognised objects and then old releases until it is back under
   80% of that. It never deletes feeds, installers or the newest two versions.

A Billing usage alert on R2 storage in the Cloudflare dashboard is the third layer and is set by hand.

---

## Legacy CDN

`https://cdn.deltavdevs.com/spectralis` keeps the same files (plus the old `warning.json`,
`changelog.json`, `community.json`, `keys/`, `visualizers/` and `web-share/`). It exists for installs
on 7.0.0 and earlier. The redeemable visualizers (`/spectralis/visualizers`) have not moved yet.
