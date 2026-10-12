namespace netDxf.Shadow.NetDxf;

/// <summary>Controls source-state validation before nonempty property transactions.</summary>
public enum ShadowSourceValidationMode
{
    /// <summary>
    /// Compare the complete captured public graph with Current before any source write.
    /// Reject changes to other properties, objects, resources or collection membership.
    /// This is the default and performs a full capture even for a local scalar edit.
    /// It does not synchronize concurrent writers that bypass the session gate.
    /// </summary>
    FullGraph = 0,

    /// <summary>
    /// Compare only the properties being edited, preserving incremental scalar capture.
    /// Use only when all source mutations are exclusively owned and serialized through
    /// this session, or explicitly reconciled with Refresh before applying an edit.
    /// Unrelated out-of-session changes are not detected by a property transaction.
    /// Structural transactions always validate the full captured graph.
    /// </summary>
    EditedProperties = 1
}
