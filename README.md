# Microsoft Entra ID + Zentitle2 Identity-Based Licensing Sample

This repository is a customer-facing sample that shows how to integrate Microsoft Entra ID with Zentitle2 Account-Based Licensing in a .NET MAUI application.

The sample signs a user in with Entra ID through MSAL and uses the returned OpenID ID token to activate a seat through the Zentitle2 Licensing Client. After login, the app displays the authenticated user details, relevant token claims, and the activation summary returned by Zentitle2.

## What This Sample Demonstrates

- Entra ID login in a native .NET MAUI application
- Entra ID client provisioning for EUP web login
- Token claim mapping for Zentitle2 Account-Based Licensing
- Seat activation with `openIdToken` credentials
- A repeatable Entra ID setup flow using the included script

## Integration Flow

1. The user signs in through Microsoft Entra ID.
2. Entra ID returns an OpenID ID token that includes `email`, `oid`, and `roles` when the optional claim and app role assignment are configured.
3. The app passes that ID token to Zentitle2 to activate the seat.
4. Zentitle2 validates the token using the Entra OIDC metadata endpoint and returns entitlement/activation details.

## Repository Layout

- `scripts/setup-entra.sh` - Entra ID provisioning helper
- `appsettings.json` - configuration template
- `appsettings.Development.json` - local configuration generated and updated by the script

## Prerequisites

Before you run the sample, make sure you have:

- a Zentitle2 account
- a Zentitle2 product already created
- the Zentitle2 `TenantId`
- the Zentitle2 `LicensingApiUrl`
- the Zentitle2 `ProductId`
- a Microsoft Entra tenant
- Azure CLI logged in with permissions to manage app registrations, service principals, app roles, and optionally groups
- `bash`, `jq`, and `perl`
- .NET 10 SDK with MAUI workloads installed

## Step 1: Configure Zentitle2

For the full Zentitle2 UI process, see the Nalpeiron documentation:

[Setup Account Based Licensing with OpenID tokens](https://docs.nalpeiron.com/zentitle2-docs/ui-administration/configuration/account-based-licensing-identity-based-licensing/setup-account-based-licensing-with-openid-tokens)

For this sample, the key Zentitle2 setup is:

1. In Zentitle2, open `Administration > Configuration > Account Based Licensing`.
2. Add your Entra tenant as the identity provider.
3. Use these values:
   - `Authority URL`: `https://login.microsoftonline.com/<tenant-id>/v2.0`
   - `Expected Token Issuer`: `https://login.microsoftonline.com/<tenant-id>/v2.0`
   - `User ID Claim`: `oid`
   - `Entitlement Group IDs Claim`: `roles`
4. Make sure the entitlement you want to test already exists and is assigned to the correct customer.
5. Add the end user under that same Zentitle2 customer with authentication type `OpenID Token`.
6. Store the Entra user object ID in the value used by the authentication claim. In this sample, that means the Zentitle2 account should match the Entra user's `oid`.
7. Assign that user access to the entitlement you want to activate.

This sample displays the Entra `roles` claim as the entitlement marker. The setup script creates an app role whose value is the Zentitle2 entitlement group ID; users receive that `roles` value only when they or one of their groups are assigned to the app role.

## Step 2: Run the Entra Setup Script

From the repository root, run:

```bash
bash scripts/setup-entra.sh
```

The script creates or updates the Entra objects required by this sample and writes local configuration to `appsettings.Development.json`.

During the script, you will be prompted for:

- your Entra tenant ID
- the native application name to create or update
- the EUP web application name to create or update
- the EUP URL, for example `https://oriondev.eup.nalpeiron-dev.com:8443`
- the Zentitle2 `ProductId`
- the Zentitle2 entitlement group ID, used as the Entra app role value
- whether to create and assign a security group to that app role

The script derives the EUP callback URL by appending `/sso` to the EUP URL. For example, `https://oriondev.eup.nalpeiron-dev.com:8443` becomes `https://oriondev.eup.nalpeiron-dev.com:8443/sso`.

### What the Script Provisions in Entra ID

The script:

- creates or updates a public-client app registration
- creates or updates a web-client app registration for EUP web login
- configures MSAL redirect URIs for Android, iOS, and desktop/Mac Catalyst
- configures the EUP `/sso` callback URL on the web-client app registration
- exposes the EUP web-client app as an API with a delegated `user_impersonation` scope
- adds the delegated Microsoft Graph `User.Read` permission
- configures the Entra `email` optional claim for ID tokens and access tokens
- creates or reuses an app role whose value is the Zentitle2 entitlement group ID
- ensures the Enterprise Application service principal exists
- optionally creates a security group and assigns that group to the app role
- updates `appsettings.Development.json`
- updates the Android MSAL redirect scheme in `Platforms/Android/MsalActivity.cs`

The final setup summary includes the EUP web app client ID and access token scope. Configure EUP with that Entra client ID, the printed v2.0 authority URL, and scope:

```text
openid profile email api://<EUP web app client ID>/user_impersonation
```

Without the `api://.../user_impersonation` scope, Entra can issue a Microsoft Graph access token instead of an EUP-audience access token.

## Step 3: Complete Local App Configuration

After the script finishes, open `appsettings.Development.json`.

The script fills in the Entra settings and some Zentitle2 fields, but you still need to provide or confirm:

- `Zentitle2.TenantId`
- `Zentitle2.LicensingApiUrl`
- `Zentitle2.ProductId`
- `Zentitle2.IdpUrl`

## Step 4: Assign Users to the Entra App Role

If the script did not create a group assignment, assign users or groups to the app role in the Entra Enterprise Application:

`Enterprise applications > IdpDemo Entra Native > Users and groups`

Without an app role assignment, the ID token will not contain the `roles` value for the Zentitle2 entitlement group.

## Step 5: Build and Run the App

Rebuild the app after the script finishes. The MAUI project packages `appsettings.Development.json` during build, so configuration changes are not picked up until you rebuild.

Example build command:

```bash
dotnet build IdpDemo.sln
```

Then run the sample from your preferred MAUI development environment or target platform.

## Expected Result

When setup is complete:

1. Click `Login` in the app.
2. Sign in with an Entra user that has the required app role and a matching Zentitle2 OpenID Token account.
3. The app should display:
   - authenticated user details
   - the token claims
   - the Entra `roles` claim
   - the Zentitle2 activation summary

## Troubleshooting

### The app says Entra ID is not configured

Run the setup script, verify `appsettings.Development.json`, and rebuild the app.

### Activation fails after login

Check these items:

- the Entra user or group is assigned to the app role
- the ID token contains `email`, `oid`, and the expected `roles` value
- the Entra user has an email/mail value; Entra cannot emit an `email` claim for users without one
- the Zentitle2 user is configured with authentication type `OpenID Token`
- the Zentitle2 authentication claim value matches the Entra user's `oid`
- the Zentitle2 user is assigned to the entitlement
- `Zentitle2.TenantId`, `LicensingApiUrl`, and `ProductId` are correct

## Notes for Adapting This Sample

- Android redirect handling uses a generated `msal<ClientId>://auth` scheme. Run the setup script before building for Android.
- iOS redirect handling uses `msauth.com.companyname.idpdemo://auth`, matching the current MAUI application ID. Mac Catalyst uses `http://localhost`.
- This sample focuses on the Entra ID + Zentitle2 integration path. It does not automate user creation in Zentitle2 or business-specific entitlement assignment workflows.
