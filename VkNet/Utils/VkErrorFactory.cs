using System;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using VkNet.Exception;
using VkNet.Model;

namespace VkNet.Utils;

/// <summary>
/// Фабрика по созданию исключений
/// </summary>
[UsedImplicitly]
public static class VkErrorFactory
{
	/// <summary>
	/// Создать ошибку
	/// </summary>
	/// <param name="error">Ошибка VK</param>
	/// <returns>
	/// Исключение <see cref="VkApiMethodInvokeException" />
	/// </returns>
	public static VkApiMethodInvokeException Create(VkError error)
	{
		var vkApiMethodInvokeExceptions = Array.Find(typeof(VkApiMethodInvokeException).Assembly.GetTypes(), x =>
			x.IsSubclassOf(typeof(VkApiMethodInvokeException))
			&& HasErrorCode(x, error.ErrorCode));

		if (vkApiMethodInvokeExceptions is null)
		{
			return new(error);
		}

		var exception = PerformanceActivator.CreateInstance<VkApiMethodInvokeException>(vkApiMethodInvokeExceptions, Predicate(), error);

		return exception ?? new VkApiMethodInvokeException(error);
	}

	private static Predicate<ConstructorInfo> Predicate() => x => Array.Exists(x.GetParameters(), p => p.ParameterType == typeof(VkError));

	private static bool HasErrorCode(MemberInfo x, int errorCode) =>
		((VkErrorAttribute) Attribute.GetCustomAttribute(x, typeof(VkErrorAttribute))).ErrorCode == errorCode;
}