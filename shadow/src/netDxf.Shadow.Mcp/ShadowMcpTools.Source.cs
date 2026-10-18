using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Mcp;

public sealed partial class ShadowMcpTools
{
    private object SourceInspect(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var source = context.Source?.Invoke() ?? throw new NotSupportedException("This host has no mutable netDxf source adapter.");
        var handle = GetString(args, "handle"); var path = GetString(args, "path") ?? "";
        var depth = GetInt(args, "depth", 1); var offset = GetInt(args, "offset", 0); var limit = GetInt(args, "limit", 32);
        var textOffset = GetInt(args, "textOffset", 0); var textLimit = GetInt(args, "textLimit", 4096);
        return source.ReadSource(document =>
        {
            object? current = handle is null ? document : document.GetObjectByHandle(handle.StartsWith("H:", StringComparison.Ordinal) ? handle[2..] : handle)
                ?? throw new ArgumentException("Unknown DXF source handle.");
            foreach (var segment in ShadowJson.Segments(path))
            {
                token.ThrowIfCancellationRequested();
                if (current is null) throw new ArgumentException($"Cannot traverse '{segment}' through a null source value.");
                if (current is IEnumerable items && current is not string && ShadowJson.Index(segment, out var index))
                {
                    if (index > 1_000_000) throw new ArgumentException("Source collection indices are bounded to one million; use captured shadow collections for larger indexed access.");
                    var enumerator = items.GetEnumerator();
                    try
                    {
                        var found = false;
                        for (var i = 0; i <= index; i++)
                        {
                            if ((i & 127) == 0) token.ThrowIfCancellationRequested();
                            if (!enumerator.MoveNext()) { found = false; break; }
                            if (i == index) { current = enumerator.Current; found = true; }
                        }
                        if (!found) throw new ArgumentException("Source collection index is out of range.");
                    }
                    finally { (enumerator as IDisposable)?.Dispose(); }
                }
                else
                {
                    var property = DxfTypeCatalog.PublicProperties(current.GetType()).FirstOrDefault(p => p.Name == segment)
                        ?? throw new ArgumentException($"Unknown public source property '{segment}'.");
                    current = property.GetValue(current);
                }
            }
            var remaining = 512; var textRemaining = 32768;
            return new
            {
                sourceDocumentId = source.Current.DocumentId, sourceRevision = source.Current.Revision, handle, path,
                value = DetachSource(current, depth, offset, limit, textOffset, textLimit, new HashSet<object>(ReferenceEqualityComparer.Instance), ref remaining, ref textRemaining, token)
            };
        });
    }

    private static object? DetachSource(object? source, int depth, int offset, int limit, int textOffset, int textLimit,
        HashSet<object> visiting, ref int budget, ref int textBudget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source is null) return null;
        var type = source.GetType();
        if (--budget < 0) return new { type = type.FullName, collapsed = true, reason = "response budget; inspect a narrower path" };
        if (source is string text)
        {
            var start = Math.Min(textOffset, text.Length);
            var count = Math.Min(text.Length - start, Math.Min(textLimit, textBudget)); textBudget -= count;
            return new { type = "System.String", length = text.Length, textOffset, value = text.Substring(start, count),
                nextTextOffset = start + count < text.Length ? (int?)(start + count) : null,
                collapsed = count == 0 && start < text.Length };
        }
        if (type.IsPrimitive || type.IsEnum || source is decimal or DateTime or DateTimeOffset or Guid or TimeSpan)
            return new { type = type.FullName, value = Convert.ToString(source, CultureInfo.InvariantCulture) };
        var handle = (source as DxfObject)?.Handle;
        if (!type.IsValueType && !visiting.Add(source)) return new { type = type.FullName, handle, cycle = true };
        try
        {
            if (depth == 0) return new { type = type.FullName, handle, collapsed = true };
            if (source is IEnumerable sequence)
            {
                var output = new List<object?>(limit); var count = 0; var hasMore = false;
                var enumerator = sequence.GetEnumerator();
                try
                {
                    while (enumerator.MoveNext())
                    {
                        if ((count & 127) == 0) token.ThrowIfCancellationRequested();
                        if (count++ < offset) continue;
                        if (output.Count == limit) { hasMore = true; break; }
                        output.Add(DetachSource(enumerator.Current, depth - 1, 0, Math.Min(limit, 8), 0, Math.Min(textLimit, 1024), visiting, ref budget, ref textBudget, token));
                    }
                }
                finally { (enumerator as IDisposable)?.Dispose(); }
                return new { type = type.FullName, count = source is ICollection collection ? (int?)collection.Count : null, offset, items = output, nextOffset = hasMore ? (int?)(offset + output.Count) : null };
            }
            var properties = DxfTypeCatalog.PublicProperties(type);
            var detached = new List<object>(Math.Min(limit, properties.Length));
            foreach (var property in properties.Skip(offset).Take(limit))
            {
                object? value;
                try { value = DetachSource(property.GetValue(source), depth - 1, 0, Math.Min(limit, 8), 0, Math.Min(textLimit, 1024), visiting, ref budget, ref textBudget, token); }
                catch (TargetInvocationException error) { value = new { error = error.GetBaseException().Message }; }
                detached.Add(new { name = property.Name, type = property.ValueType.FullName, writable = property.CanWrite, value });
            }
            return new { type = type.FullName, handle, offset, properties = detached, total = properties.Length, nextOffset = offset + detached.Count < properties.Length ? (int?)(offset + detached.Count) : null };
        }
        finally { if (!type.IsValueType) visiting.Remove(source); }
    }
}
