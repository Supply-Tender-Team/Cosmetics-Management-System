# File storage

`FileStorageService` saves files to the local `App_Data/uploads` folder when R2 settings are empty. When all four R2 settings are present, new files are saved to a private Cloudflare R2 bucket through its S3-compatible API. File paths stored in `BidDocuments` use the `r2:` prefix for remote files and `local:` for local files.

Add these keys to the ignored local `.env` file (never commit the real values):

```text
R2_ACCOUNT_ID=
R2_BUCKET=tender-files
R2_ACCESS_KEY_ID=
R2_SECRET_ACCESS_KEY=
```

Create an R2 bucket and an API token limited to that bucket with Object Read & Write permissions. Keep the bucket private. The app checks file extension and size using the existing `UPLOAD_ALLOWED_EXTENSIONS` and `UPLOAD_MAX_MB` settings before upload. R2 download requests are made by the app only after checking access: suppliers can access their own offers, and evaluation committee users can access offers only for tenders where they are committee members.

Files already stored locally remain local. This service does not automatically copy existing files to R2. The app must be reachable by the team and connected to a shared SQL Server database separately; R2 only centralizes the files.
