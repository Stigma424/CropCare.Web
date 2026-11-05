# Recommendation and notification device-test handoff

Updated 2026-11-05. Changes apply to API, Web and Android source. No commits, deployment, operational-data cleanup or synthetic farmer readings were performed.

## What changed

Web and Android consume the authenticated API assessment rather than calculating competing local advisory scores. The pipeline is readings -> quality/provenance checks -> eligible sensors -> weighted provisional score -> ordered actions -> separately sourced reference quantities. HYB-1 is the default; HYB-2 requires explicit selection. Missing scores remain unavailable; genuine zeroes remain zero. Coverage, quality reasons, source/version and reference-only limitations are displayed. Pro access remains enforced by the API. The AI wrapper is deferred: this is deterministic rules-based decision support, not an AI-generated prescription.

Notification history is authoritative on the server. Web polls its authenticated same-origin feed every 30 seconds while visible, with a badge for the latest 100 records; browser background push is not implemented. Android polls history while visible and uses data-only FCM with recipient checking, stable history IDs and a bounded persistent duplicate cache. Token registration/removal now uses authenticated API endpoints; logout detaches only the matching token. Android reports permission/registration status and supports read/delete/clear actions.

Condition alerts require two distinct, chronologically increasing samples with the same condition signature. Two healthy samples clear that condition. Invalid/incomplete/suspicious readings produce sensor-verification alerts without clearing soil warnings. Free farmers receive generic attention messages; Pro farmers can open detailed authorized advisories. First/reconnected API reporting produces a sensor-status notification. There is no scheduled offline detector: absence of uploads alone does not create a disconnected notification.

History creation and the push outbox are transactional. Delivery workers use four bounded sends, 15-second transport deadlines, two retries after the initial attempt (1 minute then 5 minutes), matching-token invalidation and stable notification IDs. An accepted_by_fcm state means provider acceptance, not proof of display. One profile token is supported per farmer; simultaneous multi-phone delivery is not promised.

## Actual verification

- API Release suite: 280 passed. One existing Firebase Message.Token deprecation warning remains.
- Web Release suite: 18 passed, including isolated Edge browser navigation and HYB-2 reference selection. Set CROPCARE_TEST_BROWSER=msedge when running the suite on this PC.
- Standalone Kotlin: three contract tests plus a syntax check across Android Kotlin source passed. The three contract tests are also in app/src/test/java/com/example/cropcare/RecommendationContractTest.kt.
- Android APK compilation, lint, installation and runtime testing are NOT verified. SDK Platform 34, Build Tools 34.0.0 and Platform Tools remain uninstalled because SDK acceptance/installation was declined. The local Gradle daemon also encountered a Windows loopback connection failure. Standalone checks do not replace an Android build.
- No physical FCM, SMTP, QR, ESP32 or cross-farmer transfer test was performed. Firestore transaction behavior and provider delivery still need authorized service/device tests.

## Local testing

Run from the actual repository roots:

    dotnet test CropCare.Api.sln -c Release
    $env:CROPCARE_TEST_BROWSER='msedge'
    dotnet test CropCare.Web.sln -c Release

Open https://localhost:7212/Account/Login and enter real credentials only on the website. With an authorized Pro account, open a zone advisory: check quality/coverage, switch HYB-1/HYB-2, and verify historical previews are labeled historical. A 400 square metre standalone reference is HYB-1 complete 18.60 kg / urea 5.60 kg; HYB-2 complete 18.60 kg / urea 11.80 kg. Live quantities are withheld without eligible readings or for a harvested cycle. Never grant Pro automatically for a test.

Open Notifications and verify refresh, unread styling, mark read, individual deletion and confirmed Clear All. Clear All is capped at 5,000 per API request; remaining records require another explicit action. Empty history is a valid result and does not prove push transport is working.

## ESP32 ingestion contract

Use POST /api/ingestion/soil with the provisioned device's Firebase bearer ID token, not a farmer token. deviceId must match its enabled, currently paired registry entry. Supply all seven numeric fields: nitrogen, phosphorus, potassium, moisture, ph, temperature, ec. Keep existing API measurement units/calibration; do not invent conversions. The server supplies the recorded timestamp; do not add an unrecognized timestamp field.

Add sampleId (8-128 letters, digits, underscore or hyphen): unique per actual measurement and reused unchanged for network retries. Replaying the same ID and values returns duplicate=true without another raw record/alert. Reusing an ID with different values returns 409. Idempotency is scoped to device, farmer, sensor and claim time, and pairing is checked again transactionally. Legacy uploads without sampleId remain accepted but cannot be deduplicated. Persist firmware IDs across reboot or include a boot nonce. Do not submit synthetic readings to farmer collections for testing.

Use real distinct consecutive readings to test condition confirmation, recovery and sensor verification. Check raw history, advisory eligibility and notification history separately. Null/omitted measurements are rejected by ingestion; accepted but suspicious measurements remain raw history and are excluded from advisory scoring. Confirm the actual hardware sends the calibrated seven parameters before interpreting recommendations.

## Phone/network prerequisites still pending

Wi-Fi remains Public. No profile or firewall change was made; LAN phone/ESP32 access is pending. Localhost is accessible only on this PC. Do not assume the PC's LAN address is reachable.

After separately approving SDK installation, build with JDK 17 and the existing Gradle wrapper (minimum SDK stays 24):

    .\gradlew.bat assembleDebug testDebugUnitTest lintDebug

The debug API URL can be selected using -Pcropcare.api.baseUrl=http://10.0.2.2:5056 for a standard emulator. A physical phone can use http://127.0.0.1:5056 only with an established adb reverse tcp:5056 tcp:5056 connection; Platform Tools are currently pending. A LAN URL requires the separately pending trusted-network/firewall setup. Release transport requires HTTPS.

On a built phone app: sign in, grant Android notification permission, check the Notifications registration status, then produce a qualifying real-device event. Verify foreground/background/terminated delivery, repeated delivery of the same history ID, token rotation, logout and account switching. Distinct history IDs must remain distinct even if message text matches. Verify tapping a push re-fetches authorized history; payload text never grants zone ownership.

Local test configuration enables assessment history and push for paired farmers, with legacy soil-threshold alerts disabled. Production defaults remain unchanged. Push enablement alone does not prove delivery; inspect outbox states and phone permissions without logging token/credential values. No automatic disconnect alert or browser background push is claimed.
