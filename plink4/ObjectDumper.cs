using plink4;
using System;
using System.Collections;
using System.Reflection;

internal static class ObjectDumper
{
    public static void Dump(string label, object obj)
    {
        Logger.Info("===== DUMP START: " + label + " =====");
        DumpObject(obj, 0);
        Logger.Info("===== DUMP END =====");
    }

    private static void DumpObject(object obj, int depth)
    {
        if (obj == null)
        {
            Logger.Info(Indent(depth) + "null");
            return;
        }

        Type t = obj.GetType();

        // Simple types
        if (t.IsPrimitive || obj is string || obj is decimal || obj is DateTime)
        {
            Logger.Info(Indent(depth) + obj.ToString());
            return;
        }

        // Arrays / collections
        if (obj is IEnumerable enumerable && !(obj is string))
        {
            foreach (var item in enumerable)
            {
                DumpObject(item, depth + 1);
            }
            return;
        }

        Logger.Info(Indent(depth) + "[" + t.FullName + "]");

        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                object value = p.GetValue(obj);

                if (value == null)
                {
                    Logger.Info(Indent(depth + 1) + p.Name + " = null");
                    continue;
                }

                Type vt = value.GetType();

                if (vt.IsPrimitive || value is string || value is decimal || value is DateTime)
                {
                    Logger.Info(Indent(depth + 1) + p.Name + " = " + value);
                }
                else
                {
                    Logger.Info(Indent(depth + 1) + p.Name + ":");
                    DumpObject(value, depth + 2);
                }
            }
            catch (Exception ex)
            {
                Logger.Info(Indent(depth + 1) + p.Name + " = ERROR: " + ex.Message);
            }
        }
    }

    private static string Indent(int depth)
    {
        return new string(' ', depth * 2);
    }
}