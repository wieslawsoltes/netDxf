using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.Shadow.NetDxf;

public sealed record ShadowCaptureOptions(int MaximumObjects = 2_000_000, int MaximumCollectionItems = 10_000_000,
    int MaximumValueDepth = 32, bool FailOnUnreadableProperty = false);

/// <summary>Captures reachable public instance state without retaining source objects in snapshots.</summary>
internal sealed class ShadowCapture
{
    private sealed record Identity(ObjectId Id)
    {
        public ShadowValue.Reference Reference { get; } = new(Id);
    }
    private readonly ConditionalWeakTable<object, Identity> identities = new();
    private readonly ShadowCapturePropertyCache propertyCache = new();
    private readonly ShadowCaptureOptions options;
    private readonly Guid documentId = Guid.NewGuid();
    private long nextId;
    private Dictionary<ObjectId, object> sources = new();
    private Queue<(ObjectId Id, object Source)> pending = new();
    private HashSet<ObjectId> scheduled = new();
    private ImmutableDictionary<ObjectId, ShadowObject>.Builder nodes = null!;
    private ImmutableArray<ShadowDiagnostic>.Builder diagnostics = null!;
    private List<ObjectId>? addedSources;
    private int changedObjects;
    private HashSet<ObjectId> changedIds = new();
    private bool incremental;

    public ShadowCapture(ShadowCaptureOptions options)
    {
        if (options.MaximumObjects < 1 || options.MaximumCollectionItems < 1 || options.MaximumValueDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        this.options = options;
    }

    public object Source(ObjectId id) => sources.TryGetValue(id, out var source) ? source : throw new ShadowConflictException($"Source object {id} no longer exists.");

    internal Dictionary<ObjectId, object> RetainBindings() => new(sources);
    internal void Bind(ObjectId id, object value)
    {
        if (identities.TryGetValue(value, out var previous))
        {
            if (previous.Id != id) throw new ShadowConflictException($"Source object is already bound to {previous.Id}, not {id}.");
        }
        else identities.Add(value, new Identity(id));
        sources[id] = value;
    }

    private Identity IdentityFor(object value)
    {
        if (identities.TryGetValue(value, out var identity)) return identity;
        var handle = (value as DxfObject)?.Handle;
        var id = !string.IsNullOrEmpty(handle) && ulong.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)
            ? new ObjectId("H:" + number.ToString("X", CultureInfo.InvariantCulture))
            : new ObjectId("O:" + Interlocked.Increment(ref nextId).ToString(CultureInfo.InvariantCulture));
        identity = new(id);
        identities.Add(value, identity);
        return identity;
    }

    private ObjectId Identify(object value) => IdentityFor(value).Id;

    private ShadowValue Reference(object value)
    {
        var identity = IdentityFor(value);
        var id = identity.Id;
        if (sources.TryGetValue(id, out var existing) && !ReferenceEquals(existing, value))
            throw new InvalidOperationException($"Duplicate source identity {id}; DXF handles must be unique.");
        if (existing is null)
        {
            sources[id] = value;
            addedSources?.Add(id);
        }
        if ((!incremental || !nodes.ContainsKey(id)) && scheduled.Add(id)) pending.Enqueue((id, value));
        return identity.Reference;
    }

    public ShadowSnapshot Capture(DxfDocument document, ShadowSnapshot? previous, IReadOnlyCollection<ObjectId>? dirty = null)
    {
        changedObjects = 0;
        changedIds = new();
        incremental = previous is not null && dirty is not null;
        var oldSources = sources;
        sources = incremental ? oldSources : new();
        addedSources = incremental ? new() : null;
        pending = new();
        scheduled = new();
        nodes = previous?.Objects.ToBuilder() ?? ImmutableDictionary.CreateBuilder<ObjectId, ShadowObject>();
        diagnostics = ImmutableArray.CreateBuilder<ShadowDiagnostic>();
        if (incremental)
        {
            var dirtySet = dirty!.ToHashSet();
            diagnostics.AddRange(previous!.Diagnostics.Where(d => d.Object is null || !dirtySet.Contains(d.Object.Value)));
            foreach (var id in dirtySet)
            {
                if (!oldSources.TryGetValue(id, out var source)) throw new ShadowConflictException($"Unknown dirty object {id}.");
                scheduled.Add(id);
                pending.Enqueue((id, source));
            }
        }
        else Reference(document);
        var root = Identify(document);
        try
        {
            while (pending.TryDequeue(out var entry))
            {
                if (sources.Count > options.MaximumObjects) throw new InvalidOperationException("Shadow object budget exceeded.");
                var sourceType = entry.Source.GetType();
                var cacheKey = ShadowCapturePropertyCache.Key(entry.Source, sourceType);
                if (!propertyCache.TryGet(cacheKey, out var fields))
                {
                    var properties = ImmutableDictionary.CreateBuilder<string, ShadowValue>(StringComparer.Ordinal);
                    foreach (var accessor in SourceAccessors.For(sourceType))
                    {
                        try { properties[accessor.Name] = ReadValue(accessor.Get(entry.Source), 0); }
                        catch (Exception error)
                        {
                            if (options.FailOnUnreadableProperty) throw;
                            var message = error.GetBaseException().Message;
                            properties[accessor.Name] = new ShadowValue.Fault(message);
                            diagnostics.Add(new("capture.property", accessor.Name + ": " + message, entry.Id));
                        }
                    }
                    if (entry.Source is Ole2Frame ole && OleRasterPreview.Read(ole) is { } preview and not ShadowValue.NullValue)
                        properties[OleRasterPreview.Property] = preview;
                    if (entry.Source is IEnumerable enumerable)
                        properties["$items"] = ReadSequence(enumerable, 0);
                    fields = properties.ToImmutable();
                    propertyCache.Add(cacheKey, fields);
                }
                var kind = entry.Source switch
                {
                    EntityObject => ShadowObjectKind.Entity,
                    DxfDocument => ShadowObjectKind.Document,
                    netDxf.Blocks.Block => ShadowObjectKind.Block,
                    netDxf.Objects.Layout => ShadowObjectKind.Layout | ShadowObjectKind.Table,
                    TableObject => ShadowObjectKind.Table,
                    _ => ShadowObjectKind.Object
                };
                var type = sourceType.FullName!;
                if (previous is not null && previous.Objects.TryGetValue(entry.Id, out var old) && old.TypeName == type && ShadowValue.Equivalent(old.Properties, fields))
                    nodes[entry.Id] = old;
                else
                {
                    nodes[entry.Id] = new(entry.Id, type, kind, fields, SourceAccessors.WritableFor(sourceType));
                    changedObjects++; changedIds.Add(entry.Id);
                }
            }
            // A full refresh still visits every reachable source. Starting from the
            // previous immutable map avoids rebuilding its tree for unchanged nodes;
            // prune only after the traversal has established the complete reachability.
            if (!incremental && previous is not null)
                foreach (var id in previous.Objects.Keys)
                    if (!sources.ContainsKey(id)) nodes.Remove(id);
            var objects = nodes.ToImmutable();
            var issues = diagnostics.ToImmutable();
            var same = previous is not null && previous.Objects.Count == objects.Count && changedObjects == 0 && previous.Diagnostics.SequenceEqual(issues);
            if (same) return previous!;
            var entities = incremental ? previous!.EntityIds : objects.Values.Where(n => n.Kind.HasFlag(ShadowObjectKind.Entity)).Select(n => n.Id).ToImmutableHashSet();
            var next = new ShadowSnapshot(documentId, previous is null ? 0 : checked(previous.Revision + 1), root, objects, entities, issues);
            if (previous is not null)
                ShadowChangeJournal.Record(previous, next,
                    changedIds.Where(id => !previous.Objects.ContainsKey(id)),
                    changedIds.Where(previous.Objects.ContainsKey),
                    incremental ? [] : previous.Objects.Keys.Where(id => !objects.ContainsKey(id)));
            return next;
        }
        catch
        {
            if (incremental && addedSources is not null)
                foreach (var id in addedSources) sources.Remove(id);
            sources = oldSources;
            throw;
        }
        finally { addedSources = null; }
    }

    public ShadowValue Preview(object? value)
    {
        // Preview runs only at the serialized source boundary. References may be registered,
        // but a later full capture prunes anything not reachable from the document.
        incremental = true;
        return ReadValue(value, 0);
    }

    private ShadowValue ReadValue(object? value, int depth)
    {
        if (value is null) return ShadowValue.Null;
        if (depth > options.MaximumValueDepth) throw new InvalidOperationException("Shadow value nesting budget exceeded.");
        var type = value.GetType();
        if (value is bool boolean) return ShadowCaptureValues.Boolean(boolean);
        if (value is double number) return ShadowCaptureValues.Double(number);
        if (value is Vector2 vector2)
        {
            if (depth == options.MaximumValueDepth) throw new InvalidOperationException("Shadow value nesting budget exceeded.");
            return ShadowCaptureValues.Vector(vector2);
        }
        if (value is Vector3 vector3)
        {
            if (depth == options.MaximumValueDepth) throw new InvalidOperationException("Shadow value nesting budget exceeded.");
            return ShadowCaptureValues.Vector(vector3);
        }
        if (value is string text) return ShadowValue.From(text);
        if (ReferenceEquals(value, StringComparer.Ordinal)) return new ShadowValue.Atom("System.StringComparer", "Ordinal");
        if (ReferenceEquals(value, StringComparer.OrdinalIgnoreCase)) return new ShadowValue.Atom("System.StringComparer", "OrdinalIgnoreCase");
        if (type.IsEnum || type.IsPrimitive || value is decimal or Guid or DateTime or DateTimeOffset or TimeSpan)
        {
            var representation = value switch
            {
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            return new ShadowValue.Atom(type.FullName!, representation ?? string.Empty);
        }
        // ExtendedFontData is an immutable value returned as a fresh instance by its
        // public getter. Source identity would make every unchanged capture differ.
        if (type.IsValueType || value is TextStyleFontData)
        {
            var fields = ImmutableDictionary.CreateBuilder<string, ShadowValue>(StringComparer.Ordinal);
            foreach (var accessor in SourceAccessors.For(type)) fields[accessor.Name] = ReadValue(accessor.Get(value), depth + 1);
            // A value-type getter may mutate an object referenced by a public field.
            // Preserve the generic path's final field read after all getters run;
            // an earlier detached field value can otherwise become stale mid-capture.
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                fields[field.Name] = ReadValue(field.GetValue(value), depth + 1);
            return new ShadowValue.Structure(type.FullName!, fields.ToImmutable());
        }
        // Arrays belong to their element assembly, but are values/collections, not model
        // identities. Array.Empty<T>() may be shared by unrelated owners; assigning it an
        // identity makes independent entity copies collide on read-only empty arrays.
        if (value is Array array) return ReadSequence(array, depth + 1);
        if (type.Assembly == typeof(DxfDocument).Assembly || value is EntityObject) return Reference(value);
        if (value is IEnumerable sequence) return ReadSequence(sequence, depth + 1);
        throw new NotSupportedException($"External object type {type.FullName} cannot be detached automatically.");
    }

    private ShadowValue.Sequence ReadSequence(IEnumerable enumerable, int depth)
    {
        var values = ImmutableArray.CreateBuilder<ShadowValue>();
        foreach (var item in enumerable)
        {
            if (values.Count >= options.MaximumCollectionItems) throw new InvalidOperationException("Shadow collection budget exceeded.");
            values.Add(ReadValue(item, depth + 1));
        }
        return new(values.ToImmutable());
    }
}
