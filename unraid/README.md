# Unraid Community Applications templates

These templates let Unraid users install Vora from **Apps** (Community Applications).

- `../ca_profile.xml` — repo profile (description, support, icon) read by Community Applications. Kept at the repo root.
- `vora.xml` — the Vora media server container.

## Requirements for users
Vora needs a PostgreSQL database with the `pgvector` extension. From **Apps**, install the
existing **pgvector** application, name its container `vora-postgres`, and set a database,
user, and password for Vora. Put the pgvector container and Vora on the same user-defined
Docker network (create one under **Settings → Docker → Add network**, e.g. `vora`) so Vora
can reach Postgres by container name. If you name the pgvector container something other than
`vora-postgres`, update the **Database Connection** host in the Vora template to match.

## Submitting to Community Applications
1. Ensure a `:latest` image tag exists (cut a production GitHub release).
2. Push these files to the public repo.
3. Submit the repo URL at https://ca.unraid.net/submit/new (sign in → add repo → review → submit).

Support questions go to [Q&A in Discussions](https://github.com/axufuris/VoraMediaServer/discussions/categories/q-a); bugs to [Issues](https://github.com/axufuris/VoraMediaServer/issues/new/choose).
