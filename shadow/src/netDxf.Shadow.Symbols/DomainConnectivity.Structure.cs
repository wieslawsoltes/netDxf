using System.Collections.Immutable;
using netDxf.Shadow.Graph;

namespace netDxf.Shadow.Symbols;

public sealed partial class DomainConnectivity
{
    /// <summary>
    /// Directed SCC/condensation, weak cut links/ports and one bounded connection-distinct loop witness per SCC.
    /// Uses declarations as authored. No geometric intersection, flow, pressure or switching state is inferred.
    /// </summary>
    public DomainConnectivityStructure AnalyzeStructure(DomainStructureOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.MaximumLoops is < 1 or > 100 || options.MaximumProofSteps is < 2 or > 10000 || options.MaximumSearchWork is < 1 or > 1000000)
            throw new ArgumentOutOfRangeException(nameof(options));
        var token = cancellationToken; token.ThrowIfCancellationRequested();
        var positions = Nodes.Select((node, i) => (node.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
        var arcs = ImmutableArray.CreateBuilder<IndexedGraphArc>();
        var physical = ImmutableArray.CreateBuilder<IndexedGraphConnection>();
        var outgoing = Enumerable.Range(0, Nodes.Length).Select(_ => new List<(int Target, int Edge)>()).ToArray();
        for (var i = 0; i < Edges.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var edge = Edges[i]; var a = positions[edge.Source]; var b = positions[edge.Target];
            physical.Add(new(a, b));
            if (edge.Direction is "forward" or "both") { arcs.Add(new(a, b)); outgoing[a].Add((b, i)); }
            if (edge.Direction is "reverse" or "both") { arcs.Add(new(b, a)); outgoing[b].Add((a, i)); }
        }
        var strong = IndexedGraphAnalysis.StrongComponents(Nodes.Length, arcs.ToImmutable(), token);
        var cuts = IndexedGraphAnalysis.LowLinks(Nodes.Length, physical.ToImmutable(), token);
        var bridges = cuts.Bridges.ToHashSet(); var articulation = cuts.ArticulationPoints.ToHashSet();
        var portCounts = new int[strong.ComponentCount]; var internalCounts = new int[strong.ComponentCount];
        var candidates = Enumerable.Range(0, strong.ComponentCount).Select(_ => new List<(int Source, int Target, int Edge)>()).ToArray();
        var condensation = new Dictionary<(int Source, int Target), int>();
        for (var i = 0; i < Nodes.Length; i++) portCounts[strong.Membership[i]]++;
        for (var i = 0; i < Edges.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var edge = Edges[i]; var a = positions[edge.Source]; var b = positions[edge.Target];
            var groupA = strong.Membership[a]; var groupB = strong.Membership[b];
            if (groupA == groupB)
            {
                internalCounts[groupA]++;
                if (bridges.Contains(i)) continue; // A weak bridge cannot participate in a connection-distinct loop.
                if (edge.Direction is "forward" or "both") candidates[groupA].Add((a, b, i));
                if (edge.Direction is "reverse" or "both") candidates[groupA].Add((b, a, i));
            }
            else
            {
                var link = edge.Direction == "reverse" ? (Source: groupB, Target: groupA) : (Source: groupA, Target: groupB);
                condensation[link] = condensation.GetValueOrDefault(link) + 1;
            }
        }
        var loops = ImmutableArray.CreateBuilder<DomainLoop>(); var steps = ImmutableArray.CreateBuilder<DomainLoopStep>();
        var groups = ImmutableArray.CreateBuilder<DomainStrongComponent>(); var work = 0;
        var loopLimit = false; var proofLimit = false; var searchLimit = false;
        // Reuse bounded scratch arrays across return-path searches; never clear/allocate O(V) for each candidate.
        var seen = new int[Nodes.Length]; var previous = new int[Nodes.Length]; var previousEdge = new int[Nodes.Length];
        var queue = new int[Nodes.Length]; var stamp = 0;
        for (var group = 0; group < strong.ComponentCount; group++)
        {
            token.ThrowIfCancellationRequested(); var status = "absent";
            foreach (var candidate in candidates[group])
            {
                token.ThrowIfCancellationRequested();
                if (loops.Count == options.MaximumLoops) { loopLimit = true; status = "incomplete"; break; }
                if (steps.Count == options.MaximumProofSteps) { proofLimit = true; status = "incomplete"; break; }
                if (work == options.MaximumSearchWork) { searchLimit = true; status = "incomplete"; break; }
                work++; stamp++; var head = 0; var tail = 0; queue[tail++] = candidate.Target; seen[candidate.Target] = stamp;
                var found = candidate.Source == candidate.Target;
                while (head < tail && !found)
                {
                    token.ThrowIfCancellationRequested(); var node = queue[head++];
                    foreach (var adjacent in outgoing[node])
                    {
                        token.ThrowIfCancellationRequested();
                        if (work == options.MaximumSearchWork) { searchLimit = true; break; }
                        work++;
                        if (adjacent.Edge == candidate.Edge || strong.Membership[adjacent.Target] != group || seen[adjacent.Target] == stamp) continue;
                        seen[adjacent.Target] = stamp; previous[adjacent.Target] = node; previousEdge[adjacent.Target] = adjacent.Edge;
                        queue[tail++] = adjacent.Target;
                        if (adjacent.Target == candidate.Source) { found = true; break; }
                    }
                    if (searchLimit) break;
                }
                if (!found)
                {
                    if (searchLimit) { status = "incomplete"; break; }
                    continue;
                }
                var chain = new List<(int Source, int Target, int Edge)>(); var proofTooLong = false;
                for (var node = candidate.Source; node != candidate.Target; node = previous[node])
                {
                    token.ThrowIfCancellationRequested();
                    if (chain.Count + 1 >= options.MaximumProofSteps - steps.Count) { proofLimit = true; proofTooLong = true; break; }
                    chain.Add((previous[node], node, previousEdge[node]));
                }
                if (proofTooLong) { status = "incomplete"; break; }
                chain.Reverse(); chain.Insert(0, candidate); var loop = loops.Count;
                loops.Add(new(loop, group, chain.Count));
                for (var step = 0; step < chain.Count; step++)
                {
                    token.ThrowIfCancellationRequested(); var item = chain[step];
                    steps.Add(new(loop + ":" + step, loop, step, Nodes[item.Source].Id, Nodes[item.Target].Id, Edges[item.Edge].Id));
                }
                status = "found"; break;
            }
            groups.Add(new(group, portCounts[group], internalCounts[group], status));
        }
        return new(Complete && DeclarationsComplete, loopLimit, proofLimit, searchLimit, work, options, groups.ToImmutable(),
            Nodes.Select((n, i) => new DomainPortStructure(n.Id, strong.Membership[i], articulation.Contains(i))).ToImmutableArray(),
            condensation.OrderBy(p => p.Key.Source).ThenBy(p => p.Key.Target)
                .Select(p => new DomainCondensationLink(p.Key.Source + ">" + p.Key.Target, p.Key.Source, p.Key.Target, p.Value)).ToImmutableArray(),
            cuts.Bridges.Order().Select(i => new DomainCriticalConnection(Edges[i].Id, Edges[i].Source, Edges[i].Target)).ToImmutableArray(),
            loops.ToImmutable(), steps.ToImmutable());
    }
}
