# Unraid Community Applications templates

These templates let Unraid users install Vora from **Apps** (Community Applications).

- `../ca_profile.xml` — repo profile (author, support, icon) read by Community Applications. Kept at the repo root.
- `vora.xml` — the Vora media server container.
- `vora-postgres.xml` — the required PostgreSQL + pgvector database.

## Requirements for users
Vora needs a PostgreSQL database with the `pgvector` extension. Install **vora-postgres**
first, and put both containers on the same user-defined Docker network (create one under
**Settings → Docker → Add network**, e.g. `vora`) so Vora can reach Postgres by name.

## Submitting to Community Applications
1. Ensure a `:latest` image tag exists (cut a production GitHub release).
2. Push these files to the public repo.
3. Submit the repo URL at https://ca.unraid.net/submit/new (sign in → add repo → review → submit).

Support questions go to [Q&A in Discussions](https://github.com/axufuris/VoraMediaServer/discussions/categories/q-a); bugs to [Issues](https://github.com/axufuris/VoraMediaServer/issues/new/choose).
