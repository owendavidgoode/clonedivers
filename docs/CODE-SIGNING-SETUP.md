# Windows Code Signing Setup (SmartScreen Relief)

`.github/workflows/forge-packages.yml` includes a conditional Windows Authenticode signing hook for `Clonedivers.exe`.

- **When secrets are absent (or on pull requests):** CI logs a skip notice, packages the unsigned `Clonedivers.exe` into the release ZIP + `SHA256SUMS`, and stays **100% green**.
- **When all 6 secrets are configured:** CI signs `Clonedivers.exe` via Microsoft's cloud HSM (`Azure/trusted-signing-action@v2`), verifies `Get-AuthenticodeSignature == Valid`, and then creates the release ZIP + `SHA256SUMS` from the signed binary.

---

## Option A: Azure Trusted Signing (10-Minute Active Setup — Recommended)

Microsoft's **Azure Trusted Signing** (now labeled **Artifact Signing** in the Azure Portal) costs **$9.99/month** on the Basic tier (includes 5,000 signatures/month), requires no USB hardware dongle, and supports individual developers via driver's license / passport verification. Public-trust certificates issued by Microsoft's root CA eliminate **"Unknown Publisher"** immediately and build Windows Defender SmartScreen reputation tied to your verified identity rather than resetting on every new `.exe` hash.

### Step 1 — Create the Signing Account & Identity Validation (~5 min, plus Microsoft ID review)

1. Sign in to the [Azure Portal](https://portal.azure.com) with a **Pay-As-You-Go** Azure subscription (free/trial subscriptions cannot create signing accounts; make sure your billing legal name and address match your photo ID).
2. Search for **Artifact Signing** (or **Trusted Signing**) -> **Create**:
   - **Account name:** e.g. `clonedivers-signing` (save this for `AZURE_SIGNING_ACCOUNT`).
   - **Region:** e.g. `East US` -> copy the **Account URI** shown on the overview page after creation (e.g. `https://eus.codesigning.azure.net/`, save for `AZURE_SIGNING_ENDPOINT`).
   - **Pricing tier:** `Basic` ($9.99/month).
3. Inside the signing account, open **Identity validations** -> **New identity** -> choose **Public** and **Individual** (or **Organization** if signing under your company), and complete the mobile photo-ID + selfie check.
4. Once validation shows **Completed**, open **Certificate profiles** -> **Create** -> **Public Trust**:
   - **Certificate profile name:** e.g. `clonedivers-public` (save for `AZURE_SIGNING_PROFILE`).
   - Select your verified identity and click **Create**.

### Step 2 — Create a Least-Privilege Service Principal (~3 min)

1. In Azure Portal, open **Microsoft Entra ID** -> **App registrations** -> **New registration**:
   - **Name:** `clonedivers-github-signing` -> click **Register**.
   - Copy **Application (client) ID** (`AZURE_CLIENT_ID`) and **Directory (tenant) ID** (`AZURE_TENANT_ID`).
2. Open **Certificates & secrets** -> **New client secret** -> choose an expiry (e.g. 12 or 24 months; set a calendar reminder to rotate it) -> copy the **Value** immediately (`AZURE_CLIENT_SECRET`).
3. Go back to your **Artifact Signing Account** -> **Certificate profiles** -> click your profile -> **Access control (IAM)** -> **Add role assignment**:
   - **Role:** `Artifact Signing Certificate Profile Signer` (or `Trusted Signing Certificate Profile Signer`).
   - **Members:** select `clonedivers-github-signing` -> **Review + assign**.
   *(Scoping the role to the single certificate profile ensures this credential cannot touch anything else in Azure.)*

### Step 3 — Add the 6 GitHub Repository Secrets (~2 min)

In `https://github.com/owendavidgoode/clonedivers/settings/secrets/actions`, add these 6 **Repository secrets**:

| Secret Name | Example / Source |
| --- | --- |
| `AZURE_TENANT_ID` | Directory (tenant) ID GUID from App registration |
| `AZURE_CLIENT_ID` | Application (client) ID GUID from App registration |
| `AZURE_CLIENT_SECRET` | Client secret **Value** (not Secret ID) |
| `AZURE_SIGNING_ENDPOINT` | Account URI, e.g. `https://eus.codesigning.azure.net/` |
| `AZURE_SIGNING_ACCOUNT` | Account name, e.g. `clonedivers-signing` |
| `AZURE_SIGNING_PROFILE` | Certificate profile name, e.g. `clonedivers-public` |

### Step 4 — Verify

1. Go to **Actions** -> **Forge launcher packages** -> **Run workflow** (`main`).
2. Confirm **Sign Clonedivers.exe with Azure Trusted Signing** and **Verify Authenticode signature** both pass green (`Authenticode status: Valid`).
3. Download `Clonedivers.exe` from the workflow artifact and verify locally in PowerShell:
   ```powershell
   Get-AuthenticodeSignature -LiteralPath .\Clonedivers.exe | Format-List Status, SignerCertificate, TimeStamperCertificate
   ```
4. Once live releases ship signed binaries, update Step 1 of `README.md` once SmartScreen prompt frequency drops.

---

## Option B: SignPath Foundation (Free OSS Alternative)

Because `owendavidgoode/clonedivers` is a public MIT-licensed open-source launcher (containing no proprietary game files or restricted third-party mod archives), it is also eligible to apply for **free** Authenticode code signing through **[SignPath.io Open Source Foundation](https://about.signpath.io/product/open-source)**:

- **$0/month** for qualifying public open-source repositories built exclusively on GitHub-hosted runners.
- Trade-off: requires submitting a short OSS project application (~1–2 weeks human review) and adding SignPath's GitHub App / signing policy file rather than self-serve instant activation.
