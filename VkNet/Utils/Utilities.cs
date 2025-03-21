﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using VkNet.Enums;
using VkNet.Exception;
using VkNet.Infrastructure;

namespace VkNet.Utils;

/// <summary>
/// Утилиты.
/// </summary>
public static class Utilities
{
	/// <summary>
	/// Преобразовать в перечисление из числа.
	/// </summary>
	/// <typeparam name="T"> Тип перечисления. </typeparam>
	/// <param name="value"> Числовое значение. </param>
	/// <returns> Перечисление указанного типа. </returns>
	/// <exception cref="System.ArgumentException"> value </exception>
	public static T EnumFrom<T>(int value)
		where T : IConvertible
	{
		if (!Enum.IsDefined(typeof(T), value))
		{
			throw new ArgumentException($"Enum value {value} not defined!", nameof(value));
		}

		return (T) (object) value;
	}

	/// <summary>
	/// Преобразовать в перечисление из числа.
	/// </summary>
	/// <typeparam name="T"> Тип перечисления. </typeparam>
	/// <param name="value"> Числовое значение. </param>
	/// <returns> Перечисление указанного типа. </returns>
	public static T? NullableEnumFrom<T>(int value)
		where T : struct
	{
		if (!Enum.IsDefined(typeof(T), value))
		{
			return null;
		}

		return (T) (object) value;
	}

	/// <summary>
	/// Получение идентификатора.
	/// Применять когда id может быть задано как строкой так и числом в json'e.
	/// </summary>
	/// <param name="response"> Ответ от сервера vk.com </param>
	/// <returns> Число типа long или null </returns>
	public static long? GetNullableLongId(VkResponse response) => string.IsNullOrWhiteSpace(response?.ToString())
		? null
		: System.Convert.ToInt64(response.ToString());

	/// <summary>
	/// Объединить не пустую коллекцию.
	/// </summary>
	/// <typeparam name="T"> Тип коллекции. </typeparam>
	/// <param name="collection"> Коллекция. </param>
	/// <param name="separator"> Разделитель. </param>
	/// <returns> Строковое представление коллекции через разделитель. </returns>
	public static string JoinNonEmpty<T>(this IEnumerable<T> collection, string separator = ",")
	{
		if (collection is null)
		{
			return string.Empty;
		}

		return string.Join(separator,
			collection.Select(i => i.ToString()
					.Trim())
				.Where(s => !string.IsNullOrWhiteSpace(s)));
	}

	/// <summary>
	/// Преобразовать массив объектов ответа сервера vk.com.
	/// </summary>
	/// <typeparam name="T"> Тип коллекции. </typeparam>
	/// <param name="response"> Ответ от сервера vk.com. </param>
	/// <param name="selector"> Функция фильтрации. </param>
	/// <returns> Коллекция данных указанного типа. </returns>
	public static IEnumerable<T> Convert<T>(this VkResponseArray response, Func<VkResponse, T> selector) => response?.Select(selector)
			.ToList()
		?? Enumerable.Empty<T>();

	/// <summary>
	/// Конвертировать JSON в читаемый формат с отступами
	/// </summary>
	/// <param name="json"> JSON. </param>
	/// <returns> JSON с отступами </returns>
	public static string PrettyPrintJson(string json)
	{
		const string hidden = "***HIDDEN***";

		var keysToHide = new[]
		{
			"access_token",
			"new_password",
			"old_password"
		};

		try
		{
			var jObject = json.ToJObject();

			foreach (var key in keysToHide.Where(x => jObject.ContainsKey(x)))
			{
				jObject[key] = hidden;
			}

			return jObject.ToString(Formatting.Indented);
		}
		catch (VkApiException)
		{
			return json;
		}
	}

	/// <summary>
	/// Сериализует объект в JSON
	/// </summary>
	/// <param name="object"> Объект </param>
	/// <returns>
	/// Сериализованный JSON
	/// </returns>
	public static string SerializeToJson<T>(T @object)
	{
		var result = JsonConvert.SerializeObject(@object, Formatting.Indented);

		return result == "null"
			? null
			: result;
	}

	/// <summary>
	/// Deserializes JSON for the specified .NET type. A return value indicates whether deserialization succeeded.
	/// </summary>
	/// <param name="json"> The JSON to deserialize. </param>
	/// <param name="result"> Deserialized object. </param>
	/// <typeparam name="T"> Type for deserialization. </typeparam>
	/// <returns>
	/// Признак успешной десериализации
	/// </returns>
	public static bool TryDeserializeObject<T>(string json, out T result)
	{
		try
		{
			result = JsonConvert.DeserializeObject<T>(json, JsonConfigure.JsonSerializerSettings);

			return true;
		}
		catch
		{
			result = default;

			return false;
		}
	}

	/// <summary>
	/// Преобразование из PascalCase в snake_case
	/// </summary>
	/// <param name="text">Исходная строка</param>
	/// <returns>
	/// Строкое представление в snake_case нотации
	/// </returns>
	public static string ToSnakeCase(this string text)
	{
		if (text is null)
		{
			throw new ArgumentNullException(nameof(text));
		}

		if (text.Length < 2)
		{
			return text;
		}

		var sb = new StringBuilder();
		sb.Append(char.ToLowerInvariant(text[0]));

		for (var i = 1; i < text.Length; ++i)
		{
			var c = text[i];

			if (char.IsUpper(c))
			{
				sb.Append('_');
				sb.Append(char.ToLowerInvariant(c));
			} else
			{
				sb.Append(c);
			}
		}

		return sb.ToString();
	}

	private static string ToCamelCase(this string str) => str.Split(new[]
		{
			"_"
		}, StringSplitOptions.RemoveEmptyEntries)
		.Select(s =>
			char.ToUpperInvariant(s[0]) + s.Substring(1, s.Length - 1))
		.Aggregate(string.Empty, (s1, s2) => s1 + s2);

	private static bool? IsNullableType(Type t)
	{
		if (t is null)
		{
			return null;
		}

		return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
	}

	/// <summary>
	/// Проверка на StringEnum
	/// </summary>
	/// <param name="t">Исходный тип</param>
	/// <returns>
	/// Признак строкового перечисления
	/// </returns>
	public static bool IsNullableStringEnum(Type t)
	{
		var isNull = IsNullableType(t.GenericTypeArguments.FirstOrDefault());
		StringEnumAttribute myAttribute = null;

		if (isNull is null)
		{
			isNull = true;
		}

		if (isNull is false)
		{
			myAttribute = (StringEnumAttribute) Attribute.GetCustomAttribute(t.GenericTypeArguments.FirstOrDefault(),
				typeof(StringEnumAttribute));
		}

		// Get instance of the attribute.

		return myAttribute is not null;
	}

	/// <summary>
	/// Проверка на StringEnum
	/// </summary>
	/// <param name="t">Исходный тип</param>
	/// <returns>
	/// Признак строкового перечисления
	/// </returns>
	public static bool IsTolerantEnum(Type t)
	{
		var isNull = IsNullableType(t.GenericTypeArguments.FirstOrDefault());
		JsonConverterAttribute myAttribute = null;

		if (isNull is null)
		{
			isNull = true;
		}

		if (isNull is false)
		{
			myAttribute = (JsonConverterAttribute) Attribute.GetCustomAttribute(t.GenericTypeArguments.FirstOrDefault(),
				typeof(JsonConverterAttribute));
		}

		// Get instance of the attribute.

		return myAttribute is not null;
	}

	/// <summary>
	/// Проверка на StringEnum
	/// </summary>
	/// <param name="t">Исходный тип</param>
	/// <returns>
	/// Признак строкового перечисления
	/// </returns>
	public static bool IsStringEnum(Type t)
	{
		// Get instance of the attribute.
		var myAttribute = (StringEnumAttribute) Attribute.GetCustomAttribute(t, typeof(StringEnumAttribute));

		return myAttribute is not null;
	}

	/// <summary>
	/// Serialize Enum Value
	/// </summary>
	/// <param name="value">Значение перечисления</param>
	/// <typeparam name="TEnum">Перечисление</typeparam>
	/// <returns>
	/// Строковое представление
	/// </returns>
	public static string Serialize<TEnum>(TEnum value)
	{
		var fallback = Enum.GetName(typeof(TEnum), value);

		var member = typeof(TEnum).GetMember(value.ToString())
			.FirstOrDefault();

		if (member is null)
		{
			return fallback;
		}

		var enumMemberAttributes = member.GetCustomAttributes(typeof(EnumMemberAttribute), false)
			.Cast<EnumMemberAttribute>()
			.FirstOrDefault();

		return enumMemberAttributes is null
			? fallback
			: enumMemberAttributes.Value;
	}

	/// <summary>
	/// Deserialize Enum Value
	/// </summary>
	/// <param name="value">Значение перечисления</param>
	/// <typeparam name="TEnum">Тип перечисления</typeparam>
	/// <returns>
	/// Перечисление
	/// </returns>
	public static TEnum? Deserialize<TEnum>(string value)
		where TEnum : struct
	{
		if (Enum.TryParse(value.ToCamelCase(), out TEnum parsed))
		{
			return parsed;
		}

		var found = typeof(TEnum).GetMembers()
			.Select(x => new
			{
				Member = x,
				Attribute = x.GetCustomAttributes(typeof(EnumMemberAttribute), false)
					.OfType<EnumMemberAttribute>()
					.FirstOrDefault()
			})
			.FirstOrDefault(x => x.Attribute?.Value == value);

		if (found is not null)
		{
			return (TEnum) Enum.Parse(typeof(TEnum), found.Member.Name);
		}

		return null;
	}
}