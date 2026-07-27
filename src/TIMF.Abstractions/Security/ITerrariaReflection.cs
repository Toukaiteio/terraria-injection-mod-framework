using System.Reflection;

namespace TIMF.Abstractions.Security
{
    /// <summary>
    /// Narrow reflection broker for compatibility with private Terraria methods. It refuses
    /// methods declared by the mod, framework libraries, BCL, or any other assembly.
    /// </summary>
    public interface ITerrariaReflection
    {
        object Invoke(MethodInfo method, object instance, object[] arguments);
        object GetFieldValue(FieldInfo field, object instance);
        void SetFieldValue(FieldInfo field, object instance, object value);
        object GetPropertyValue(PropertyInfo property, object instance, object[] index);
    }
}
