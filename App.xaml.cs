namespace IdpDemo;

public partial class App : Application
{
	private readonly Page _startupPage;

	public App(IServiceProvider serviceProvider, Configuration.ConfigurationLoadResult configurationLoadResult)
	{
		InitializeComponent();

		_startupPage = CreateStartupPage(serviceProvider, configurationLoadResult);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_startupPage)
		{
			Title = "IdpDemo"
		};
	}

	private static Page CreateStartupPage(IServiceProvider serviceProvider, Configuration.ConfigurationLoadResult configurationLoadResult)
	{
		if (configurationLoadResult.IsSuccess)
		{
			return serviceProvider.GetRequiredService<MainPage>();
		}

		var errorMessage = configurationLoadResult.ErrorMessage ?? "Unknown configuration error.";
		Console.Error.WriteLine($"[startup] {errorMessage}");

		var errorPage = serviceProvider.GetRequiredService<ErrorPage>();
		errorPage.SetError(errorMessage);
		return errorPage;
	}
}
