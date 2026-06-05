namespace IdpDemo.Configuration;

public static class AppSettingsValidator
{
	public static string? Validate(AppSettings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Entra.TenantId) ||
			settings.Entra.TenantId.Contains("your-entra-tenant-id", StringComparison.OrdinalIgnoreCase))
		{
			return "Entra.TenantId is missing or still uses the placeholder value.";
		}

		if (string.IsNullOrWhiteSpace(settings.Entra.ClientId) ||
			settings.Entra.ClientId.Contains("your-entra-client-id", StringComparison.OrdinalIgnoreCase))
		{
			return "Entra.ClientId is missing or still uses the placeholder value.";
		}

		if (settings.Entra.Scopes.Length == 0 || settings.Entra.Scopes.Any(string.IsNullOrWhiteSpace))
		{
			return "Entra.Scopes must contain at least one non-empty scope.";
		}

		if (string.IsNullOrWhiteSpace(settings.Zentitle2.IdpUrl) ||
			settings.Zentitle2.IdpUrl.Contains("your-entra-tenant-id", StringComparison.OrdinalIgnoreCase))
		{
			return "Zentitle2.IdpUrl is missing or still uses the placeholder value.";
		}

		if (string.IsNullOrWhiteSpace(settings.Zentitle2.TenantId))
		{
			return "Zentitle2.TenantId is missing.";
		}

		if (string.IsNullOrWhiteSpace(settings.Zentitle2.LicensingApiUrl))
		{
			return "Zentitle2.LicensingApiUrl is missing.";
		}

		if (string.IsNullOrWhiteSpace(settings.Zentitle2.ProductId))
		{
			return "Zentitle2.ProductId is missing.";
		}

		return null;
	}
}
