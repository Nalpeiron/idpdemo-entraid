using System.Text;
using IdpDemo.Configuration;
using Microsoft.Extensions.Logging;
using Zentitle.Licensing.Client;
using Zentitle.Licensing.Client.Persistence.Storage;

namespace IdpDemo.Services;

public sealed class ZentitleActivationService
{
	private readonly Zentitle2Settings _settings;
	private readonly ILoggerFactory _loggerFactory;

	public ZentitleActivationService(AppSettings appSettings, ILoggerFactory loggerFactory)
	{
		_settings = appSettings.Zentitle2;
		_loggerFactory = loggerFactory;
	}

	public Task<ActivationSummary> ActivateAsync(string openIdToken, string seatName, CancellationToken cancellationToken = default)
	{
		return Task.Run(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			using var activation = CreateActivation();
			await activation.Initialize();
			await DeactivateIfNeededAsync(activation);
			await activation.ActivateWithOpenIdToken(openIdToken, seatName, editionId: null);

			var entitlement = await activation.GetActivationEntitlement();
			var entitlementExpiryDate = activation.Info.Entitlement?.EntitlementExpiry;

			return new ActivationSummary(
				activation.Info.Id,
				activation.Info.Entitlement?.Id,
				activation.Info.ProductId,
				entitlement.ProductName,
				entitlement.Sku,
				entitlement.OfferingName,
				entitlement.Plan?.Name,
				entitlementExpiryDate,
				BuildDisplayText(
					activation.Info.Entitlement?.Id,
					activation.Info.ProductId,
					entitlement.ProductName,
					entitlement.Sku,
					entitlement.OfferingName,
					entitlement.Plan?.Name,
					entitlementExpiryDate));
		}, cancellationToken);
	}

	public Task DeactivateAsync(CancellationToken cancellationToken = default)
	{
		return Task.Run(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			using var activation = CreateActivation();
			await activation.Initialize();

			if (activation.State is Zentitle.Licensing.Client.ActivationState.NotActivated or Zentitle.Licensing.Client.ActivationState.Uninitialized)
			{
				return;
			}

			var result = await activation.Deactivate();
			if (!result.IsSuccess)
			{
				throw new InvalidOperationException(BuildOperationError(result.Error));
			}
		}, cancellationToken);
	}

	private Activation CreateActivation()
	{
		return new Activation(options =>
		{
			options
				.WithTenant(_settings.TenantId)
				.WithProduct(_settings.ProductId)
				.WithSeatId(() => $"idpdemo-{Guid.NewGuid():N}")
				.UseStorage(new PlainTextFileActivationStorage(GetStoragePath()))
				.WithOnlineActivationSupport(online =>
				{
					online
						.UseLicensingApi(new Uri(_settings.LicensingApiUrl))
						.UseHttpClientFactory(() => new HttpClient());
				})
				.UseLoggerFactory(_loggerFactory);
		});
	}

	private static async Task DeactivateIfNeededAsync(IActivation activation)
	{
		if (activation.State is Zentitle.Licensing.Client.ActivationState.NotActivated or Zentitle.Licensing.Client.ActivationState.Uninitialized)
		{
			return;
		}

		var result = await activation.Deactivate();
		if (!result.IsSuccess)
		{
			throw new InvalidOperationException(BuildOperationError(result.Error));
		}
	}

	private static string BuildDisplayText(
		string? entitlementId,
		string? productId,
		string? productName,
		string? sku,
		string? offeringName,
		string? planName,
		DateTimeOffset? entitlementExpiryDate)
	{
		var builder = new StringBuilder();
		builder.AppendLine($"Entitlement ID: {ValueOrUnavailable(entitlementId)}");
		builder.AppendLine($"Product ID: {ValueOrUnavailable(productId)}");
		builder.AppendLine($"Product Name: {ValueOrUnavailable(productName)}");
		builder.AppendLine($"SKU: {ValueOrUnavailable(sku)}");
		builder.AppendLine($"Offering Name: {ValueOrUnavailable(offeringName)}");
		builder.AppendLine($"Plan Name: {ValueOrUnavailable(planName)}");
		builder.Append($"Entitlement Expiry Date: {(entitlementExpiryDate.HasValue ? entitlementExpiryDate.Value.ToString("u") : "Unavailable")}");
		return builder.ToString();
	}

	private static string ValueOrUnavailable(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
	}

	private static string BuildOperationError(ActivationOperationError? error)
	{
		if (error is null)
		{
			return "The activation operation failed without additional details.";
		}

		return $"{error.Code}: {error.Message} {error.Details}".Trim();
	}

	private static string GetStoragePath()
	{
		Directory.CreateDirectory(FileSystem.Current.AppDataDirectory);
		return Path.Combine(FileSystem.Current.AppDataDirectory, "zentitle-activation.json");
	}
}

public sealed record ActivationSummary(
	string? ActivationId,
	string? EntitlementId,
	string? ProductId,
	string? ProductName,
	string? Sku,
	string? OfferingName,
	string? PlanName,
	DateTimeOffset? EntitlementExpiryDate,
	string DisplayText)
{
	public string ToDisplayText() => DisplayText;
}
