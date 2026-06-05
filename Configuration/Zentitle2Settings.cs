namespace IdpDemo.Configuration;

public sealed class Zentitle2Settings
{
	public string TenantId { get; init; } = string.Empty;

	public string LicensingApiUrl { get; init; } = string.Empty;

	public string ProductId { get; init; } = string.Empty;

	public string IdpUrl { get; init; } = "https://login.microsoftonline.com/your-entra-tenant-id/v2.0";

	public string UsernameClaim { get; init; } = "email";

	public string AuthenticationClaim { get; init; } = "oid";

	public string EntitlementGroupIdClaim { get; init; } = "roles";
}
