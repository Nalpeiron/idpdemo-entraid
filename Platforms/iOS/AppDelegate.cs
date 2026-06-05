using Foundation;
using Microsoft.Identity.Client;
using UIKit;

namespace IdpDemo;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
	{
		return AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(url);
	}
}
