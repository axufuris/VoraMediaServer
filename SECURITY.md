# Security Policy

## Reporting a vulnerability

Please **don't** report security problems in a public issue or discussion.

Report them privately through GitHub: go to the repository's **Security** tab and choose **Report a vulnerability**, or use [this link](https://github.com/axufuris/VoraMediaServer/security/advisories/new). Only the maintainers can see the report.

Please include:

- what the problem is and what someone could do with it;
- the steps to reproduce it, or a proof of concept;
- the Vora version (shown at the bottom of the admin sidebar) and how it's installed (Unraid, Docker Compose, other);
- anything you've found about which part is affected (the API, the web client, a plugin, a native client).

You'll get an acknowledgement within a few days. Once a fix is ready it's released and the advisory is published, crediting you unless you'd rather not be named.

## Supported versions

Vora is in beta. Fixes go into the latest release only, so please update before reporting and check whether the problem is still there.

## Scope

In scope: the Vora server and its API, the web client, the plugins shipped in this repository, the Docker image and the Unraid templates. The Android, Fire TV and Roku clients are also covered; report them here too.

Out of scope: problems that need an already-compromised server or admin account, missing security headers with no demonstrated impact, and issues in third-party services Vora connects to (TMDB, OpenAI, Last.fm and so on). Report those to the service.
