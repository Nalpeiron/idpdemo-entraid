namespace IdpDemo.Configuration;

public sealed class AppSettings
{
	public EntraSettings Entra { get; init; } = new();

	public Zentitle2Settings Zentitle2 { get; init; } = new();
}
