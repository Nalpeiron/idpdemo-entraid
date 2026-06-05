namespace IdpDemo.Configuration;

public sealed class EntraSettings
{
	public string TenantId { get; init; } = string.Empty;

	public string ClientId { get; init; } = string.Empty;

	public string Instance { get; init; } = "https://login.microsoftonline.com";

	public string[] Scopes { get; init; } = ["User.Read"];

	public string EntitlementGroupIdClaim { get; init; } = "roles";

	public bool IsConfigured() =>
		!string.IsNullOrWhiteSpace(TenantId) &&
		!string.IsNullOrWhiteSpace(ClientId) &&
		!TenantId.Contains("your-entra-tenant-id", StringComparison.OrdinalIgnoreCase) &&
		!ClientId.Contains("your-entra-client-id", StringComparison.OrdinalIgnoreCase);
}
