# Device pairing (sign in on a TV or app with a code)

A TV, a Roku, an Apple TV or Vora Desktop has no usable browser and a clumsy keyboard, so it signs in by **pairing**: it shows a short code, and the person approves that code on the web page where they are already signed in, however they signed in (a local password, an SSO provider, later LDAP). The approval carries the person's identity, so pairing works the same for every kind of account and needs nothing from the identity provider. It is the Vora-native flow, modelled on the OAuth device grant (RFC 8628) but run entirely by Vora.

Server code: `Vora.Application/Auth/Pairing/` (`DevicePairingManager`, `DevicePairingStore`, `DevicePairingCode`) and `Vora.Api/Endpoints/DevicePairingEndpoints.cs`. Web: `/link` (`pages/Auth/LinkDevicePage.tsx`) and the **Link a TV or app** card in Account settings (`components/Account/LinkDeviceCard.tsx`).

## The flow

1. The device calls `POST /api/auth/device/start`, shows `userCode` and `verificationUrl` (and a QR of `verificationUrlComplete`), and starts polling.
2. The person opens the page (scanning the QR fills the code in), signs in if they are not, checks the device it names, and presses Approve (or Deny).
3. The next poll returns `approved` with the same sign-in a password login gives. The device then does what it always does after a login: list profiles, exchange for a profile token, and refresh with the refresh token.

## Client contract

All bodies are JSON. Send the `X-Vora-*` device headers (`Device-Id`, `Client`, `Device`, `Device-Type`, `OS`) on `start`: they are what the approval page shows the person ("Living Room, Vora Roku, Roku OS"), so use a name they will recognise.

`POST /api/auth/device/start` (anonymous, 10 per 5 minutes per address) -> 200:

```json
{ "deviceCode": "…43 chars…", "userCode": "BCDF-GHJK", "verificationUrl": "https://vora.example.com/link",
  "verificationUrlComplete": "https://vora.example.com/link?code=BCDFGHJK", "expiresInSeconds": 600, "intervalSeconds": 5 }
```

Show `userCode` large, in two groups of four, with `verificationUrl` under it, and offer `verificationUrlComplete` as the QR. Keep `deviceCode` secret: it is the only thing that can collect the sign-in. 403 (title "Device blocked") when an admin has blocked this device id. The address is the server's **public address** setting (`docs/sso.md`), else the address the request came to.

`POST /api/auth/device/poll { "deviceCode": "…" }` (anonymous, 90 per minute per address) -> always 200 with a `status`:

| `status` | Meaning and what to do |
| --- | --- |
| `pending` | Nobody has answered. Poll again after `intervalSeconds`. |
| `slow_down` | You polled sooner than the interval. Use the new `intervalSeconds` (it grows by 5, up to 30) and keep going. |
| `approved` | `auth` holds `{ accessToken, userId, displayName, isAdmin, refreshToken }`, the same shape as a login (`refreshToken` included). Delivered **once**: the pairing is gone after this answer. |
| `denied` | The person pressed Deny, an admin blocked the device meanwhile, or the account can no longer sign in. Show "not signed in" and offer a new code. Reported once. |
| `expired` | The code ran out (10 minutes), was already collected, or never existed. Start again. |

Poll every `intervalSeconds` (5 by default; half a second early is tolerated). Stop at `approved`, `denied` or `expired`, and when the screen closes. A device that loses the answer to an `approved` poll must start again, since the answer is not repeated.

The device session behaves like any other: `POST /api/auth/refresh` with the refresh token, one refresh token per device id (a new sign-in retires the device's earlier one). Which sign-in method the session counts as depends on how the approver can sign in: an account with a turned-on provider gets an `External` session, which survives an admin turning password sign-in off; a password-only account gets a `Password` session, which ends at the next refresh once password sign-in is off for non-admins.

## What the page does

`GET /api/auth/device/lookup?userCode=` (needs a token, `AuthStrict`) -> `{ userCode, clientName, deviceName, deviceType, operatingSystem, location, sameNetwork, requestedAt, expiresAt }`. `location` is "Local Network" for a private address, else a city and country from the same lookup the Devices page uses; `sameNetwork` is true when the device and the browser share an address or are both private. The page warns when it is false, because that is what a phishing attempt looks like: someone starting a pairing elsewhere and talking a person into approving it.

`POST /api/auth/device/approve { "userCode" }` and `POST /api/auth/device/deny { "userCode" }` (need a token) -> `{ "status": "approved" | "denied" }`. The code is typed any way a person types it: case, spaces and the dash do not matter.

| Status | Title (ProblemDetails) | Why |
| --- | --- | --- |
| 404 | Code not found | Not waiting for approval: wrong, expired, or already answered. The same answer for all three, and it counts as a wrong code. |
| 409 | Already answered | Approve or deny on a code that was answered between the lookup and the click. |
| 403 | Password sign-in is turned off | The password switch is off and this account is neither an administrator nor linked to a turned-on provider (approving would be a way around the switch). It can still deny. |
| 403 | Device blocked | An admin blocked the device after it asked. |
| 429 | Too many wrong codes | Five codes in ten minutes that matched nothing lock that **account** out of lookup and answers for ten minutes. Plus the `AuthStrict` limit per address. |

The approval is bound to the **caller's account**, taken from the token (`GetAccountId`), never from the request: whoever approves is who the device becomes.

## Rules that keep it safe

- `userCode` is 8 characters from `BCDFGHJKMNPQRSTVWXZ23456789` (no vowels, so no words; no 0, O, 1, I or L), about 2.8 x 10^11 codes, valid ten minutes, one pairing per code. It can only be guessed by someone already signed in, and five misses lock them out.
- `deviceCode` is 32 random bytes, stored hashed. Pairings live in memory (`DevicePairingStore`, a singleton): a restart drops them and the device shows a new code, like the one-time sign-in codes in `docs/sso.md`. Expired pairings are purged on the next start, and at most 2000 are kept.
- A blocked device (Devices page) cannot start a pairing, cannot be approved, and is refused at the moment of delivery, so blocking still works for a device that asked before it was blocked. Revoking a device's sessions works as before (refresh tokens, `docs/auth-tokens.md`).
- The device gets the same account token (two hours) and refresh token (90 days) a login gives; nothing about pairing makes a session longer-lived or wider.

## Coming back after signing in

A signed-out visitor to `/link?code=…` is sent to the login page (or profile selection) with the address remembered in `sessionStorage` (`post_sign_in_redirect`, ten minutes, app paths only: `utils/postSignInRedirect.ts`). Because it is per tab and survives the redirect to an identity provider and back, the code survives an SSO sign-in. `RequireAuth` sends the visitor back when they land on `/` after signing in, and the link page clears it once it opens signed in.

## Vora Desktop: Sign in with your browser

Vora Desktop cannot finish an identity-provider redirect inside its window (the shell opens off-origin pages in the system browser, so the person ends up signed in there and not in the app), so inside Desktop the sign-in page replaces the "Continue with X" buttons with **Sign in with your browser**, shown beside the password form (`components/Auth/BrowserSignIn.tsx`, `hooks/useBrowserSignIn.ts`). It is offered only when the shell can open a browser (`window.voraDesktop?.openExternal`, bridge v3, `desktop/desktopBrowser.ts`), so a plain browser and an older Desktop keep today's page.

It is the client contract above with the web client as the device: `start`, then `bridge.openExternal(verificationUrlComplete)` (the shell opens only an address on the connected server's origin), a "Waiting for approval in your browser" panel with the code, **Open again** and **Cancel**, and `poll` every `intervalSeconds` (a `slow_down` or a 429 waits longer; five network failures in a row stop it). `approved` stores the account the way a password login does (`utils/signInSession.ts`) and goes on to profile selection; `denied` and `expired` offer **Get a new code**. The refresh token in the answer is not used: the web client never refreshes. It works for every kind of account, since the approval happens in the browser where the person already signs in however they do. The other two places that send a person to an identity provider use the same hand-off to the system browser (`docs/sso.md`, Inside Vora Desktop): the Register page's **Continue with X** opens the provider's start address in the browser and then shows this same **Sign in with your browser** panel, and Account settings' **Link X** opens the link address in the browser and refreshes the list when the window gets focus back. Nothing approves a pairing for the person: approving stays on the approval page in the browser.
