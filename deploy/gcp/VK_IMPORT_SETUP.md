# VK friends birthday import (opt-in)

BirthdayHelper can import **available** birthdays from your VK friend list. The importer uses VK ID OAuth 2.1 Authorization Code + PKCE and the VK API `friends.get` method (`fields=bdate`).

## Important prerequisites

- A VK ID application registered at <https://id.vk.com/>.
- The application must allow use of the **friends** API permission/scope. This permission may be restricted or require application review. OAuth login alone does **not** guarantee friends access.
- Redirect URI registered **exactly**:

  `https://birthday-helper-kqxrqpdkya-lm.a.run.app/integrations/vk/callback`

- Numeric VK ID application ID. This is **not** an access token or secret.

## Enable in production

1. GitHub → `HromykoIvan/BirthdayHelper` → Settings → Secrets and variables → Actions → **Variables**.
2. Create repository variable `VK_ID_CLIENT_ID` with the numeric VK app ID.
3. Rerun the `Deploy BirthdayHelper to Google Cloud Run` workflow (or push to `master` after changing application code).
4. In Telegram `@IvGrTestBot`, send `/import` and use **ВКонтакте / VK**.
5. Log in at the official VK ID page and authorize the requested read-only friends permission.
6. Return to Telegram, review the imported names/dates, choose entries, then confirm. Existing duplicate checks are used.

**Never** paste your VK password, access token or refresh token into Telegram, GitHub issues or ChatGPT.

## Design and limitations

- OAuth state and PKCE verifier are saved for **10 minutes** in MongoDB with a TTL index, then consumed atomically on callback.
- VK access/refresh tokens are **not persisted**. Every import requires a new authorization.
- Only visible friend names and birthday dates are retained; hidden `bdate` fields cannot be fetched.
- Friends with day/month only are stored with `BirthYearKnown=false`; no fake age is shown.
- A maximum of **1,000** friends are inspected per import.
- VK account data is not automatically synchronized. This is a one-time import.
- The existing Telegram import preview allows explicit selection and flags name/date conflicts.
- The VK ID application's permission approval and actual API connectivity **must be tested** with an authorized VK account. CI cannot verify that VK grants `friends.get` access.
- The importer is disabled by default until `VK_ID_CLIENT_ID` is configured.

To disable again, delete the repository variable `VK_ID_CLIENT_ID` and redeploy.

No server-side client secret is needed for this authorization-code PKCE exchange. The numeric app ID is non-secret.
