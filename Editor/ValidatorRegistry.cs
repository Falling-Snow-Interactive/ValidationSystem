using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace Fsi.Validation
{
    /// <summary>
    /// Discovers and runs validators marked with <see cref="ValidationMethod" />.
    /// A validator must be <c>static</c>, parameterless, and return either:
    /// <list type="bullet">
    /// <item><description><c>bool</c> (true = pass, false = fail)</description></item>
    /// <item><description><see cref="ValidatorResult" /> (explicit pass/fail + message)</description></item>
    /// </list>
    /// </summary>
    public static class ValidatorRegistry
    {
        private static IReadOnlyList<MethodInfo> cachedMethods;

        public static IReadOnlyList<MethodInfo> GetValidatorMethods()
        {
            cachedMethods ??= BuildMethodCache();
            return cachedMethods;
        }

        private static IReadOnlyList<MethodInfo> BuildMethodCache()
        {
            try
            {
                return TypeCache.GetMethodsWithAttribute<ValidationMethod>()
                                .Where(IsValidatorMethod)
                                .ToList();
            }
            catch (Exception)
            {
                return DiscoverWithReflection();
            }
        }

        public static void ClearCache()
        {
            cachedMethods = null;
        }

        public static bool TryRunValidator(MethodInfo method, out ValidatorResult result)
        {
            if (!IsValidatorMethod(method))
            {
                result = ValidatorResult.Fail($"Invalid validator signature on {method?.DeclaringType?.FullName}.{method?.Name}.");
                return false;
            }

            object returnValue = method.Invoke(null, null);
            if (method.ReturnType == typeof(bool))
            {
                bool passed = returnValue is true;
                result = passed ? ValidatorResult.Pass() : ValidatorResult.Fail();
                return true;
            }

            result = (ValidatorResult)returnValue;
            return true;
        }

        private static IReadOnlyList<MethodInfo> DiscoverWithReflection()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(GetAssemblyMethods)
                            .Where(IsValidatorMethod)
                            .ToList();
        }

        private static IEnumerable<MethodInfo> GetAssemblyMethods(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes()
                               .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                               .Where(method => method.GetCustomAttribute<ValidationMethod>() != null);
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types
                         .Where(type => type != null)
                         .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                         .Where(method => method.GetCustomAttribute<ValidationMethod>() != null);
            }
        }

        private static bool IsValidatorMethod(MethodInfo method)
        {
            if (method == null || !method.IsStatic || method.GetParameters().Length != 0)
            {
                return false;
            }

            return method.ReturnType == typeof(bool) || method.ReturnType == typeof(ValidatorResult);
        }
    }

    public readonly struct ValidatorResult
    {
        public bool Passed { get; }
        public string Message { get; }

        private ValidatorResult(bool passed, string message = null)
        {
            Passed = passed;
            Message = message;
        }

        public static ValidatorResult Pass(string message = null) => new(true, message);

        public static ValidatorResult Fail(string message = null) => new(false, message);
    }
}
