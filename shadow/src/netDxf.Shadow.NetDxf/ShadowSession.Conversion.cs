using System.Collections;
using System.Globalization;
using System.Reflection;

namespace netDxf.Shadow.NetDxf;

public sealed partial class ShadowSession
{
    private object? ConvertValue(ShadowValue value, Type target, Func<ObjectId, object>? resolve = null)
    {
        var nullable = Nullable.GetUnderlyingType(target);
        if (value is ShadowValue.NullValue)
        {
            if (target.IsValueType && nullable is null) throw new ArgumentException($"{target.Name} cannot be null.");
            return null;
        }
        target = nullable ?? target;
        if (value is ShadowValue.Reference reference)
        {
            var source = (resolve ?? capture.Source)(reference.Id);
            return target.IsInstanceOfType(source) ? source : throw new ArgumentException("Incompatible source reference.");
        }
        if (target == typeof(object)) return ConvertBoxedValue(value, resolve);
        if (value is ShadowValue.Atom atom)
        {
            if (target.IsEnum) return Enum.Parse(target, atom.Text, false);
            if (target == typeof(Guid)) return Guid.Parse(atom.Text);
            if (target == typeof(DateTime)) return DateTime.Parse(atom.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(atom.Text, CultureInfo.InvariantCulture);
            if (target == typeof(TimeSpan)) return TimeSpan.Parse(atom.Text, CultureInfo.InvariantCulture);
            var result = Convert.ChangeType(atom.Text, target, CultureInfo.InvariantCulture);
            if (result is double d && !double.IsFinite(d) || result is float f && !float.IsFinite(f))
                throw new ArgumentException("Edits must contain finite numbers.");
            return result;
        }
        if (value is ShadowValue.Structure { TypeName: "netDxf.Tables.TextStyleFontData" } fontData
            && target == typeof(netDxf.Tables.TextStyleFontData))
            return new netDxf.Tables.TextStyleFontData(
                (string)ConvertValue(fontData.Fields["FamilyName"], typeof(string), resolve)!,
                (int)ConvertValue(fontData.Fields["Flags"], typeof(int), resolve)!);
        if (value is ShadowValue.Structure structure && target.IsValueType)
        {
            double Number(string name) => structure.Fields.TryGetValue(name, out var field) ? Convert.ToDouble(ConvertValue(field, typeof(double), resolve), CultureInfo.InvariantCulture) : 0;
            if (target == typeof(Vector2)) return new Vector2(Number("X"), Number("Y"));
            if (target == typeof(Vector3)) return new Vector3(Number("X"), Number("Y"), Number("Z"));
            if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                var arguments = target.GetGenericArguments();
                return Activator.CreateInstance(target,
                    ConvertValue(structure.Fields["Key"], arguments[0], resolve),
                    ConvertValue(structure.Fields["Value"], arguments[1], resolve));
            }
            var result = Activator.CreateInstance(target)!;
            foreach (var (name, field) in structure.Fields)
            {
                var accessor = SourceAccessors.For(target).SingleOrDefault(a => a.Name == name && a.CanWrite)
                    ?? throw new NotSupportedException($"Cannot write {target.Name}.{name}.");
                accessor.Set(result, ConvertValue(field, accessor.ValueType, resolve));
            }
            return result;
        }
        if (value is ShadowValue.Sequence sequence)
        {
            var element = target.IsArray ? target.GetElementType() : target.GetInterfaces().Append(target)
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (element is null) throw new NotSupportedException($"Cannot construct collection {target.Name}.");
            var array = Array.CreateInstance(element, sequence.Items.Length);
            for (var i = 0; i < array.Length; i++) array.SetValue(ConvertValue(sequence.Items[i], element, resolve), i);
            if (target.IsArray) return array;
            var listType = typeof(List<>).MakeGenericType(element);
            if (!target.IsAssignableFrom(listType)) throw new NotSupportedException($"Collection {target.Name} needs a custom editor.");
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var item in array) list.Add(item);
            return list;
        }
        throw new NotSupportedException($"Cannot convert {value.GetType().Name} to {target.Name}.");
    }

}
