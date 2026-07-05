#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
APPSETTINGS_TEMPLATE_PATH="${APP_ROOT}/appsettings.json"
APPSETTINGS_PATH="${APP_ROOT}/appsettings.Development.json"
ANDROID_MSAL_ACTIVITY_PATH="${APP_ROOT}/Platforms/Android/MsalActivity.cs"

DEFAULT_INSTANCE="https://login.microsoftonline.com"
DEFAULT_SCOPES_JSON='["User.Read"]'
IOS_REDIRECT_URI="msauth.com.companyname.idpdemo://auth"
WINDOWS_REDIRECT_URI="http://localhost"
GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"
GRAPH_USER_READ_SCOPE_ID="e1fe6dd8-ba31-4d61-89e7-88639da4683d"

need_cmd() {
	if ! command -v "$1" >/dev/null 2>&1; then
		echo "Missing required command: $1" >&2
		exit 1
	fi
}

prompt() {
	local label="$1"
	local default_value="${2:-}"
	local value

	if [[ -n "${default_value}" ]]; then
		read -r -p "${label} [${default_value}]: " value
		echo "${value:-$default_value}"
	else
		read -r -p "${label}: " value
		echo "${value}"
	fi
}

prompt_required() {
	local label="$1"
	local value

	while true; do
		value="$(prompt "${label}")"
		if [[ -n "${value}" ]]; then
			echo "${value}"
			return 0
		fi

		echo "A value is required." >&2
	done
}

normalize_base_url() {
	local value="$1"
	value="${value%/}"
	if [[ "${value}" == */sso ]]; then
		value="${value%/sso}"
	fi
	echo "${value}"
}

new_uuid() {
	if command -v uuidgen >/dev/null 2>&1; then
		uuidgen | tr '[:upper:]' '[:lower:]'
	else
		python3 -c 'import uuid; print(uuid.uuid4())'
	fi
}

az_rest() {
	local method="$1"
	local url="$2"
	local body="${3:-}"

	if [[ -n "${body}" ]]; then
		az rest --method "${method}" --url "${url}" --headers "Content-Type=application/json" --body "${body}"
	else
		az rest --method "${method}" --url "${url}"
	fi
}

json_array_contains() {
	local json="$1"
	local value="$2"
	jq -e --arg value "${value}" 'index($value) != null' <<<"${json}" >/dev/null
}

need_cmd az
need_cmd jq
need_cmd perl

if [[ ! -f "${APPSETTINGS_TEMPLATE_PATH}" ]]; then
	echo "Expected appsettings.json template at ${APPSETTINGS_TEMPLATE_PATH}" >&2
	exit 1
fi

if [[ ! -f "${APPSETTINGS_PATH}" ]]; then
	cp "${APPSETTINGS_TEMPLATE_PATH}" "${APPSETTINGS_PATH}"
fi

if ! az account show >/dev/null 2>&1; then
	echo "Azure CLI is not logged in. Run 'az login' and then rerun this script." >&2
	exit 1
fi

CURRENT_TENANT_ID="$(jq -r '.Entra.TenantId // empty' "${APPSETTINGS_PATH}")"
CURRENT_PRODUCT_ID="$(jq -r '.Zentitle2.ProductId // empty' "${APPSETTINGS_PATH}")"

cat <<'EOF'
This script provisions the Microsoft Entra ID pieces required by the MAUI app and updates appsettings.Development.json.

It creates or updates:
  - a public-client app registration for native sign-in
  - a web-client app registration for EUP web login
  - MSAL redirect URIs for Android, iOS, and desktop/Mac Catalyst
  - the EUP /sso callback URL on the web-client app registration
  - a delegated Microsoft Graph User.Read permission
  - the email optional claim in ID tokens and access tokens
  - an app role whose value is the Zentitle2 entitlement group ID
  - optionally, a security group assigned to that app role

For Zentitle2, configure Account-Based Licensing with:
  Authority URL: https://login.microsoftonline.com/<tenant-id>/v2.0
  Expected Token Issuer: https://login.microsoftonline.com/<tenant-id>/v2.0
  User ID Claim: oid
  Entitlement Group IDs Claim: roles
EOF

echo

DEFAULT_TENANT_ID="$(az account show --query tenantId -o tsv)"
TENANT_ID="$(
	if [[ -n "${CURRENT_TENANT_ID}" && "${CURRENT_TENANT_ID}" != "your-entra-tenant-id" ]]; then
		prompt "Microsoft Entra tenant ID" "${CURRENT_TENANT_ID}"
	else
		prompt "Microsoft Entra tenant ID" "${DEFAULT_TENANT_ID}"
	fi
)"

if [[ "${TENANT_ID}" != "${DEFAULT_TENANT_ID}" ]]; then
	cat <<EOF >&2
The Azure CLI is currently authenticated against tenant ${DEFAULT_TENANT_ID}, but this script was told to use ${TENANT_ID}.
Run 'az login --tenant ${TENANT_ID}' or switch your Azure CLI context, then rerun this script.
EOF
	exit 1
fi

APPLICATION_NAME="$(prompt "Native application name" "IdpDemo Entra Native")"
WEB_APPLICATION_NAME="$(prompt "EUP web application name" "IdpDemo Entra EUP Web")"
EUP_URL="$(normalize_base_url "$(prompt_required "EUP URL (for example https://oriondev.eup.nalpeiron-dev.com:8443)")")"
EUP_CALLBACK_URL="${EUP_URL}/sso"
PRODUCT_ID="$(
	if [[ -n "${CURRENT_PRODUCT_ID}" ]]; then
		prompt "Zentitle2 product ID" "${CURRENT_PRODUCT_ID}"
	else
		prompt "Zentitle2 product ID"
	fi
)"
ENTITLEMENT_GROUP_ID="$(prompt "Zentitle2 entitlementGroupId / app role value")"
APP_ROLE_DISPLAY_NAME="$(prompt "App role display name" "Zentitle2 Entitlement ${ENTITLEMENT_GROUP_ID}")"
ASSIGN_GROUP="$(prompt "Create/assign a security group to this app role? (y/N)" "N")"

echo
echo "Looking for existing app registration..."

EXISTING_APP="$(
	az ad app list --display-name "${APPLICATION_NAME}" -o json \
	| jq -c --arg name "${APPLICATION_NAME}" '[.[] | select(.displayName == $name)] | first // empty'
)"

CREATE_PAYLOAD="$(
	jq -n \
		--arg display_name "${APPLICATION_NAME}" \
		--arg windows_redirect "${WINDOWS_REDIRECT_URI}" \
		--arg ios_redirect "${IOS_REDIRECT_URI}" \
		--arg graph_app_id "${GRAPH_APP_ID}" \
		--arg user_read_scope_id "${GRAPH_USER_READ_SCOPE_ID}" \
		'{
			displayName: $display_name,
			signInAudience: "AzureADMyOrg",
			publicClient: {
				redirectUris: [$windows_redirect, $ios_redirect]
			},
			requiredResourceAccess: [
				{
					resourceAppId: $graph_app_id,
					resourceAccess: [
						{
							id: $user_read_scope_id,
							type: "Scope"
						}
					]
				}
			],
			optionalClaims: {
				idToken: [
					{
						name: "email",
						source: null,
						essential: false,
						additionalProperties: []
					}
				],
				accessToken: [
					{
						name: "email",
						source: null,
						essential: false,
						additionalProperties: []
					}
				]
			}
		}'
)"

if [[ -z "${EXISTING_APP}" || "${EXISTING_APP}" == "null" ]]; then
	APP_JSON="$(az_rest POST "https://graph.microsoft.com/v1.0/applications" "${CREATE_PAYLOAD}")"
else
	APP_JSON="${EXISTING_APP}"
fi

APPLICATION_OBJECT_ID="$(jq -r '.id' <<<"${APP_JSON}")"
CLIENT_ID="$(jq -r '.appId' <<<"${APP_JSON}")"
ANDROID_REDIRECT_URI="msal${CLIENT_ID}://auth"

echo "Ensuring redirect URIs and app role are configured..."

APP_JSON="$(az_rest GET "https://graph.microsoft.com/v1.0/applications/${APPLICATION_OBJECT_ID}")"
EXISTING_REDIRECTS="$(jq -c '.publicClient.redirectUris // []' <<<"${APP_JSON}")"
REDIRECTS="${EXISTING_REDIRECTS}"

for redirect_uri in "${WINDOWS_REDIRECT_URI}" "${IOS_REDIRECT_URI}" "${ANDROID_REDIRECT_URI}"; do
	if ! json_array_contains "${REDIRECTS}" "${redirect_uri}"; then
		REDIRECTS="$(jq -c --arg value "${redirect_uri}" '. + [$value]' <<<"${REDIRECTS}")"
	fi
done

EXISTING_APP_ROLES="$(jq -c '.appRoles // []' <<<"${APP_JSON}")"
EXISTING_REQUIRED_RESOURCE_ACCESS="$(jq -c '.requiredResourceAccess // []' <<<"${APP_JSON}")"
EXISTING_OPTIONAL_CLAIMS="$(jq -c '.optionalClaims // {}' <<<"${APP_JSON}")"
APP_ROLE_ID="$(jq -r --arg value "${ENTITLEMENT_GROUP_ID}" '.[] | select(.value == $value) | .id' <<<"${EXISTING_APP_ROLES}" | head -n 1)"

if [[ -z "${APP_ROLE_ID}" ]]; then
	APP_ROLE_ID="$(new_uuid)"
	APP_ROLES="$(
		jq -c \
			--arg id "${APP_ROLE_ID}" \
			--arg display_name "${APP_ROLE_DISPLAY_NAME}" \
			--arg value "${ENTITLEMENT_GROUP_ID}" \
			'. + [{
				allowedMemberTypes: ["User"],
				description: ("Grants Zentitle2 entitlement group " + $value),
				displayName: $display_name,
				id: $id,
				isEnabled: true,
				value: $value
			}]' <<<"${EXISTING_APP_ROLES}"
	)"
else
	APP_ROLES="${EXISTING_APP_ROLES}"
fi

REQUIRED_RESOURCE_ACCESS="$(
	jq -c \
		--arg graph_app_id "${GRAPH_APP_ID}" \
		--arg user_read_scope_id "${GRAPH_USER_READ_SCOPE_ID}" \
		'
		def has_user_read:
			any(.resourceAccess[]?; .id == $user_read_scope_id and .type == "Scope");

		if any(.[]?; .resourceAppId == $graph_app_id) then
			map(
				if .resourceAppId == $graph_app_id then
					if has_user_read then
						.
					else
						.resourceAccess += [{ id: $user_read_scope_id, type: "Scope" }]
					end
				else
					.
				end
			)
		else
			. + [{
				resourceAppId: $graph_app_id,
				resourceAccess: [
					{
						id: $user_read_scope_id,
						type: "Scope"
					}
				]
			}]
		end
		' <<<"${EXISTING_REQUIRED_RESOURCE_ACCESS}"
)"

OPTIONAL_CLAIMS="$(
	jq -c '
		def email_claim:
			{
				name: "email",
				source: null,
				essential: false,
				additionalProperties: []
			};
		def ensure_email_claim:
			if any(.[]?; .name == "email") then
				.
			else
				. + [email_claim]
			end;

		.idToken = ((.idToken // []) | ensure_email_claim) |
		.accessToken = ((.accessToken // []) | ensure_email_claim)
	' <<<"${EXISTING_OPTIONAL_CLAIMS}"
)"

PATCH_PAYLOAD="$(
	jq -n \
		--argjson redirects "${REDIRECTS}" \
		--argjson app_roles "${APP_ROLES}" \
		--argjson required_resource_access "${REQUIRED_RESOURCE_ACCESS}" \
		--argjson optional_claims "${OPTIONAL_CLAIMS}" \
		'{
			publicClient: {
				redirectUris: $redirects
			},
			requiredResourceAccess: $required_resource_access,
			optionalClaims: $optional_claims,
			appRoles: $app_roles
		}'
)"

az_rest PATCH "https://graph.microsoft.com/v1.0/applications/${APPLICATION_OBJECT_ID}" "${PATCH_PAYLOAD}" >/dev/null

echo "Ensuring EUP web application client exists..."

EXISTING_WEB_APP="$(
	az ad app list --display-name "${WEB_APPLICATION_NAME}" -o json \
	| jq -c --arg name "${WEB_APPLICATION_NAME}" '[.[] | select(.displayName == $name)] | first // empty'
)"

WEB_CREATE_PAYLOAD="$(
	jq -n \
		--arg display_name "${WEB_APPLICATION_NAME}" \
		--arg redirect_uri "${EUP_CALLBACK_URL}" \
		'{
			displayName: $display_name,
			signInAudience: "AzureADMyOrg",
				web: {
					redirectUris: [$redirect_uri],
					implicitGrantSettings: {
						enableAccessTokenIssuance: true,
						enableIdTokenIssuance: true
					}
				},
			optionalClaims: {
				idToken: [
					{
						name: "email",
						source: null,
						essential: false,
						additionalProperties: []
					}
				],
				accessToken: [
					{
						name: "email",
						source: null,
						essential: false,
						additionalProperties: []
					}
				]
			}
		}'
)"

if [[ -z "${EXISTING_WEB_APP}" || "${EXISTING_WEB_APP}" == "null" ]]; then
	WEB_APP_JSON="$(az_rest POST "https://graph.microsoft.com/v1.0/applications" "${WEB_CREATE_PAYLOAD}")"
else
	WEB_APP_JSON="${EXISTING_WEB_APP}"
fi

WEB_APPLICATION_OBJECT_ID="$(jq -r '.id' <<<"${WEB_APP_JSON}")"
WEB_CLIENT_ID="$(jq -r '.appId' <<<"${WEB_APP_JSON}")"
WEB_APPLICATION_ID_URI="api://${WEB_CLIENT_ID}"
WEB_ACCESS_SCOPE_VALUE="user_impersonation"
WEB_ACCESS_SCOPE="${WEB_APPLICATION_ID_URI}/${WEB_ACCESS_SCOPE_VALUE}"

WEB_APP_JSON="$(az_rest GET "https://graph.microsoft.com/v1.0/applications/${WEB_APPLICATION_OBJECT_ID}")"
EXISTING_WEB_REDIRECTS="$(jq -c '.web.redirectUris // []' <<<"${WEB_APP_JSON}")"
WEB_REDIRECTS="${EXISTING_WEB_REDIRECTS}"

if ! json_array_contains "${WEB_REDIRECTS}" "${EUP_CALLBACK_URL}"; then
	WEB_REDIRECTS="$(jq -c --arg value "${EUP_CALLBACK_URL}" '. + [$value]' <<<"${WEB_REDIRECTS}")"
fi

EXISTING_WEB_IDENTIFIER_URIS="$(jq -c '.identifierUris // []' <<<"${WEB_APP_JSON}")"
WEB_IDENTIFIER_URIS="${EXISTING_WEB_IDENTIFIER_URIS}"

if ! json_array_contains "${WEB_IDENTIFIER_URIS}" "${WEB_APPLICATION_ID_URI}"; then
	WEB_IDENTIFIER_URIS="$(jq -c --arg value "${WEB_APPLICATION_ID_URI}" '. + [$value]' <<<"${WEB_IDENTIFIER_URIS}")"
fi

EXISTING_WEB_OPTIONAL_CLAIMS="$(jq -c '.optionalClaims // {}' <<<"${WEB_APP_JSON}")"
WEB_OPTIONAL_CLAIMS="$(
	jq -c '
		def email_claim:
			{
				name: "email",
				source: null,
				essential: false,
				additionalProperties: []
			};
		def ensure_email_claim:
			if any(.[]?; .name == "email") then
				.
			else
				. + [email_claim]
			end;

		.idToken = ((.idToken // []) | ensure_email_claim) |
		.accessToken = ((.accessToken // []) | ensure_email_claim)
	' <<<"${EXISTING_WEB_OPTIONAL_CLAIMS}"
)"

EXISTING_WEB_API="$(jq -c '.api // {}' <<<"${WEB_APP_JSON}")"
WEB_API="$(
	jq -c \
		--arg scope_id "$(new_uuid)" \
		--arg scope_value "${WEB_ACCESS_SCOPE_VALUE}" \
		'
		def eup_scope($id; $value):
			{
				adminConsentDescription: "Allow EUP to sign in users with this application.",
				adminConsentDisplayName: "Sign in to EUP",
				id: $id,
				isEnabled: true,
				type: "User",
				userConsentDescription: "Allow EUP to sign you in with this application.",
				userConsentDisplayName: "Sign in to EUP",
				value: $value
			};

		.requestedAccessTokenVersion = 2 |
		.oauth2PermissionScopes = (
			if any(.oauth2PermissionScopes[]?; .value == $scope_value) then
				[
					.oauth2PermissionScopes[] |
					if .value == $scope_value then
						eup_scope(.id; $scope_value)
					else
						.
					end
				]
			else
				((.oauth2PermissionScopes // []) + [eup_scope($scope_id; $scope_value)])
			end
		)
		' <<<"${EXISTING_WEB_API}"
)"

WEB_PATCH_PAYLOAD="$(
	jq -n \
		--argjson redirects "${WEB_REDIRECTS}" \
		--argjson identifier_uris "${WEB_IDENTIFIER_URIS}" \
		--argjson optional_claims "${WEB_OPTIONAL_CLAIMS}" \
		--argjson api "${WEB_API}" \
		'{
			identifierUris: $identifier_uris,
				web: {
					redirectUris: $redirects,
					implicitGrantSettings: {
						enableAccessTokenIssuance: true,
						enableIdTokenIssuance: true
					}
				},
			optionalClaims: $optional_claims,
			api: $api
		}'
)"

az_rest PATCH "https://graph.microsoft.com/v1.0/applications/${WEB_APPLICATION_OBJECT_ID}" "${WEB_PATCH_PAYLOAD}" >/dev/null

echo "Ensuring enterprise application service principal exists..."

SERVICE_PRINCIPAL="$(
	az ad sp list --filter "appId eq '${CLIENT_ID}'" -o json \
	| jq -c '.[0] // empty'
)"

if [[ -z "${SERVICE_PRINCIPAL}" || "${SERVICE_PRINCIPAL}" == "null" ]]; then
	SERVICE_PRINCIPAL="$(az ad sp create --id "${CLIENT_ID}" -o json)"
fi

SERVICE_PRINCIPAL_ID="$(jq -r '.id' <<<"${SERVICE_PRINCIPAL}")"

if [[ "${ASSIGN_GROUP}" =~ ^[Yy]$ ]]; then
	GROUP_NAME="$(prompt "Security group name" "IdpDemo Zentitle2 ${ENTITLEMENT_GROUP_ID}")"
	EXISTING_GROUP="$(
		az ad group list --display-name "${GROUP_NAME}" -o json \
		| jq -c --arg name "${GROUP_NAME}" '[.[] | select(.displayName == $name)] | first // empty'
	)"

	if [[ -z "${EXISTING_GROUP}" || "${EXISTING_GROUP}" == "null" ]]; then
		MAIL_NICKNAME="$(tr -cd '[:alnum:]' <<<"${GROUP_NAME}" | tr '[:upper:]' '[:lower:]')"
		EXISTING_GROUP="$(az ad group create --display-name "${GROUP_NAME}" --mail-nickname "${MAIL_NICKNAME:-idpdemozentitle2}" -o json)"
	fi

	GROUP_ID="$(jq -r '.id' <<<"${EXISTING_GROUP}")"
	EXISTING_ASSIGNMENT="$(
		az_rest GET "https://graph.microsoft.com/v1.0/servicePrincipals/${SERVICE_PRINCIPAL_ID}/appRoleAssignedTo" \
		| jq -c --arg principal_id "${GROUP_ID}" --arg app_role_id "${APP_ROLE_ID}" '
			.value[]? | select(.principalId == $principal_id and .appRoleId == $app_role_id) | .id
		' | head -n 1
	)"

	if [[ -z "${EXISTING_ASSIGNMENT}" ]]; then
		az_rest POST "https://graph.microsoft.com/v1.0/servicePrincipals/${SERVICE_PRINCIPAL_ID}/appRoleAssignedTo" "$(
			jq -n \
				--arg principal_id "${GROUP_ID}" \
				--arg resource_id "${SERVICE_PRINCIPAL_ID}" \
				--arg app_role_id "${APP_ROLE_ID}" \
				'{
					principalId: $principal_id,
					resourceId: $resource_id,
					appRoleId: $app_role_id
				}'
		)" >/dev/null
	fi
fi

echo "Updating local app settings and Android redirect scheme..."

IDP_URL="${DEFAULT_INSTANCE}/${TENANT_ID}/v2.0"
EXPECTED_TOKEN_ISSUER="${IDP_URL}"

tmp_file="$(mktemp)"
jq \
	--arg tenant_id "${TENANT_ID}" \
	--arg client_id "${CLIENT_ID}" \
	--arg instance "${DEFAULT_INSTANCE}" \
	--argjson scopes "${DEFAULT_SCOPES_JSON}" \
	--arg idp_url "${IDP_URL}" \
	--arg product_id "${PRODUCT_ID}" \
	'
	.Entra.TenantId = $tenant_id |
	.Entra.ClientId = $client_id |
	.Entra.Instance = $instance |
	.Entra.Scopes = $scopes |
	.Entra.EntitlementGroupIdClaim = "roles" |
	.Zentitle2.IdpUrl = $idp_url |
	.Zentitle2.ProductId = $product_id |
	.Zentitle2.UsernameClaim = "email" |
	.Zentitle2.AuthenticationClaim = "oid" |
	.Zentitle2.EntitlementGroupIdClaim = "roles"
	' "${APPSETTINGS_PATH}" > "${tmp_file}"
mv "${tmp_file}" "${APPSETTINGS_PATH}"

perl -0pi -e "s/msal(?:your-entra-client-id|[0-9a-fA-F-]{36})/msal${CLIENT_ID}/g" "${ANDROID_MSAL_ACTIVITY_PATH}"

cat <<EOF

Entra ID provisioning complete.

Created or updated:
  Native app registration: ${APPLICATION_NAME}
  Native app client ID: ${CLIENT_ID}
  EUP web app registration: ${WEB_APPLICATION_NAME}
  EUP web app client ID: ${WEB_CLIENT_ID}
  EUP callback URL: ${EUP_CALLBACK_URL}
  EUP access token audience: ${WEB_APPLICATION_ID_URI}
  EUP access token scope: ${WEB_ACCESS_SCOPE}
  Enterprise application object ID: ${SERVICE_PRINCIPAL_ID}
  App role value: ${ENTITLEMENT_GROUP_ID}
  Optional claims: email in ID token and access token

Redirect URIs:
  Android: ${ANDROID_REDIRECT_URI}
  iOS: ${IOS_REDIRECT_URI}
  Desktop/Mac Catalyst: ${WINDOWS_REDIRECT_URI}

Zentitle2 values to enter in Administration > Configuration > Account-Based Licensing:
  Authority URL: ${IDP_URL}
  Expected Token Issuer: ${EXPECTED_TOKEN_ISSUER}
  User ID Claim: oid
  Entitlement Group IDs Claim: roles

Next steps:
  1. Assign users or groups to app role "${APP_ROLE_DISPLAY_NAME}" in the Enterprise Application if you did not let this script create a group assignment.
  2. Add each user's Entra object ID (oid claim) as the OpenID Token authentication claim value in Zentitle2.
  3. Configure EUP with Entra client ID ${WEB_CLIENT_ID}.
  4. Configure EUP to use authority ${IDP_URL} and scope "openid profile email ${WEB_ACCESS_SCOPE}".
  5. Rebuild and run the MAUI app so the packaged appsettings.Development.json and Android redirect scheme are refreshed.
EOF
