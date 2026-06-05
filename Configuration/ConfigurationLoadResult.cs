namespace IdpDemo.Configuration;

public sealed class ConfigurationLoadResult
{
	public AppSettings? Settings { get; init; }

	public string? ErrorMessage { get; init; }

	public bool IsSuccess => Settings is not null && string.IsNullOrWhiteSpace(ErrorMessage);
}
