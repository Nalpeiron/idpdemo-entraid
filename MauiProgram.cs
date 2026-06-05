using IdpDemo.Configuration;
using IdpDemo.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace IdpDemo;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		var configurationResult = AppSettingsLoader.Load();

		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton(configurationResult);
		builder.Services.AddSingleton(sp =>
		{
			var loadResult = sp.GetRequiredService<ConfigurationLoadResult>();
			return loadResult.Settings ?? throw new InvalidOperationException(loadResult.ErrorMessage ?? "Application settings are unavailable.");
		});
		builder.Services.AddSingleton<ApplicationLifetimeService>();
		builder.Services.AddSingleton<ErrorPage>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<ZentitleActivationService>();

		builder.Services.AddSingleton(sp =>
		{
			var loadResult = sp.GetRequiredService<ConfigurationLoadResult>();
			if (!loadResult.IsSuccess || loadResult.Settings is null)
			{
				throw new InvalidOperationException(loadResult.ErrorMessage ?? "Entra configuration is unavailable.");
			}

			var settings = loadResult.Settings.Entra;
			return PublicClientApplicationBuilder
				.Create(settings.ClientId)
				.WithAuthority($"{settings.Instance.TrimEnd('/')}/{settings.TenantId}")
				.WithRedirectUri(GetRedirectUri(settings.ClientId))
				.WithLogging((level, message, containsPii) =>
				{
					if (!containsPii)
					{
						sp.GetRequiredService<ILoggerFactory>()
							.CreateLogger("MSAL")
							.Log(MapLogLevel(level), message);
					}
				})
				.Build();
		});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	private static string GetRedirectUri(string clientId)
	{
#if ANDROID
		return $"msal{clientId}://auth";
#elif IOS
		return $"msauth.{AppInfo.PackageName}://auth";
#else
		return "http://localhost";
#endif
	}

	private static Microsoft.Extensions.Logging.LogLevel MapLogLevel(Microsoft.Identity.Client.LogLevel logLevel)
	{
		return logLevel switch
		{
			Microsoft.Identity.Client.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
			Microsoft.Identity.Client.LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
			Microsoft.Identity.Client.LogLevel.Info => Microsoft.Extensions.Logging.LogLevel.Information,
			_ => Microsoft.Extensions.Logging.LogLevel.Debug
		};
	}
}
