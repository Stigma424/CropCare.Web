# Verification and remaining acceptance

> Update: Real local sign-in and the approved seven-reservation registry backfill are complete. See [CROPCARE_SIGNIN_RESTORED.md](CROPCARE_SIGNIN_RESTORED.md) for verified results. Earlier pending/audit statements below describe the original implementation stage.

Date: 2026-11-04. Tests run on .NET SDK 9.0.318 targeting .NET 8, using xUnit and local Microsoft Edge through Playwright.

## Reproduce without Android Studio

From CropCare.Web repository:

```powershell
$env:CROPCARE_TEST_BROWSER = 'msedge'
$env:CROPCARE_ARTIFACT_DIR = 'ABSOLUTE_SCREENSHOT_DIRECTORY'
dotnet build CropCare.Web.sln
dotnet test CropCare.Web.sln
```

From CropCare.Api repository:

```powershell
dotnet build CropCare.Api.sln
dotnet test CropCare.Api.sln
```

The browser test requires Edge, or installed Playwright Chromium with the channel variable unset. Browser requests are limited to an ephemeral loopback server; backend Firebase/API clients are substituted. Fake data is source-only test fixture data and is never written to operational collections. The browser bridge uses loopback HTTP (a browser secure-context exception) with TestServer HTTPS requests; production Secure cookies and HTTPS middleware remain enabled.

## Recorded results

- Web: 18 passed, 0 failed, including the full browser navigation/screenshot test.
- API: 10 passed, 0 failed.
- Both project builds passed. No live Firebase/SMTP/device test was run.

## Actual scope of checks

- Private username normalization/password preservation, generic failures, UID mismatch, missing/disabled accounts, review readiness, throttling, registry collisions and owner mismatch.
- HTTP login, no-store grants, invalid identifiers and scoped infrastructure-error handling.
- Web login/custom-token exchange with a Firebase response lacking localId, protected session, local-only return URL and anti-forgery rejection.
- Completed-profile registration retry, new password 7/8/128/129 boundaries, six-digit leading-zero recovery, expired code and consumed-ticket navigation.
- All four subscription states; unknown status stays unknown. API-authorization invalidation and malformed upstream response handling.
- Notification API mutations and capped clear with remaining records; authenticated unpair and truthful QR reuse results; zone deletion conflict/denied sensor presentation.
- Genuine zero, missing metrics, stale/partial monitoring, empty history, unified history redirects and cursor link.
- Browser navigation across 1440 desktop, 768 tablet and 390 phone widths; no horizontal overflow or JavaScript errors, password toggle, recovery forms, login, zone filter, calendar navigation, responsive menu/Escape and empty date-filter results retaining Load More.

Twenty-eight screenshot artifacts were captured. Login, dashboard, zone and phone history were visually inspected. This is not a claim that every frame has had final pixel-level design sign-off; four Figma detailed contexts remain unavailable due service quota, and other screenshots remain available for reviewer inspection.

Web build has no warnings. API retains three pre-existing warnings: IngestionController nullability and deprecated FCM Message.Token in ingestion and ZoneAssessmentService. Assessment code was left unchanged.

## Pending external acceptance

- Authorized Firebase sign-in/custom-token signing and reviewed username audit/backfill; no live migration was executed.
- Actual SMTP arrival, spam/expiry/reuse behavior and post-reset username sign-in; recovery tests substitute API behavior.
- Real Firestore ownership/transaction races, deletion conflicts and historical isolation across farmers. Website fake responses do not prove the backend owner checks.
- Physical QR scan/claim release/reuse and ESP32 ingestion, calibration and stale transition.
- Manual subscription review with approved test accounts; no automatic activation.
- Multi-instance throttling/session deployment review, trusted proxy configuration and production HTTPS/error routing smoke test.
- Four Figma frame comparisons and remaining screenshot review; keyboard/screen-reader usability review beyond automated menu/focus checks.
- Exhaustive registration response-loss/race, timeout/cancellation, chart midnight/multiple-page edge fixtures and stale recommendation-popup browser races remain additional regression coverage. Current tests do not claim the entire acceptance matrix is automated.

No mobile files, Firebase users, soil readings, QR claims, deployed rules or datasets were changed by these checks. No commits or pushes were made.
