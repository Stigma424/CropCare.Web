# Username registry migration runbook

> Update: Real local sign-in and the approved seven-reservation registry backfill are complete. See [CROPCARE_SIGNIN_RESTORED.md](CROPCARE_SIGNIN_RESTORED.md) for verified results. Earlier pending/audit statements below describe the original implementation stage.

Do not run apply until the dry-run report has been reviewed and a maintenance window approved. No live audit or apply was executed during implementation.

1. Back up usernames, profile document IDs and registry reservations to restricted storage. Do not export passwords, tokens or unrelated soil records.
2. With the existing Firebase:ProjectId and application-default credentials configured, run from the API repository:

   dotnet run --project CropCare.Api -- --audit-usernames

3. Review profile, invalid-name, case-collision, mismatched-owner and missing-reservation counts plus the SHA-256 review fingerprint. The utility projects username/userId only. The optional --report path creates a new restricted JSON artifact listing invalid names, collision groups, missing reservations and mismatched owners, without password/contact/token fields. Protect it as account metadata and never commit it. Inspect ambiguous owners privately through authorized tooling; no arbitrary owner selection or automatic rename is allowed. Resolve conflicts explicitly and rerun dry run. A nonzero invalid/collision/mismatch count blocks all writes.
4. Pause every profile/username writer, including legacy mobile clients, throughout the approved maintenance window. Re-run the dry run. Confirm a restricted backup and the exact reviewed fingerprint.
5. Only after explicit review, apply:

   dotnet run --project CropCare.Api -- --audit-usernames --apply --reviewed-sha256 REVIEWED_FINGERPRINT

The apply command refuses a changed report or any ambiguity, marks registry readiness false, transactionally creates missing reservations only and validates the current profile/owner. It never overwrites owners, renames users or deletes accounts/readings. It marks system_metadata/username_registry_v1 ready only after completion. An interrupted apply leaves login unavailable; inspect, rerun dry run and review the new fingerprint before retrying.

6. Test approved accounts, case variations, wrong passwords, disabled accounts and logout on an isolated/authorized environment before rollout. Re-enable only registry-aware writers. Legacy clients that bypass the registry must remain blocked from username/profile creation by the separately reviewed backend/rules policy.

Existing passwords are not rewritten. Plaintext legacy password field cleanup, production rules and historical ownership policy require a separate approval. Review IAM/custom-token signing and Firebase quotas. Never log submitted credentials, Firebase response bodies or custom tokens.
