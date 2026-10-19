namespace netDxf.Shadow.Studio;

public sealed record CadSaveRequest(long Revision, string Format = "dxf", string? Version = null);
