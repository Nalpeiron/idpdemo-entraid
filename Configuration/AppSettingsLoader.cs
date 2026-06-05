using System.Reflection;
using System.Text.Json;

namespace IdpDemo.Configuration;

public static class AppSettingsLoader
{
	private const string TemplateResourceName = "IdpDemo.appsettings.json";
	private const string DevelopmentSettingsFileName = "appsettings.Development.json";

	public static ConfigurationLoadResult Load()
	{
		try
		{
			var settings = TryLoadDevelopmentSettings();
			if (settings is null)
			{
				var assembly = Assembly.GetExecutingAssembly();
				using var stream = assembly.GetManifestResourceStream(TemplateResourceName);
				if (stream is null)
				{
					return new ConfigurationLoadResult
					{
						ErrorMessage = $"Embedded configuration resource '{TemplateResourceName}' was not found."
					};
				}

				settings = JsonSerializer.Deserialize<AppSettings>(stream, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
			}

			if (settings is null)
			{
				return new ConfigurationLoadResult
				{
					ErrorMessage = "The application settings file is empty or invalid."
				};
			}

			var validationError = AppSettingsValidator.Validate(settings);
			if (!string.IsNullOrWhiteSpace(validationError))
			{
				return new ConfigurationLoadResult
				{
					ErrorMessage = validationError
				};
			}

			return new ConfigurationLoadResult
			{
				Settings = settings
			};
		}
		catch (JsonException ex)
		{
			return new ConfigurationLoadResult
			{
				ErrorMessage = $"The application settings JSON is invalid: {ex.Message}"
			};
		}
		catch (Exception ex)
		{
			return new ConfigurationLoadResult
			{
				ErrorMessage = $"Failed to load application settings: {ex.Message}"
			};
		}
	}

	private static AppSettings? TryLoadDevelopmentSettings()
	{
		using var stream = TryOpenDevelopmentSettingsStream();
		if (stream is null)
		{
			return null;
		}

		return JsonSerializer.Deserialize<AppSettings>(stream, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});
	}

	private static Stream? TryOpenDevelopmentSettingsStream()
	{
		try
		{
			return FileSystem.Current.OpenAppPackageFileAsync(DevelopmentSettingsFileName).GetAwaiter().GetResult();
		}
		catch (FileNotFoundException)
		{
			var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, DevelopmentSettingsFileName);
			return File.Exists(baseDirectoryPath) ? File.OpenRead(baseDirectoryPath) : null;
		}
	}
}
