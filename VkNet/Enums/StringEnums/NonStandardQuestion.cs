using Newtonsoft.Json;
using VkNet.Utils.JsonConverter;

namespace VkNet.Enums.StringEnums;

/// <summary>
/// Типы нестандартных вопросов
/// </summary>
[StringEnum]
[JsonConverter(typeof(TolerantStringEnumConverter))]
public enum NonStandardQuestion
{
	/// <summary>
	/// Простое текстовое поле (строка)
	/// </summary>
	Input,

	/// <summary>
	/// Большое текстовое поле (абзац)
	/// </summary>
	Textarea,

	/// <summary>
	/// Выбор одного из нескольких вариантов
	/// </summary>
	Radio,

	/// <summary>
	/// Выбор нескольких вариантов
	/// </summary>
	Checkbox,

	/// <summary>
	/// Выбор одного варианта из выпадающего списка
	/// </summary>
	/// <remarks>
	/// options должен быть массивом структур, описывающих варианты ответа
	/// </remarks>
	Select,

	/// <summary>
	/// Текст ответа
	/// </summary>
	Label,

	/// <summary>
	/// Ключ ответа (необязательно)
	/// </summary>
	Key
}