using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>Immutable packed BVH. Query results are original input indices, not draw-order sorted.</summary>
public sealed class SpatialIndex
{
    private readonly record struct Node(Bounds2 Bounds, int Left, int Right, int Start, int Count);
    private readonly ImmutableArray<Node> nodes;
    private readonly ImmutableArray<int> order;
    private readonly ImmutableArray<Bounds2> bounds;
    private readonly ImmutableArray<int> parents;
    private readonly ImmutableArray<int> leaves;
    private readonly ImmutableDictionary<int, Bounds2> itemOverrides = ImmutableDictionary<int, Bounds2>.Empty;
    private readonly ImmutableDictionary<int, Bounds2> nodeOverrides = ImmutableDictionary<int, Bounds2>.Empty;
    private readonly int refitCount;
    private readonly int leafSize;
    public Bounds2 Bounds => nodes.IsEmpty ? Bounds2.Empty : NodeBounds(0);
    public int RefitCount => refitCount;

    private SpatialIndex(SpatialIndex previous, ImmutableDictionary<int, Bounds2> items, ImmutableDictionary<int, Bounds2> nodeBounds)
    {
        nodes = previous.nodes; order = previous.order; bounds = previous.bounds;
        parents = previous.parents; leaves = previous.leaves; leafSize = previous.leafSize;
        itemOverrides = items; nodeOverrides = nodeBounds; refitCount = previous.refitCount + 1;
    }

    /// <summary>
    /// Path-copies only changed bounds and ancestor bounds; packed topology is shared.
    /// Rebuilds after 128 refits or when empty/nonempty membership changes. Caller indices
    /// must retain their meaning across revisions. The original index remains unchanged.
    /// </summary>
    public SpatialIndex Refit(IEnumerable<KeyValuePair<int, Bounds2>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var items = itemOverrides.ToBuilder();
        var affected = new HashSet<int>();
        var rebuild = refitCount >= 128;
        var changed = false;
        var seen = new HashSet<int>();
        foreach (var (index, box) in changes)
        {
            if ((uint)index >= (uint)bounds.Length) throw new ArgumentOutOfRangeException(nameof(changes));
            if (!seen.Add(index)) throw new ArgumentException("Each input index may be refitted only once per batch.", nameof(changes));
            if (!box.IsEmpty && !(double.IsFinite(box.MinX) && double.IsFinite(box.MinY) && double.IsFinite(box.MaxX) && double.IsFinite(box.MaxY)))
                throw new ArgumentException("Nonempty spatial bounds must be finite.", nameof(changes));
            var before = ItemBounds(index);
            if (before == box) continue;
            changed = true;
            rebuild |= before.IsEmpty != box.IsEmpty;
            if (bounds[index] == box) items.Remove(index); else items[index] = box;
            for (var node = leaves[index]; node >= 0 && affected.Add(node); node = parents[node]) { }
        }
        if (!changed) return this;
        Bounds2 Item(int i) => items.TryGetValue(i, out var box) ? box : bounds[i];
        if (rebuild) return new SpatialIndex(Enumerable.Range(0, bounds.Length).Select(Item), leafSize);
        var overrides = nodeOverrides.ToBuilder();
        Bounds2 NodeBox(int i) => overrides.TryGetValue(i, out var box) ? box : nodes[i].Bounds;
        foreach (var index in affected.OrderDescending())
        {
            var node = nodes[index];
            var box = Bounds2.Empty;
            if (node.Count == 0) box = NodeBox(node.Left).Union(NodeBox(node.Right));
            else for (var i = node.Start; i < node.Start + node.Count; i++) box = box.Union(Item(order[i]));
            if (box == node.Bounds) overrides.Remove(index); else overrides[index] = box;
        }
        return new(this, items.ToImmutable(), overrides.ToImmutable());
    }
    private Bounds2 ItemBounds(int index) => itemOverrides.TryGetValue(index, out var box) ? box : bounds[index];
    private Bounds2 NodeBounds(int index) => nodeOverrides.TryGetValue(index, out var box) ? box : nodes[index].Bounds;

    public SpatialIndex(IEnumerable<Bounds2> source, int leafSize = 8)
    {
        if (leafSize < 1) throw new ArgumentOutOfRangeException(nameof(leafSize));
        this.leafSize = leafSize;
        bounds = source.ToImmutableArray();
        var parentStorage = new List<int>();
        var leafStorage = Enumerable.Repeat(-1, bounds.Length).ToArray();
        var indices = Enumerable.Range(0, bounds.Length).Where(i => !bounds[i].IsEmpty).ToArray();
        var storage = new List<Node>();
        if (indices.Length > 0) Build(0, indices.Length, -1);
        nodes = storage.ToImmutableArray();
        order = indices.ToImmutableArray();
        parents = parentStorage.ToImmutableArray();
        leaves = leafStorage.ToImmutableArray();

        int Build(int start, int count, int parent)
        {
            var box = Bounds2.Empty;
            for (var i = start; i < start + count; i++) box = box.Union(bounds[indices[i]]);
            var index = storage.Count;
            storage.Add(default); parentStorage.Add(parent);
            if (count <= leafSize)
            {
                storage[index] = new(box, -1, -1, start, count);
                for (var i = start; i < start + count; i++) leafStorage[indices[i]] = index;
            }
            else
            {
                var middle = start + count / 2;
                var x = box.Width >= box.Height;
                Select(start, start + count - 1, middle, x);
                var left = Build(start, middle - start, index);
                var right = Build(middle, start + count - middle, index);
                storage[index] = new(box, left, right, 0, 0);
            }
            return index;
        }
        void Select(int left, int right, int nth, bool x)
        {
            var budget = 2 * (int)Math.Log2(right - left + 1) + 2;
            int Compare(int a, int b)
            {
                var first = bounds[a].Center; var second = bounds[b].Center;
                var comparison = (x ? first.X : first.Y).CompareTo(x ? second.X : second.Y);
                return comparison == 0 ? a.CompareTo(b) : comparison;
            }
            while (left < right)
            {
                if (--budget <= 0) { Array.Sort(indices, left, right - left + 1, Comparer<int>.Create(Compare)); return; }
                var pivot = indices[left + (right - left) / 2];
                var i = left; var j = right;
                while (i <= j)
                {
                    while (Compare(indices[i], pivot) < 0) i++;
                    while (Compare(indices[j], pivot) > 0) j--;
                    if (i <= j) { (indices[i], indices[j]) = (indices[j], indices[i]); i++; j--; }
                }
                if (nth <= j) right = j;
                else if (nth >= i) left = i;
                else return;
            }
        }
    }

    public ImmutableArray<int> Query(Bounds2 rectangle)
    {
        if (nodes.IsEmpty || rectangle.IsEmpty) return [];
        var result = ImmutableArray.CreateBuilder<int>();
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.TryPop(out var index))
        {
            var node = nodes[index];
            if (!NodeBounds(index).Intersects(rectangle)) continue;
            if (node.Count > 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var item = order[i];
                    if (ItemBounds(item).Intersects(rectangle)) result.Add(item);
                }
            }
            else { stack.Push(node.Left); stack.Push(node.Right); }
        }
        return result.ToImmutable();
    }
}
