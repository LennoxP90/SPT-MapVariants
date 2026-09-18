using System;
using System.Linq;
using System.Reflection;

namespace MapVariants.Client.Api
{
    /// <summary>
    /// Logs the public surface at load, exactly as a consumer reading it by
    /// reflection would see it. There is no compile-time reference, so a renamed
    /// member breaks silently in someone else's mod weeks later, not at build.
    /// </summary>
    internal static class ApiSelfCheck
    {
        internal static void Run()
        {
            try
            {
                var type = typeof(Maps);

                var constants = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.IsLiteral)
                    .Select(field => $"{field.Name}={field.GetRawConstantValue()}");

                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .Select(property => $"{property.Name}:{property.PropertyType.Name}");

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(method => !method.IsSpecialName)
                    .Select(method => $"{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name))})");

                var events = type.GetEvents(BindingFlags.Public | BindingFlags.Static).Select(e => e.Name);

                Plugin.Log.LogInfo($"[MV Api] {type.FullName} v{Maps.ApiVersion}");
                Plugin.Log.LogInfo($"[MV Api]   const: {string.Join(", ", constants)}");
                Plugin.Log.LogInfo($"[MV Api]   props: {string.Join(", ", properties)}");
                Plugin.Log.LogInfo($"[MV Api]   methods: {string.Join(", ", methods)}");
                Plugin.Log.LogInfo($"[MV Api]   events: {string.Join(", ", events)}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[MV Api] self check failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
