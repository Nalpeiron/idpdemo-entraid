using IdpDemo.Services;

namespace IdpDemo;

public partial class ErrorPage : ContentPage
{
	private readonly ApplicationLifetimeService _applicationLifetimeService;

	public ErrorPage(ApplicationLifetimeService applicationLifetimeService)
	{
		_applicationLifetimeService = applicationLifetimeService;
		InitializeComponent();
	}

	public void SetError(string errorMessage)
	{
		ErrorMessageLabel.Text = errorMessage;
	}

	private void OnCloseClicked(object? sender, EventArgs e)
	{
		_applicationLifetimeService.Quit();
	}
}
