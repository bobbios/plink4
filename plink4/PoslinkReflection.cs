using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace plink4
{
    internal static class PoslinkReflection
    {
        public static object RequireProperty(object obj, string propName, string errorIfNull)
        {
            var value = GetProperty(obj, propName);
            if (value == null) throw new Exception(errorIfNull);
            return value;
        }

        public static object CreateRequest(string operationName)
        {
            var type = FindTypeByNameContains(operationName, "Req")
                    ?? FindTypeByNameContains(operationName, "Request");

            if (type == null)
                throw new Exception($"{operationName} request type not found.");

            return Activator.CreateInstance(type);
        }

        public static object CreateResponse(string operationName)
        {
            var type = FindTypeByNameContains(operationName, "Rsp")
                    ?? FindTypeByNameContains(operationName, "Response");

            if (type == null)
                throw new Exception($"{operationName} response type not found.");

            return Activator.CreateInstance(type);
        }

        public static int InvokeTxMethod(object target, string methodName, object req, ref object rsp)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) &&
                    m.GetParameters().Length >= 2);

            if (method == null)
                throw new Exception($"{methodName} method not found.");

            object[] args = { req, rsp };
            var execResult = method.Invoke(target, args);
            rsp = args[1];

            return GetErrorCodeInt(execResult) == 0 ? 0 : 1;
        }

        public static object GetProperty(object obj, string propName)
        {
            if (obj == null) return null;
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            return pi?.GetValue(obj);
        }

        public static object GetOrCreateProperty(object obj, string propName)
        {
            if (obj == null) return null;

            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return null;

            var value = pi.GetValue(obj);
            if (value != null) return value;

            if (!pi.CanWrite) return null;
            if (pi.PropertyType.GetConstructor(Type.EmptyTypes) == null) return null;

            value = Activator.CreateInstance(pi.PropertyType);
            pi.SetValue(obj, value);
            return value;
        }

        public static bool SetProperty(object obj, string propName, object value)
        {
            try
            {
                if (obj == null) return false;

                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null || !pi.CanWrite) return false;

                object finalValue = value;
                if (value != null)
                {
                    var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;

                    if (targetType.IsEnum)
                        finalValue = Enum.Parse(targetType, value.ToString(), true);
                    else if (!targetType.IsAssignableFrom(value.GetType()))
                        finalValue = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                }

                pi.SetValue(obj, finalValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnumProperty(object obj, string propName, params string[] candidates)
        {
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null || !pi.PropertyType.IsEnum) return false;

                foreach (var candidate in candidates)
                {
                    try
                    {
                        var enumValue = Enum.Parse(pi.PropertyType, candidate, true);
                        pi.SetValue(obj, enumValue);
                        return true;
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static Type FindTypeByNameContains(params string[] containsAll)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    var name = (t.FullName ?? "") + "|" + (t.Name ?? "");
                    bool match = containsAll.All(c =>
                        name.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (match) return t;
                }
            }

            return null;
        }

        public static bool TryCancelTerminal(object terminal)
        {
            try
            {
                if (terminal == null) return false;

                var method = terminal.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        m.Name.Equals("Cancel", StringComparison.OrdinalIgnoreCase) &&
                        m.GetParameters().Length == 0);

                if (method == null) return false;

                method.Invoke(terminal, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int GetErrorCodeInt(object execResult)
        {
            try
            {
                if (execResult == null) return -1;
                var method = execResult.GetType().GetMethod("GetErrorCode", BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return -1;
                return Convert.ToInt32(method.Invoke(execResult, null), CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
        }
    }
}
