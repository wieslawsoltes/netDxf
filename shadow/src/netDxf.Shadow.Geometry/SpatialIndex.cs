using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>Immutable packed BVH. Query results are original input indices, not draw-order sorted.</summary>
public sealed class SpatialIndex
{
    private readonly record struct Node(Bounds2 Bounds, int Left, int Right, int Start, int Count);
    private readonly ImmutableArray<Node> nodes;
    private readonly ImmutableArray<int> order;
    private readonly ImmutableArray<Bounds2> bounds;

    public SpatialIndex(IEnumerable<Bounds2> source, int leafSize = 8)
    {
        if (leafSize < 1) throw new ArgumentOutOfRangeException(nameof(leafSize));
        bounds = source.ToImmutableArray();
        var indices = Enumerable.Range(0, bounds.Length).Where(i => !bounds[i].IsEmpty).ToArray();
        var storage = new List<Node>();
        if (indices.Length > 0) Build(0, indices.Length);
        nodes = storage.ToImmutableArray();
        order = indices.ToImmutableArray();

        int Build(int start, int count)
        {
            var box = Bounds2.Empty;
            for (var i = start; i < start + count; i++) box = box.Union(bounds[indices[i]]);
            var index = storage.Count;
            storage.Add(default);
            if (count <= leafSize) storage[index] = new(box, -1, -1, start, count);
            else
            {
                var middle = start + count / 2;
                var x = box.Width >= box.Height;
                Select(start, start + count - 1, middle, x);
                var left = Build(start, middle - start);
                var right = Build(middle, start + count - middle);
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
            if (!node.Bounds.Intersects(rectangle)) continue;
            if (node.Count > 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var item = order[i];
                    if (bounds[item].Intersects(rectangle)) result.Add(item);
                }
            }
            else { stack.Push(node.Left); stack.Push(node.Right); }
        }
        return result.ToImmutable();
    }
}
