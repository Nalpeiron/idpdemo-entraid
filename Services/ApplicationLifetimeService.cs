namespace IdpDemo.Services;

public sealed class ApplicationLifetimeService
{
	public void Quit()
	{
		Application.Current?.Quit();
	}
}
