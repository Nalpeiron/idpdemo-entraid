using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Identity.Client;

namespace IdpDemo;

[Activity(Exported = true, LaunchMode = LaunchMode.SingleTask, NoHistory = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
[IntentFilter(
	new[] { Intent.ActionView },
	Categories = new[] { Intent.CategoryBrowsable, Intent.CategoryDefault },
	DataHost = "auth",
	DataScheme = AndroidRedirectScheme)]
public class MsalActivity : BrowserTabActivity
{
	private const string AndroidRedirectScheme = "msal9add5b1e-8a6b-4d73-bb37-ffb3e4279c9f";
}
