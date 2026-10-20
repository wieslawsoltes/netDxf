using System.Collections.Immutable;

namespace netDxf.Shadow.Symbols;

public sealed record DomainStructureOptions(int MaximumLoops = 20, int MaximumProofSteps = 4000, int MaximumSearchWork = 100000);
public sealed record DomainStrongComponent(int Id, int PortCount, int ConnectionCount, string LoopStatus);
public sealed record DomainPortStructure(string Id, int StrongComponent, bool ArticulationPoint);
public sealed record DomainCondensationLink(string Id, int Source, int Target, int ConnectionCount);
public sealed record DomainCriticalConnection(string Id, string Source, string Target);
public sealed record DomainLoop(int Id, int StrongComponent, int StepCount);
public sealed record DomainLoopStep(string Id, int Loop, int Step, string Source, string Target, string Connection);

/// <summary>
/// Exact structure of the retained declared graph. Incomplete source scope can split strong components and
/// create apparent weak cut links. Loop witnesses use distinct connection IDs; reversing the same
/// bidirectional connection is not a loop. At most one witness is retained per strong component.
/// </summary>
public sealed class DomainConnectivityStructure
{
    public bool ScopeComplete { get; }
    public bool LoopSearchComplete { get; }
    public bool LoopLimitReached { get; }
    public bool ProofLimitReached { get; }
    public bool SearchLimitReached { get; }
    public int SearchWork { get; }
    public DomainStructureOptions Options { get; }
    public ImmutableArray<DomainStrongComponent> StrongComponents { get; }
    public ImmutableArray<DomainPortStructure> PortStructures { get; }
    public ImmutableArray<DomainCondensationLink> Condensation { get; }
    public ImmutableArray<DomainCriticalConnection> CriticalConnections { get; }
    public ImmutableArray<DomainLoop> Loops { get; }
    public ImmutableArray<DomainLoopStep> LoopSteps { get; }
    internal DomainConnectivityStructure(bool scopeComplete, bool loopLimit, bool proofLimit, bool searchLimit,
        int searchWork, DomainStructureOptions options, ImmutableArray<DomainStrongComponent> components,
        ImmutableArray<DomainPortStructure> ports, ImmutableArray<DomainCondensationLink> condensation,
        ImmutableArray<DomainCriticalConnection> critical, ImmutableArray<DomainLoop> loops, ImmutableArray<DomainLoopStep> steps)
    {
        ScopeComplete = scopeComplete; LoopLimitReached = loopLimit; ProofLimitReached = proofLimit;
        SearchLimitReached = searchLimit; SearchWork = searchWork; Options = options;
        StrongComponents = components; PortStructures = ports; Condensation = condensation;
        CriticalConnections = critical; Loops = loops; LoopSteps = steps;
        LoopSearchComplete = components.All(c => c.LoopStatus != "incomplete");
    }
    /// <summary>Closed, directed proof from first port back to itself, with a different connection at each step.</summary>
    public DomainConnectionProof LoopProof(int loop, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Loops.Any(l => l.Id == loop)) throw new ArgumentOutOfRangeException(nameof(loop));
        var nodes = ImmutableArray.CreateBuilder<string>(); var edges = ImmutableArray.CreateBuilder<string>();
        foreach (var step in LoopSteps)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (step.Loop != loop) continue;
            if (nodes.Count == 0) nodes.Add(step.Source);
            nodes.Add(step.Target); edges.Add(step.Connection);
        }
        return new(nodes.ToImmutable(), edges.ToImmutable());
    }
}
