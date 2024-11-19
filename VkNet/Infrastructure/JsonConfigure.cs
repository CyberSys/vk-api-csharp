using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VkNet.Exception;

namespace VkNet.Infrastructure;

/// <summary>
/// Конфигурация JSON
/// </summary>
internal static class JsonConfigure
{
	/// <returns></returns>
	internal static readonly JsonSerializerSettings JsonSerializerSettings = new()
	{
		MaxDepth = null,
		ReferenceLoopHandling = ReferenceLoopHandling.Ignore
	};

	/// <returns>
	/// Преобразование в JSON
	/// </returns>
	internal static JObject ToJObject(this string answer)
	{
		try
		{
			using var stringReader = new StringReader(answer);

			using JsonReader jsonReader = new JsonTextReader(stringReader);

			jsonReader.MaxDepth = null;

			return JObject.Load(jsonReader);
		}
		catch (JsonReaderException ex)
		{
			throw new VkApiException("Wrong json data.", ex);
		}
	}
}