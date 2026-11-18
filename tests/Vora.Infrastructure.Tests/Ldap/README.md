# Real-directory LDAP tests

`NovellLdapDirectoryTests` talks to a real OpenLDAP and is skipped unless two environment variables are set, so CI (which has no directory) skips it and the unit tests cover the same rules with a fake directory.

Run a directory whose certificate is for `localhost` and `127.0.0.1`, signed by a CA you keep as a PEM file (the directory's own default certificate has expired, so make one with `openssl`):

```
docker run -d --name vora-ldap --hostname localhost -p 3389:389 -p 3636:636 \
  -v <volume holding ca.crt server.crt server.key dhparam.pem>:/container/service/slapd/assets/certs \
  -e LDAP_ORGANISATION=Example -e LDAP_DOMAIN=example.org -e LDAP_ADMIN_PASSWORD=adminpw \
  -e LDAP_TLS=true -e LDAP_TLS_CRT_FILENAME=server.crt -e LDAP_TLS_KEY_FILENAME=server.key \
  -e LDAP_TLS_CA_CRT_FILENAME=ca.crt -e LDAP_TLS_DH_PARAM_FILENAME=dhparam.pem -e LDAP_TLS_VERIFY_CLIENT=never \
  osixia/openldap:1.5.0
docker cp directory.ldif vora-ldap:/tmp/directory.ldif
docker exec vora-ldap ldapadd -x -H ldap://localhost -D cn=admin,dc=example,dc=org -w adminpw -f /tmp/directory.ldif
```

Then:

```
VORA_TEST_LDAP=localhost:3389:3636 VORA_TEST_LDAP_CA=<path to ca.crt> dotnet test tests/Vora.Infrastructure.Tests --filter NovellLdapDirectoryTests
```

`directory.ldif` holds `alice` and `bob` (with mail), `carol` (no mail), two entries sharing one mail (`dup1`, `dup2`), and two `groupOfNames` groups.
