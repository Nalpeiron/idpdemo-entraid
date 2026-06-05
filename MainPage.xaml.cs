using System.Security.Claims;
using System.Text.Json;
using IdpDemo.Configuration;
using IdpDemo.Services;
using Microsoft.Identity.Client;

namespace IdpDemo;

public partial class MainPage : ContentPage
{
	private readonly IPublicClientApplication _msalClient;
	private readonly AppSettings _appSettings;
	private readonly ZentitleActivationService _zentitleActivationService;
	private string? _accessToken;
	private string? _idToken;
	private bool _isAuthenticated;

	public MainPage(
		IPublicClientApplication msalClient,
		AppSettings appSettings,
		ZentitleActivationService zentitleActivationService)
	{
		_msalClient = msalClient;
		_appSettings = appSettings;
		_zentitleActivationService = zentitleActivationService;
		InitializeComponent();
		ResetActivationSummary();
		UpdateLoginButtonState();
	}

	private async void OnLoginClicked(object? sender, EventArgs e)
	{
		var activationStarted = false;
		string? activationToken = null;

		if (_isAuthenticated)
		{
			await LogoutAsync();
			return;
		}

		if (!_appSettings.Entra.IsConfigured())
		{
			await DisplayAlertAsync(
				"Entra ID Not Configured",
				"Run scripts/setup-entra.sh, then rebuild the app before attempting to log in.",
				"OK");
			return;
		}

		SetBusyState(true);

		try
		{
			var authenticationResult = await AcquireTokenAsync();

			activationToken = authenticationResult.IdToken;
			activationStarted = true;
			UpdateAuthenticatedIdentity(authenticationResult.ClaimsPrincipal, authenticationResult.AccessToken, authenticationResult.IdToken);

			var activationSummary = await ActivateSeatAsync(authenticationResult);
			ShowActivationSummary(activationSummary);
		}
		catch (Exception ex)
		{
			if (activationStarted)
			{
				ShowActivationFailure(ex.Message, activationToken);
			}
			else
			{
				ClearAuthenticatedIdentity();
			}

			await DisplayAlertAsync("Login Failed", ex.Message, "OK");
		}
		finally
		{
			SetBusyState(false);
		}
	}

	private async Task LogoutAsync()
	{
		SetBusyState(true);

		try
		{
			if (_isAuthenticated)
			{
				await _zentitleActivationService.DeactivateAsync();
			}

			var accounts = await _msalClient.GetAccountsAsync();
			foreach (var account in accounts)
			{
				await _msalClient.RemoveAsync(account);
			}

			ClearAuthenticatedIdentity();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Logout Failed", ex.Message, "OK");
		}
		finally
		{
			SetBusyState(false);
		}
	}

	private void SetBusyState(bool isBusy)
	{
		LoginButton.IsEnabled = !isBusy;
		LoginActivityIndicator.IsVisible = isBusy;
		LoginActivityIndicator.IsRunning = isBusy;
	}

	private void UpdateAuthenticatedIdentity(ClaimsPrincipal user, string? accessToken = null, string? identityToken = null)
	{
		var name = FindJwtClaim(accessToken, "name")
			?? FindJwtClaim(identityToken, "name")
			?? FindClaim(user, "name")
			?? FindClaim(user, "nickname")
			?? "Unavailable";
		var email = FindJwtClaim(accessToken, "email")
			?? FindJwtClaim(identityToken, "email")
			?? FindClaim(user, "email")
			?? "Unavailable";
		var subject = FindJwtClaim(accessToken, _appSettings.Zentitle2.AuthenticationClaim)
			?? FindJwtClaim(identityToken, _appSettings.Zentitle2.AuthenticationClaim)
			?? FindClaim(user, _appSettings.Zentitle2.AuthenticationClaim)
			?? "Unavailable";
		var issuer = FindJwtClaim(accessToken, "iss")
			?? FindJwtClaim(identityToken, "iss")
			?? FindClaim(user, "iss")
			?? "Unavailable";
		var entitlementGroupId = FindJwtClaim(accessToken, _appSettings.Zentitle2.EntitlementGroupIdClaim)
			?? FindClaim(user, _appSettings.Zentitle2.EntitlementGroupIdClaim)
			?? FindJwtClaim(identityToken, _appSettings.Zentitle2.EntitlementGroupIdClaim)
			?? "Missing";
		var roles = FindJwtClaim(accessToken, _appSettings.Entra.EntitlementGroupIdClaim)
			?? FindJwtClaim(identityToken, _appSettings.Entra.EntitlementGroupIdClaim)
			?? FindClaim(user, _appSettings.Entra.EntitlementGroupIdClaim)
			?? "No roles claim returned";

		UserNameLabel.Text = $"Name: {name}";
		UserEmailLabel.Text = $"Email: {email}";
		UserSubjectLabel.Text = $"{_appSettings.Zentitle2.AuthenticationClaim}: {subject}";
		UserIssuerLabel.Text = $"iss: {issuer}";
		EntitlementGroupLabel.Text = $"{_appSettings.Zentitle2.EntitlementGroupIdClaim}: {entitlementGroupId}";
		RolesLabel.Text = $"{_appSettings.Entra.EntitlementGroupIdClaim}: {roles}";
		_accessToken = accessToken;
		_idToken = identityToken;
		AccessTokenEditor.Text = accessToken ?? string.Empty;
		IdTokenEditor.Text = identityToken ?? string.Empty;
		CopyAccessTokenButton.IsEnabled = !string.IsNullOrWhiteSpace(accessToken);
		CopyIdTokenButton.IsEnabled = !string.IsNullOrWhiteSpace(identityToken);
		Grid.SetColumn(ActivationSummaryCard, 1);
		Grid.SetColumnSpan(ActivationSummaryCard, 1);
		IdentitySummaryCard.IsVisible = true;
		_isAuthenticated = true;
		UpdateLoginButtonState();
	}

	private void ClearAuthenticatedIdentity(bool clearActivationDetails = true)
	{
		UserNameLabel.Text = string.Empty;
		UserEmailLabel.Text = string.Empty;
		UserSubjectLabel.Text = string.Empty;
		UserIssuerLabel.Text = string.Empty;
		EntitlementGroupLabel.Text = string.Empty;
		RolesLabel.Text = string.Empty;
		_accessToken = null;
		_idToken = null;
		AccessTokenEditor.Text = string.Empty;
		IdTokenEditor.Text = string.Empty;
		CopyAccessTokenButton.IsEnabled = false;
		CopyIdTokenButton.IsEnabled = false;
		Grid.SetColumn(ActivationSummaryCard, 0);
		Grid.SetColumnSpan(ActivationSummaryCard, 2);
		if (clearActivationDetails)
		{
			ResetActivationSummary();
		}

		IdentitySummaryCard.IsVisible = false;
		ActivationSummaryCard.IsVisible = true;
		_isAuthenticated = false;
		UpdateLoginButtonState();
	}

	private void ResetActivationSummary()
	{
		ActivationDetailsLabel.Text = "Sign in to view the current activation details.";
		ActivationSummaryCard.IsVisible = true;
	}

	private void ShowActivationFailure(string errorMessage, string? activationToken)
	{
		var details = new List<string>
		{
			"Activation failed:",
			ValueOrUnavailable(errorMessage)
		};

		if (!string.IsNullOrWhiteSpace(activationToken))
		{
			details.Add($"OpenID Token:\n{activationToken}");
		}

		ActivationDetailsLabel.Text = string.Join("\n\n", details);
		ActivationSummaryCard.IsVisible = true;
	}

	private void ShowActivationSummary(ActivationSummary activationSummary)
	{
		ActivationDetailsLabel.Text = activationSummary.ToDisplayText();
		ActivationSummaryCard.IsVisible = true;
	}

	private void UpdateLoginButtonState()
	{
		LoginButton.Text = _isAuthenticated ? "Logout" : "Login";
		SemanticProperties.SetHint(LoginButton, _isAuthenticated ? "Logs the current user out" : "Opens the login flow");
	}

	private async void OnCopyAccessTokenClicked(object? sender, EventArgs e)
	{
		await CopyTokenAsync(_accessToken, "Access token");
	}

	private async void OnCopyIdTokenClicked(object? sender, EventArgs e)
	{
		await CopyTokenAsync(_idToken, "ID token");
	}

	private async Task CopyTokenAsync(string? token, string label)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			await DisplayAlertAsync("Nothing To Copy", $"{label} is unavailable.", "OK");
			return;
		}

		await Clipboard.Default.SetTextAsync(token);
	}

	private async Task<ActivationSummary> ActivateSeatAsync(AuthenticationResult authenticationResult)
	{
		if (string.IsNullOrWhiteSpace(authenticationResult.IdToken))
		{
			throw new InvalidOperationException("Entra ID did not return an identity token required for Zentitle2 activation.");
		}

		ValidateTokenForActivation(authenticationResult.IdToken);

		var seatName = FindClaim(authenticationResult.ClaimsPrincipal, "preferred_username")
			?? FindClaim(authenticationResult.ClaimsPrincipal, "email")
			?? FindClaim(authenticationResult.ClaimsPrincipal, "name")
			?? FindClaim(authenticationResult.ClaimsPrincipal, "oid")
			?? "IdpDemo Seat";

		return await _zentitleActivationService.ActivateAsync(authenticationResult.IdToken, seatName);
	}

	private async Task<AuthenticationResult> AcquireTokenAsync()
	{
		var accounts = await _msalClient.GetAccountsAsync();
		var account = accounts.FirstOrDefault();

		if (account is not null)
		{
			try
			{
				return await _msalClient
					.AcquireTokenSilent(_appSettings.Entra.Scopes, account)
					.ExecuteAsync();
			}
			catch (MsalUiRequiredException)
			{
			}
		}

		var interactiveBuilder = _msalClient.AcquireTokenInteractive(_appSettings.Entra.Scopes);

#if ANDROID
		interactiveBuilder = interactiveBuilder.WithParentActivityOrWindow(Platform.CurrentActivity);
#elif IOS || MACCATALYST
		interactiveBuilder = interactiveBuilder.WithParentActivityOrWindow(Platform.GetCurrentUIViewController());
#endif

		return await interactiveBuilder.ExecuteAsync();
	}

	private void ValidateTokenForActivation(string token)
	{
		if (!JwtClaimHasAnyValue(token, _appSettings.Zentitle2.UsernameClaim))
		{
			throw new InvalidOperationException(
				$"Entra ID token is missing required claim '{_appSettings.Zentitle2.UsernameClaim}'. Rerun scripts/setup-entra.sh to configure the email optional claim, and confirm the Entra user has an email/mail value.");
		}
	}

	private static string? FindClaim(ClaimsPrincipal user, string claimType)
	{
		return user.FindFirst(claimType)?.Value;
	}

	private static string ValueOrUnavailable(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
	}

	private static string? FindJwtClaim(string? token, string claimName)
	{
		var claimValues = FindJwtClaimValues(token, claimName);
		return claimValues.Count switch
		{
			0 => null,
			1 => claimValues[0],
			_ => string.Join(", ", claimValues)
		};
	}

	private static bool JwtClaimHasAnyValue(string? token, string claimName)
	{
		return FindJwtClaimValues(token, claimName).Any(value => !string.IsNullOrWhiteSpace(value));
	}

	private static IReadOnlyList<string> FindJwtClaimValues(string? token, string claimName)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return Array.Empty<string>();
		}

		var parts = token.Split('.');
		if (parts.Length < 2)
		{
			return Array.Empty<string>();
		}

		try
		{
			var payloadBytes = DecodeBase64Url(parts[1]);
			using var document = JsonDocument.Parse(payloadBytes);

			if (!document.RootElement.TryGetProperty(claimName, out var claimValue))
			{
				return Array.Empty<string>();
			}

			return claimValue.ValueKind switch
			{
				JsonValueKind.Array => claimValue
					.EnumerateArray()
					.Select(FormatJwtClaimValue)
					.Where(value => !string.IsNullOrWhiteSpace(value))
					.Select(value => value!)
					.ToArray(),
				_ => FormatJwtClaimValue(claimValue) is { } value
					? new[] { value }
					: Array.Empty<string>()
			};
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static string? FormatJwtClaimValue(JsonElement claimValue)
	{
		return claimValue.ValueKind switch
		{
			JsonValueKind.Null => null,
			JsonValueKind.Undefined => null,
			JsonValueKind.String => claimValue.GetString(),
			_ => claimValue.ToString()
		};
	}

	private static byte[] DecodeBase64Url(string value)
	{
		var normalized = value.Replace('-', '+').Replace('_', '/');
		var padding = normalized.Length % 4;

		if (padding > 0)
		{
			normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
		}

		return Convert.FromBase64String(normalized);
	}
}
