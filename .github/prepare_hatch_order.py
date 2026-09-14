from pathlib import Path
p = Path('netDxf/IO/DxfReader.cs')
raw=p.read_bytes();crlf=b'\r\n' in raw;s=raw.decode().replace('\r\n','\n')
old='''            this.ReadHatchPolylineFlag(72);
            this.ReadNextHatchPolylineTag();
            bool closed = this.ReadHatchPolylineFlag(73);
            this.ReadNextHatchPolylineTag();
            int vertexCount = this.ReadHatchPolylineCount(93);
            this.ReadNextHatchPolylineTag();'''
assert s.count(old)==1
s=s.replace(old,'''            bool closed;
            int vertexCount;
            this.ReadHatchPolylineHeader(out closed, out vertexCount);''')
start=s.index('                    case HatchBoundaryPath.EdgeType.Line:',s.index('private HatchBoundaryPath ReadEdgeBoundaryPath'))
end=s.index('                    case HatchBoundaryPath.EdgeType.Spline:',start)
s=s[:start]+'''                    case HatchBoundaryPath.EdgeType.Line:
                    case HatchBoundaryPath.EdgeType.Arc:
                    case HatchBoundaryPath.EdgeType.Ellipse:
                        edges.Add(this.ReadHatchScalarEdge((HatchBoundaryPath.EdgeType) kind));
                        break;
'''+s[end:]
start=s.index('            this.RequireHatchEdgeCode(94);',s.index('private HatchBoundaryPath.Spline ReadHatchSplineEdge'))
end=s.index('            // Only consumed data',start)
s=s[:start]+'''            int degree, knotCount, controlCount;
            bool rational, periodic;
            this.ReadHatchSplineHeader(out degree, out rational, out periodic, out knotCount, out controlCount);
'''+s[end:]
p.write_bytes(s.replace('\n','\r\n').encode() if crlf else s.encode())
p=Path('tests/netDxf.Conformance/RegressionTests.cs');s=p.read_text();old='        RegisterHatchEdgeDispatchTests();';assert s.count(old)==1
p.write_text(s.replace(old,old+'\n        RegisterHatchScalarOrderTests();'))
