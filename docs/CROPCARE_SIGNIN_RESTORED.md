# Real sign-in restored — 2026-11-04

This update supersedes the original handoff's pending local Firebase configuration and username migration items. Other service/device/design checks remain pending.

## Repair completed

- Matched the existing Web Firebase API key's project number to the CropCare project ID and existing service-account credential. No secret values were printed.
- Added the existing key to API user secrets and corrected the user-scoped GOOGLE_APPLICATION_CREDENTIALS environment variable. No credentials or machine paths were added to committed configuration.
- Removed the credential-path override from the API launch profile so it inherits local environment configuration.
- Stopped only the local CropCare API/Web processes for the reviewed registry update and restarted both apps.

## Reviewed registry update

The pre-apply dry run matched the approved fingerprint exactly:

8674a4e6620fd874ace462ddebe85023ca3ef876b9c34bdd41e2f2d8cebfe001

Profiles=8, Invalid=0, CollisionGroups=0, Mismatches=0, Missing=7.

The approved utility created the seven missing reservations, preserved the existing reservation and marked the registry ready. No profiles were renamed/deleted, passwords changed, or readings/QR records altered.

The post-apply read-only audit reported Profiles=8, Invalid=0, CollisionGroups=0, Mismatches=0, Missing=0. Its new fingerprint is:

f04ba57089755dbf762f4f8e686a3925e82e7ab1a3f001f4e373f59707167e11

Do not reuse the old fingerprint for another apply.

## Verification

- Live HTTPS login page returned 200 and displayed the Username form.
- Nonexistent diagnostic credentials received generic 401 instead of service-unavailable.
- User entered their own credentials in the website and confirmed: Dashboard opens. Real custom-token exchange and profile verification completed through the normal sign-in flow; no bypass or substitute account was used.
- API: 10 tests passed. Web: 18 tests passed, including browser navigation at desktop/tablet/phone widths. Tests use isolated fixtures rather than live measurements.
- The first attempted rebuilding test run encountered running-executable locks. The successful rerun used dotnet test --no-build against the already-built assemblies. No application-source changes were needed for this repair.

Local website: https://localhost:7212/Account/Login
Local API: https://localhost:7202/ and http://localhost:5056/

Restart existing Visual Studio/terminal processes before a future launch so they inherit the corrected user environment. The currently restarted apps already use it.

## Still pending

Real SMTP, payment review, physical QR/ESP32 and cross-farmer historical isolation remain as documented in the original checklist. Real account navigation beyond the user-confirmed dashboard was not independently observed through browser control; isolated browser navigation passes. Case variants and logout pass isolated tests, but were not separately repeated with the user's real credentials.

No mobile edits, commits, pushes, production rules, authentication bypass or password capture.
