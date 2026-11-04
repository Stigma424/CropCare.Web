# CropCare Web repair handoff

> Update: Real local sign-in and the approved seven-reservation registry backfill are complete. See [CROPCARE_SIGNIN_RESTORED.md](CROPCARE_SIGNIN_RESTORED.md) for verified results. Earlier pending/audit statements below describe the original implementation stage.

Implemented 2026-11-04. Mobile remains on hold. No commits, pushes, live registry changes, rule deployments or operational-data cleanup were performed.

## Implemented scope

- Private case-insensitive username/password API login; Firebase credential verification, registry-owner UID check and custom-token exchange. Tokens stay on the web server. Login is deliberately unavailable until the registry audit is reviewed and applied.
- Registration retry/profile ownership checks, 8–128 character new passwords, one email-code recovery flow, safe return URLs, anti-forgery POST mutations, bounded sessions and API-authorization invalidation.
- Separate authentication and farmer layouts, locally hosted Inter, green sidebar/title bars, responsive navigation, password controls and keyboard focus.
- Dashboard, zones, harvest/replant, sensors/QR release, seven-metric history, notifications, settings/account/help and manual subscription states use actual API data. Missing measurements remain unavailable; genuine zeroes remain zero. History filters describe loaded-page coverage.
- Compatibility redirects replace duplicate history, legacy recovery and placeholder Home routes. Factual privacy and production error views replace scaffolding. Only reference-checked dead files were removed.
- Recommendation algorithms, assessment datasets and pre-existing hybrid-corn work are preserved. Snapshot soil health is explicitly provisional, not long-term fertilization efficiency.

## Design coverage

Reference: Figma Website page 30:82, file n4P2AUS6E4witAV4soKibE, and the supplied paper.
Detailed context was inspected for login, registration, request/verify recovery, dashboard, add zone, notifications, settings hub, help, logout, edit profile, history, zone, harvested zone, pair and rename sensor screens.
The Figma service quota prevented detailed retrieval for reset password (682:885), account settings (682:1162), change password (682:1245) and sensor status (682:1347). Those use verified shared styles; exact visual sign-off for these four remains pending. Screenshots use isolated fixtures, never live readings.

## Deployment prerequisites and limitations

See USERNAME_REGISTRY_MIGRATION.md before enabling username login. Both API and Web require Firebase:WebApiKey for the same Firebase project. API also requires its existing Firebase credentials with custom-token signing permission; Web requires its existing API base address. Store secrets in environment/user secrets, never source control. Deploy Web and API together because the additive login grant contains customToken and userId.

Rate limits are per-process. Multiple production instances require a shared limiter or equivalent gateway policy. Review trusted proxy forwarding before using client IP limits behind a proxy. Token sessions remain memory-backed; restarting the web host signs users out.

External acceptance remains pending: actual Firebase/custom-token sign-in, reviewed registry migration, SMTP delivery/expiry, real payment review, physical QR/ESP32, cross-owner history policy and real Firestore ownership/conflict checks. Fake HTTP responses verify website handling; they do not prove deployed Firebase rules or Firestore transactions.
