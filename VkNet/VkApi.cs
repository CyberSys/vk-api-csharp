// ReSharper disable once RedundantUsingDirective
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using VkNet.Abstractions;
using VkNet.Abstractions.Authorization;
using VkNet.Abstractions.Category;
using VkNet.Abstractions.Core;
using VkNet.Abstractions.Utils;
using VkNet.Categories;
using VkNet.Enums;
using VkNet.Exception;
using VkNet.Infrastructure;
using VkNet.Model;
using VkNet.Utils;
using VkNet.Utils.AntiCaptcha;
using VkNet.Utils.JsonConverter;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable EventNeverSubscribedTo.Global

namespace VkNet;

/// <summary>
/// Служит для оповещения об истечении токена
/// </summary>
/// <param name="sender">
/// Экземпляр API у которого истекло время токена
/// </param>
public delegate void VkApiDelegate(VkApi sender);

/// <inheritdoc cref="IVkApi" />
/// <summary>
/// API для работы с ВКонтакте.
/// Выступает в качестве фабрики для различных категорий API (например, для работы
/// с пользователями, группами и т.п.)
/// </summary>
public class VkApi : IVkApi
{
	private readonly ServiceProvider _serviceProvider;

	/// <summary>
	/// Параметры авторизации.
	/// </summary>
	private IApiAuthParams _ap;

	/// <summary>
	/// Таймер.
	/// </summary>
	private Timer _expireTimer;

	/// <summary>
	/// Сервис управления языком
	/// </summary>
	private ILanguageService _language;

	/// <summary>
	/// Логгер
	/// </summary>
	private ILogger _logger;

	/// <summary>
	/// Обработчик ошибок десериализации
	/// </summary>
	public bool? DeserializationErrorHandler { get; set; }

	/// <summary>
	/// Rest Client
	/// </summary>
	public IRestClient RestClient { get; set; }

	/// <inheritdoc cref="VkApi" />
	public VkApi(ILogger logger, ICaptchaSolver captchaSolver = null, IAuthorizationFlow authorizationFlow = null)
	{
		var container = new ServiceCollection();

		if (logger is not null)
		{
			container.TryAddSingleton(_ => logger);
		}

		if (captchaSolver is not null)
		{
			container.TryAddSingleton(captchaSolver);
		}

		if (authorizationFlow is not null)
		{
			container.TryAddSingleton(authorizationFlow);
		}

		container.RegisterDefaultDependencies();

		_serviceProvider = container.BuildServiceProvider();

		Initialization(_serviceProvider);
	}

	/// <inheritdoc cref="VkApi" />
	public VkApi(IServiceCollection serviceCollection = null)
	{
		var container = serviceCollection ?? new ServiceCollection();

		container.RegisterDefaultDependencies();

		_serviceProvider = container.BuildServiceProvider();

		Initialization(_serviceProvider);
	}

	/// <summary>
	/// Токен для доступа к методам API
	/// </summary>
	private string AccessToken { get; set; }

	/// <inheritdoc />
	public IVkApiVersionManager VkApiVersion { get; set; }

	/// <inheritdoc />
	public event VkApiDelegate OnTokenExpires;

	/// <inheritdoc />
	public event VkApiDelegate OnTokenUpdatedAutomatically;

	/// <inheritdoc />
	public IAuthorizationFlow AuthorizationFlow { get; set; }

	/// <inheritdoc />
	public INeedValidationHandler NeedValidationHandler { get; set; }

	/// <inheritdoc />
	public bool IsAuthorized => !string.IsNullOrWhiteSpace(AccessToken);

	/// <inheritdoc />
	public string Token => AccessToken;

	/// <inheritdoc />
	public long? UserId { get; set; }

	/// <inheritdoc />
	public ICaptchaSolver CaptchaSolver { get; set; }

	/// <inheritdoc />
	public void SetLanguage(Language language) => _language.SetLanguage(language);

	/// <inheritdoc />
	public Language? GetLanguage() => _language.GetLanguage();

	/// <inheritdoc />
	public void Authorize(IApiAuthParams @params)
	{
		// если токен не задан - обычная авторизация
		if (@params.AccessToken is null)
		{
			AuthorizeWithAntiCaptcha(@params);

			// Сбросить после использования
			@params.CaptchaSid = null;
			@params.CaptchaKey = "";
		}

		// если токен задан - авторизация с помощью токена полученного извне
		else
		{
			TokenAuth(@params.AccessToken, @params.UserId, @params.TokenExpireTime);
		}

		if (@params.IsTokenUpdateAutomatically)
		{
			OnTokenExpires += OnTokenExpired;
		}

		_ap = @params;

		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Авторизация прошла успешно");
		}
	}

	/// <inheritdoc />
	public void Authorize(ApiAuthParams @params) => Authorize((IApiAuthParams) @params);

	/// <inheritdoc />
	public Task AuthorizeAsync(IApiAuthParams @params, CancellationToken token = default) =>
		TypeHelper.TryInvokeMethodAsync(() => Authorize(@params), CancellationToken.None);

	/// <inheritdoc />
	public void RefreshToken(Func<string> code = null)
	{
		if (!string.IsNullOrWhiteSpace(_ap.Login) && !string.IsNullOrWhiteSpace(_ap.Password))
		{
			_ap.TwoFactorAuthorization ??= code;
			AuthorizeWithAntiCaptcha(_ap);
		} else
		{
			const string message =
				"Невозможно обновить токен доступа т.к. последняя авторизация происходила не при помощи логина и пароля";

			if (_logger.IsEnabled(LogLevel.Error))
			{
				_logger.LogError(message);
			}

			throw new AggregateException(message);
		}
	}

	/// <inheritdoc />
	public void RefreshToken(Task<string> code = null)
	{
		if (!string.IsNullOrWhiteSpace(_ap.Login) && !string.IsNullOrWhiteSpace(_ap.Password))
		{
			_ap.TwoFactorAuthorizationAsync ??= code;
			AuthorizeWithAntiCaptcha(_ap);
		} else
		{
			const string message =
				"Невозможно обновить токен доступа т.к. последняя авторизация происходила не при помощи логина и пароля";

			if (_logger.IsEnabled(LogLevel.Error))
			{
				_logger.LogError(message);
			}

			throw new AggregateException(message);
		}
	}

	/// <inheritdoc />
	public void LogOut() => AccessToken = string.Empty;

	/// <inheritdoc />
	public Task RefreshTokenAsync(Func<string> code = null, CancellationToken token = default) =>
		TypeHelper.TryInvokeMethodAsync(() => RefreshToken(code), token);

	/// <inheritdoc />
	public Task RefreshTokenAsync(Task<string> code = null, CancellationToken token = default) =>
		TypeHelper.TryInvokeMethodAsync(() => RefreshToken(code), token);

	/// <inheritdoc />
	public Task LogOutAsync(CancellationToken token = default) => TypeHelper.TryInvokeMethodAsync(LogOut, token);

	/// <inheritdoc />
	[MethodImpl(MethodImplOptions.NoInlining)]
	public VkResponse Call(string methodName, VkParameters parameters, bool skipAuthorization = false,
							params JsonConverter[] jsonConverters)
	{
		var answer = CallBase(methodName, parameters, skipAuthorization);

		var json = answer.ToJObject();

		var rawResponse = json["response"];

		return new(rawResponse)
		{
			RawJson = answer
		};
	}

	/// <inheritdoc />
	[MethodImpl(MethodImplOptions.NoInlining)]
	public T Call<T>(string methodName, VkParameters parameters, bool skipAuthorization = false, params JsonConverter[] jsonConverters)
	{
		var answer = CallBase(methodName, parameters, skipAuthorization);

		var context = new StreamingContext(StreamingContextStates.All)
			.AddTypeData(typeof(TolerantStringEnumConverter), DeserializationErrorHandler);

		JsonConvert.DefaultSettings = () => new()
		{
			Context = context
		};

		var settings = new JsonSerializerSettings
		{
			Converters = [],
			ContractResolver = new DefaultContractResolver()
			{
				NamingStrategy = new SnakeCaseNamingStrategy()
			},
			Context = context,
			MaxDepth = null,
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore
		};

		var converters = GetJsonConverters<T>(jsonConverters);

		foreach (var jsonConverter in converters)
		{
			settings.Converters.Add(jsonConverter);
		}

		return JsonConvert.DeserializeObject<T>(answer, settings);
	}

	/// <inheritdoc />
	public Task<VkResponse> CallAsync(string methodName, VkParameters parameters, bool skipAuthorization = false,
									CancellationToken token = default)
	{
		var task = TypeHelper.TryInvokeMethodAsync(() =>
			Call(methodName, parameters, skipAuthorization), token);

		task.ConfigureAwait(false);

		return task;
	}

	/// <inheritdoc />
	public Task<T> CallAsync<T>(string methodName, VkParameters parameters, bool skipAuthorization = false,
								CancellationToken token = default)
	{
		var task = TypeHelper.TryInvokeMethodAsync(() =>
			Call<T>(methodName, parameters, skipAuthorization), token);

		task.ConfigureAwait(false);

		return task;
	}

	/// <inheritdoc />
	[CanBeNull]
	public string Invoke(string methodName, IDictionary<string, string> parameters, bool skipAuthorization = false)
	{
		if (!skipAuthorization && !IsAuthorized)
		{
			if (_logger.IsEnabled(LogLevel.Error))
			{
				_logger.LogError("Метод '{MethodName}' нельзя вызывать без авторизации", methodName);
			}

			throw new AccessTokenInvalidException($"Метод '{methodName}' нельзя вызывать без авторизации");
		}

		var url = $"https://api.vk.com/method/{methodName}";
		var answer = InvokeBase(url, parameters);

		if (_logger.IsEnabled(LogLevel.Trace))
		{
			_logger.LogTrace("Uri = \"{Url}\"", url);
			_logger.LogTrace("Json ={NewLine}{Json}", Environment.NewLine, Utilities.PrettyPrintJson(answer));
		}

		VkErrors.IfErrorThrowException(answer);

		return answer;
	}

	/// <inheritdoc />
	[CanBeNull]
	public Task<string> InvokeAsync(string methodName, IDictionary<string, string> parameters, bool skipAuthorization = false,
									CancellationToken token = default) => TypeHelper.TryInvokeMethodAsync(() =>
		Invoke(methodName, parameters, skipAuthorization), token);

	/// <inheritdoc />
	public VkResponse CallLongPoll(string server, VkParameters parameters, params JsonConverter[] jsonConverters)
	{
		var json = InvokeLongPollExtended(server, parameters);
		var rawResponse = json.Root;

		return new(rawResponse)
		{
			RawJson = json.ToString()
		};
	}

	/// <inheritdoc />
	public T CallLongPoll<T>(string server, VkParameters parameters, params JsonConverter[] jsonConverters)
	{
		var answer = InvokeLongPollExtended(server, parameters);
		var rawResponse = answer.Root.ToString();

		var response = new VkResponse(rawResponse)
		{
			RawJson = answer.ToString()
		};

		var settings = new JsonSerializerSettings
		{
			Converters = new List<JsonConverter>(),
			ContractResolver = new DefaultContractResolver
			{
				NamingStrategy = new SnakeCaseNamingStrategy()
			},
			MaxDepth = null,
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore
		};

		if (jsonConverters.Any())
		{
			foreach (var jsonConverter in jsonConverters)
			{
				settings.Converters.Add(jsonConverter);
			}
		}

		settings.Converters.Add(new VkCollectionJsonConverter());
		settings.Converters.Add(new VkDefaultJsonConverter());
		settings.Converters.Add(new UnixDateTimeConverter());
		settings.Converters.Add(new AttachmentJsonConverter());
		settings.Converters.Add(new StringEnumConverter());

		return JsonConvert.DeserializeObject<T>(response, settings);
	}

	/// <inheritdoc />
	public Task<VkResponse> CallLongPollAsync(string server, VkParameters parameters, CancellationToken token = default) =>
		TypeHelper.TryInvokeMethodAsync(() => CallLongPoll(server, parameters), token);

	/// <inheritdoc />
	public string InvokeLongPoll(string server, Dictionary<string, string> parameters) => InvokeLongPollExtended(server, parameters)
		.ToString();

	/// <inheritdoc />
	public JObject InvokeLongPollExtended(string server, Dictionary<string, string> parameters)
	{
		if (string.IsNullOrEmpty(server))
		{
			const string message = "Server не должен быть пустым или null";

			if (_logger.IsEnabled(LogLevel.Error))
			{
				_logger.LogError(message);
			}

			throw new ArgumentException(message);
		}

		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Вызов GetLongPollHistory с сервером {Server}, с параметрами {Parameters}",
				server,
				string.Join(",", parameters.Select(x => $"{x.Key}={x.Value}")));
		}

		var answer = InvokeBase(server, parameters);

		if (!_logger.IsEnabled(LogLevel.Trace))
		{
			return VkErrors.IfErrorThrowException(answer);
		}

		_logger.LogTrace("Uri = \"{Url}\"", server);
		_logger.LogTrace("Json ={NewLine}{Json}", Environment.NewLine, Utilities.PrettyPrintJson(answer));

		return VkErrors.IfErrorThrowException(answer);
	}

	/// <inheritdoc />
	public Task<string> InvokeLongPollAsync(string server, Dictionary<string, string> parameters, CancellationToken token = default) =>
		TypeHelper.TryInvokeMethodAsync(() =>
			InvokeLongPollExtended(server, parameters)
				.ToString(), token);

	/// <inheritdoc />
	public Task<JObject> InvokeLongPollExtendedAsync(string server, Dictionary<string, string> parameters,
													CancellationToken token = default) => TypeHelper.TryInvokeMethodAsync(() =>
		InvokeLongPollExtended(server, parameters), token);

	/// <inheritdoc cref="IDisposable" />
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <inheritdoc />
	public void Validate(string validateUrl)
	{
		StopTimer();

		LastInvokeTime = DateTimeOffset.Now;
		var authorization = NeedValidationHandler.Validate(validateUrl);

		if (string.IsNullOrWhiteSpace(authorization.AccessToken))
		{
			const string message = "Не удалось автоматически пройти валидацию!";

			if (_logger.IsEnabled(LogLevel.Error))
			{
				_logger.LogError(message);
			}

			throw new NeedValidationException(new()
			{
				ErrorMessage = message,
				RedirectUri = new(validateUrl)
			});
		}

		AccessToken = authorization.AccessToken;
		UserId = authorization.UserId;
	}

	/// <inheritdoc cref="IVkApi.Validate" />
	[Obsolete(ObsoleteText.Validate)]
	public void Validate(string validateUrl, string phoneNumber)
	{
		_ap.Phone = phoneNumber;
		Validate(validateUrl);
	}

	/// <summary>
	/// Получить список JsonConverter для обработки ответа vk api
	/// </summary>
	/// <param name="customConverters">Список конвертеров</param>
	/// <returns>
	/// Полный список конвертеров
	/// </returns>
	protected virtual List<JsonConverter> GetJsonConverters<T>(IReadOnlyList<JsonConverter> customConverters)
	{
		var converters = new List<JsonConverter>();

		converters.AddRange(customConverters);

		converters.Add(new VkCollectionJsonConverter());
		converters.Add(new VkDefaultJsonConverter());
		converters.Add(new UnixDateTimeConverter());
		converters.Add(new AttachmentJsonConverter());
		converters.Add(new StringEnumConverter());

		return converters;
	}

	private void OnTokenExpired(VkApi sender)
	{
		var isAsync = _ap.TwoFactorAuthorization is null;

		if (isAsync)
		{
			RefreshTokenAsync(_ap.TwoFactorAuthorizationAsync)
				.GetAwaiter()
				.GetResult();
		} else
		{
			RefreshTokenAsync(_ap.TwoFactorAuthorization)
				.GetAwaiter()
				.GetResult();
		}

		OnTokenUpdatedAutomatically?.Invoke(sender);
	}

	/// <summary>
	/// Releases unmanaged and - optionally - managed resources.
	/// </summary>
	/// <param name="disposing">
	/// <c> true </c> to release both managed and unmanaged resources; <c> false </c>
	/// to release only unmanaged resources.
	/// </param>
	protected virtual void Dispose(bool disposing)
	{
		_expireTimer?.Dispose();
		_serviceProvider.Dispose();
	}

	#region Requests limit stuff

	/// <summary>
	/// The <see cref="IRateLimiter" />.
	/// </summary>
	private IRateLimiter _rateLimiter;

	/// <summary>
	/// Запросов в секунду.
	/// </summary>
	private int _requestsPerSecond;

	/// <inheritdoc />
	public DateTimeOffset? LastInvokeTime { get; private set; }

	/// <inheritdoc />
	public TimeSpan? LastInvokeTimeSpan
	{
		get {
			if (LastInvokeTime.HasValue)
			{
				return DateTimeOffset.Now - LastInvokeTime.Value;
			}

			return null;
		}
	}

	/// <inheritdoc />
	public int RequestsPerSecond
	{
		get => _requestsPerSecond;

		set {
			if (value < 0)
			{
				throw new ArgumentException("Value must be positive", nameof(value));
			}

			_requestsPerSecond = value;

			if (_requestsPerSecond == 0)
			{
				return;
			}

			_rateLimiter.SetRate(_requestsPerSecond, TimeSpan.FromSeconds(1));
		}
	}

	#endregion

	#region Captcha handler stuff

	/// <summary>
	/// Обработчик ошибки капчи
	/// </summary>
	[UsedImplicitly]
	public ICaptchaHandler CaptchaHandler { get; set; }

	/// <inheritdoc />
	public int MaxCaptchaRecognitionCount
	{
		get => CaptchaHandler.MaxCaptchaRecognitionCount;

		set {
			switch (value)
			{
				case < 0:
					throw new ArgumentException(@"Value must be positive", nameof(value));

				case 0:
					return;

				default:
					CaptchaHandler.MaxCaptchaRecognitionCount = value;

					break;
			}
		}
	}

	#endregion

	#region Categories Definition

	/// <inheritdoc />
	public IUsersCategory Users { get; private set; }

	/// <inheritdoc />
	public IFriendsCategory Friends { get; private set; }

	/// <inheritdoc />
	public IStatusCategory Status { get; private set; }

	/// <inheritdoc />
	public IMessagesCategory Messages { get; private set; }

	/// <inheritdoc />
	public IGroupsCategory Groups { get; private set; }

	/// <inheritdoc />
	public IAudioCategory Audio { get; private set; }

	/// <inheritdoc />
	public IDatabaseCategory Database { get; private set; }

	/// <inheritdoc />
	public IUtilsCategory Utils { get; private set; }

	/// <inheritdoc />
	public IWallCategory Wall { get; private set; }

	/// <inheritdoc />
	public IBoardCategory Board { get; private set; }

	/// <inheritdoc />
	public IFaveCategory Fave { get; private set; }

	/// <inheritdoc />
	public IVideoCategory Video { get; private set; }

	/// <inheritdoc />
	public IAccountCategory Account { get; private set; }

	/// <inheritdoc />
	public IPhotoCategory Photo { get; private set; }

	/// <inheritdoc />
	public IDocsCategory Docs { get; private set; }

	/// <inheritdoc />
	public ILikesCategory Likes { get; private set; }

	/// <inheritdoc />
	public IPagesCategory Pages { get; private set; }

	/// <inheritdoc />
	public IAppsCategory Apps { get; private set; }

	/// <inheritdoc />
	public INewsFeedCategory NewsFeed { get; private set; }

	/// <inheritdoc />
	public IStatsCategory Stats { get; private set; }

	/// <inheritdoc />
	public IGiftsCategory Gifts { get; private set; }

	/// <inheritdoc />
	public IMarketsCategory Markets { get; private set; }

	/// <inheritdoc />
	public IAuthCategory Auth { get; private set; }

	/// <inheritdoc />
	public IExecuteCategory Execute { get; private set; }

	/// <inheritdoc />
	public IPollsCategory PollsCategory { get; private set; }

	/// <inheritdoc />
	public ISearchCategory Search { get; private set; }

	/// <inheritdoc />
	public IStorageCategory Storage { get; set; }

	/// <inheritdoc />
	public IAdsCategory Ads { get; private set; }

	/// <inheritdoc />
	public INotificationsCategory Notifications { get; set; }

	/// <inheritdoc />
	public IWidgetsCategory Widgets { get; set; }

	/// <inheritdoc />
	public ILeadsCategory Leads { get; set; }

	/// <inheritdoc />
	public IStreamingCategory Streaming { get; set; }

	/// <inheritdoc />
	public IPlacesCategory Places { get; set; }

	/// <inheritdoc />
	public IPrettyCardsCategory PrettyCards { get; set; }

	/// <inheritdoc />
	public IPodcastsCategory Podcasts { get; set; }

	///<inheritdoc />
	public INotesCategory Notes { get; set; }

	/// <inheritdoc />
	public IAppWidgetsCategory AppWidgets { get; set; }

	/// <inheritdoc />
	public IOrdersCategory Orders { get; set; }

	/// <inheritdoc />
	public ISecureCategory Secure { get; set; }

	/// <inheritdoc />
	public IStoriesCategory Stories { get; set; }

	/// <inheritdoc />
	public ILeadFormsCategory LeadForms { get; set; }

	/// <inheritdoc />
	public IDonutCategory Donut { get; set; }

	/// <inheritdoc />
	public IDownloadedGamesCategory DownloadedGames { get; set; }

	/// <inheritdoc />
	public IAsrCategory Asr { get; set; }

	/// <inheritdoc />
	public IShortVideoCategory ShortVideo { get; set; }

	/// <inheritdoc />
	public IStoreCategory Store { get; set; }

	/// <inheritdoc />
	public ICallsCategory Calls { get; set; }

	#endregion

	#region private

	/// <summary>
	/// Базовое обращение к vk.com
	/// </summary>
	/// <param name="methodName"> Наименование метода </param>
	/// <param name="parameters"> Параметры запроса </param>
	/// <param name="skipAuthorization"> Пропустить авторизацию </param>
	/// <returns> Ответ от vk.com в формате json </returns>
	/// <exception cref="CaptchaNeededException"> Требуется ввести капчу </exception>
	private string CallBase(string methodName, VkParameters parameters, bool skipAuthorization)
	{
		if (!parameters.ContainsKey(Constants.Version))
		{
			parameters.Add(Constants.Version, VkApiVersion.Version);
		}

		if (!parameters.ContainsKey(Constants.AccessToken))
		{
			parameters.Add(Constants.AccessToken, AccessToken);
		}

		if (!parameters.ContainsKey(Constants.Language)
			&& _language.GetLanguage()
				.HasValue)
		{
			parameters.Add(Constants.Language, _language.GetLanguage());
		}

		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Вызов метода {MethodName}, с параметрами {Parameters}",
				methodName,
				string.Join(",", parameters.Where(x => x.Key != Constants.AccessToken)
					.Select(x => $"{x.Key}={x.Value}")));
		}

		string answer;

		if (CaptchaSolver is null)
		{
			answer = Invoke(methodName, parameters, skipAuthorization);
		} else
		{
			answer = CaptchaHandler.Perform((sid, key) =>
			{
				parameters.Add(Constants.CaptchaSid, sid);
				parameters.Add(Constants.CaptchaKey, key);

				return Invoke(methodName, parameters, skipAuthorization);
			});
		}

		return answer;
	}

	private string InvokeBase(string url, IDictionary<string, string> @params)
	{
		var answer = string.Empty;

		if (_expireTimer is null)
		{
			SetTimer(0);
		}

		// Защита от превышения количества запросов в секунду
		_rateLimiter.Perform(SendRequest, CancellationToken.None)
			.ConfigureAwait(false)
			.GetAwaiter()
			.GetResult();

		return answer;

		void SendRequest()
		{
			LastInvokeTime = DateTimeOffset.Now;

			var response = RestClient.PostAsync(new(url), @params, Encoding.UTF8)
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();

			answer = response.Message ?? response.Value;
		}
	}

	/// <summary>
	/// Авторизация и получение токена
	/// </summary>
	/// <param name="authParams"> Параметры авторизации </param>
	private void AuthorizeWithAntiCaptcha(IApiAuthParams authParams)
	{
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Старт авторизации");
		}

		if (CaptchaSolver is null)
		{
			BaseAuthorize(authParams);
		} else
		{
			CaptchaHandler.Perform((sid, key) =>
			{
				if (_logger.IsEnabled(LogLevel.Debug))
				{
					_logger.LogDebug("Авторизация с использование капчи");
				}

				authParams.CaptchaSid = sid;
				authParams.CaptchaKey = key;
				BaseAuthorize(authParams);

				return true;
			});
		}
	}

	/// <summary>
	/// Авторизация через установку токена
	/// </summary>
	/// <param name="accessToken"> Токен </param>
	/// <param name="userId"> Идентификатор пользователя </param>
	/// <param name="expireTime"> Время истечения токена </param>
	/// <exception cref="ArgumentNullException"> </exception>
	private void TokenAuth(string accessToken, long? userId, int expireTime)
	{
		if (string.IsNullOrWhiteSpace(accessToken))
		{
			if (_logger.IsEnabled(LogLevel.Error))
			{
				_logger.LogError("Авторизация через токен. Токен не задан");
			}

			throw new ArgumentNullException(accessToken);
		}

		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Авторизация через токен");
		}

		StopTimer();

		LastInvokeTime = DateTimeOffset.Now;
		SetApiPropertiesAfterAuth(expireTime, accessToken, userId);
		_ap = new ApiAuthParams();
	}

	/// <summary>
	/// Sets the token properties.
	/// </summary>
	/// <param name="authorization"> The authorization. </param>
	private void SetTokenProperties(AuthorizationResult authorization)
	{
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("Установка свойств токена");
		}

		var expireTime = (Convert.ToInt32(authorization.ExpiresIn) - 10) * 1000;
		SetApiPropertiesAfterAuth(expireTime, authorization.AccessToken, authorization.UserId);
	}

	/// <summary>
	/// Установить свойства api после авторизации
	/// </summary>
	/// <param name="expireTime"> </param>
	/// <param name="accessToken"> </param>
	/// <param name="userId"> </param>
	private void SetApiPropertiesAfterAuth(int expireTime, string accessToken, long? userId)
	{
		SetTimer(expireTime);
		AccessToken = accessToken;
		UserId = userId;
	}

	/// <summary>
	/// Установить значение таймера
	/// </summary>
	/// <param name="expireTime"> Значение таймера </param>
	private void SetTimer(int expireTime)
	{
		_expireTimer = new(AlertExpires);

		_expireTimer.Change(expireTime > 0
			? expireTime
			: Timeout.Infinite, Timeout.Infinite);
	}

	/// <summary>
	/// Прекращает работу таймера оповещения
	/// </summary>
	private void StopTimer() => _expireTimer?.Dispose();

	/// <summary>
	/// Создает событие оповещения об окончании времени токена
	/// </summary>
	/// <param name="state"> </param>
	private void AlertExpires(object state) => OnTokenExpires?.Invoke(this);

	/// <summary>
	/// Авторизация и получение токена
	/// </summary>
	/// <param name="authParams"> Параметры авторизации </param>
	/// <exception cref="VkAuthorizationException"> </exception>
	private void BaseAuthorize(IApiAuthParams authParams)
	{
		StopTimer();

		LastInvokeTime = DateTimeOffset.Now;

		AuthorizationFlow.SetAuthorizationParams(authParams);

		var authorization = AuthorizationFlow.AuthorizeAsync()
			.GetAwaiter()
			.GetResult();

		if (string.IsNullOrWhiteSpace(authorization.AccessToken))
		{
			const string message = "Authorization fail: invalid access token.";

			if (_logger.IsEnabled(LogLevel.Error))
			{
				_logger.LogError(message);
			}

			throw new VkAuthorizationException(message);
		}

		SetTokenProperties(authorization);
	}

	private void Initialization(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetService<ILogger>();
		CaptchaHandler = serviceProvider.GetRequiredService<ICaptchaHandler>();
		_language = serviceProvider.GetRequiredService<ILanguageService>();
		_rateLimiter = serviceProvider.GetRequiredService<IRateLimiter>();

		NeedValidationHandler = serviceProvider.GetRequiredService<INeedValidationHandler>();
		AuthorizationFlow = serviceProvider.GetRequiredService<IAuthorizationFlow>();
		CaptchaSolver = serviceProvider.GetService<ICaptchaSolver>();
		RestClient = serviceProvider.GetRequiredService<IRestClient>();
		VkApiVersion = serviceProvider.GetRequiredService<IVkApiVersionManager>();

		Users = new UsersCategory(this);
		Friends = new FriendsCategory(this);
		Status = new StatusCategory(this);
		Messages = new MessagesCategory(this);
		Groups = new GroupsCategory(this);
		Audio = new AudioCategory(this);
		Wall = new WallCategory(this);
		Board = new BoardCategory(this);
		Database = new DatabaseCategory(this);
		Utils = new UtilsCategory(this);
		Fave = new FaveCategory(this);
		Video = new VideoCategory(this);
		Account = new AccountCategory(this);
		Photo = new PhotoCategory(this);
		Docs = new DocsCategory(this);
		Likes = new LikesCategory(this);
		Pages = new PagesCategory(this);
		Gifts = new GiftsCategory(this);
		Apps = new AppsCategory(this);
		NewsFeed = new NewsFeedCategory(this);
		Stats = new StatsCategory(this);
		Auth = new AuthCategory(this);
		Markets = new MarketsCategory(this);
		Execute = new ExecuteCategory(this);
		PollsCategory = new PollsCategory(this);
		Search = new SearchCategory(this);
		Ads = new AdsCategory(this);
		Storage = new StorageCategory(this);
		Notifications = new NotificationsCategory(this);
		Widgets = new WidgetsCategory(this);
		Leads = new LeadsCategory(this);
		Streaming = new StreamingCategory(this);
		Places = new PlacesCategory(this);
		Notes = new NotesCategory(this);
		AppWidgets = new AppWidgetsCategory(this);
		Orders = new OrdersCategory(this);
		Secure = new SecureCategory(this);
		Stories = new StoriesCategory(this);
		LeadForms = new LeadFormsCategory(this);
		PrettyCards = new PrettyCardsCategory(this);
		Podcasts = new PodcastsCategory(this);
		Donut = new DonutCategory(this);
		DownloadedGames = new DownloadedGamesCategory(this);
		Asr = new AsrCategory(this);
		ShortVideo = new ShortVideoCategory(this);
		Store = new StoreCategory(this);
		Calls = new CallsCategory(this);

		RequestsPerSecond = 3;

		MaxCaptchaRecognitionCount = 5;
		#if NET45
		if (_logger.IsEnabled(LogLevel.Error))
		{
			_logger.LogError("Могут быть проблемы при выполнении запросов с Кодировкой 1251. Если проблема воспроизводится рекомендуется обновиться на NETFramework 4.6.1 или выше");
		}
		#else
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		#endif

		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("VkApi Initialization successfully");
		}
	}

	#endregion
}