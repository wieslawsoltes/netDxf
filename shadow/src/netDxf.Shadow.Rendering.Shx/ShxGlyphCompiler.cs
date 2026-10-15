using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.Shx;

public sealed record ShxExecutionOptions(int MaximumInstructions = 1_000_000, int MaximumCurves = 200_000,
    int MaximumCallDepth = 64, int MaximumStackDepth = 4);
public sealed record ShxGlyph(ImmutableArray<CadContour> Contours, Vec3 Advance, Bounds2 Bounds)
{
    public int ExecutedInstructions { get; init; }
    public double ExitScale { get; init; } = 1;
}

/// <summary>Bounded SHX VM emitting analytic immutable geometry, including extended BIGFONT primitives.</summary>
public static class ShxGlyphCompiler
{
    public static ShxGlyph Compile(ShxFont font, int number, bool vertical = false,
        ShxExecutionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(font); options ??= new();
        if (options.MaximumInstructions < 1 || options.MaximumCurves < 1 || options.MaximumCallDepth is < 1 or > 256 || options.MaximumStackDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var state = new Machine(font, vertical, options, cancellationToken);
        state.Call(number); return state.Finish();
    }

    private sealed class Machine(ShxFont font, bool vertical, ShxExecutionOptions limits, CancellationToken token)
    {
        private static readonly double[] Dx = [1, 1, 1, .5, 0, -.5, -1, -1, -1, -1, -1, -.5, 0, .5, 1, 1];
        private static readonly double[] Dy = [0, .5, 1, 1, 1, 1, 1, .5, 0, -.5, -1, -1, -1, -1, -1, -.5];
        private Stack<Vec3> positions = new();
        private readonly HashSet<int> active = new();
        private readonly ImmutableArray<CadContour>.Builder contours = ImmutableArray.CreateBuilder<CadContour>();
        private readonly ImmutableArray<Curve3>.Builder run = ImmutableArray.CreateBuilder<Curve3>();
        private Vec3 position; private double scale = 1; private bool down = true;
        private Affine3 output = Affine3.Identity;
        private int work, curves; private Bounds2 bounds = Bounds2.Empty;

        internal void Call(int number)
        {
            Tick();
            if (!font.Symbols.TryGetValue(number, out var symbol)) throw new KeyNotFoundException($"SHX symbol {number} is missing.");
            if (active.Count >= limits.MaximumCallDepth || !active.Add(number)) throw new InvalidOperationException("SHX subshape recursion/call budget exceeded.");
            try
            {
                var skip = false;
                foreach (var instruction in symbol.Instructions)
                {
                    Tick(); if (skip) { skip = false; continue; }
                    var code = instruction.Code; var a = instruction.Operands;
                    switch (code)
                    {
                        case 1: down = true; break;
                        case 2: down = false; Flush(); break;
                        case 3: scale /= a[0]; Check(); break;
                        case 4: scale *= a[0]; Check(); break;
                        case 5:
                            if (positions.Count >= limits.MaximumStackDepth) throw new InvalidOperationException("SHX position stack budget exceeded.");
                            positions.Push(position); break;
                        case 6:
                            if (!positions.TryPop(out var saved)) throw new FormatException("SHX position stack underflow.");
                            Flush(); position = saved; break;
                        case 7: if (a.Length == 6 && a[0] == 0) Primitive(a); else Call(a[0]); break;
                        case 8: Move(a[0], a[1]); break;
                        case 9: for (var i = 0; i < a.Length; i += 2) { Tick(); Move(a[i], a[i + 1]); } break;
                        case 10: Arc(a[0] * scale, 0, 256, a[1]); break;
                        case 11: Arc(((a[2] << 8) + a[3]) * scale, a[0], a[1] == 0 ? 256 : a[1], a[4]); break;
                        case 12: Bulge(a[0], a[1], a[2]); break;
                        case 13: for (var i = 0; i < a.Length; i += 3) { Tick(); Bulge(a[i], a[i + 1], a[i + 2]); } break;
                        case 14: skip = !vertical; break;
                        default:
                            if (code <= 15) throw new NotSupportedException($"Unsupported SHX opcode {code}.");
                            Move(Dx[code & 15] * (code >> 4), Dy[code & 15] * (code >> 4)); break;
                    }
                }
            }
            finally { active.Remove(number); }
        }
        private void Primitive(ImmutableArray<int> args)
        {
            var info = font.BigFont;
            if (info is null || info.CharacterWidth <= 0 || font.CapHeight <= 0)
                throw new FormatException("Extended subshape placement requires BIGFONT nominal dimensions.");
            Flush();
            var savedPosition = position; var savedScale = scale; var savedOutput = output;
            var savedPen = down; var savedStack = positions;
            output = output * Affine3.Move(position + new Vec3(args[2] * scale, args[3] * scale)) *
                Affine3.Scale(new(args[4] * scale / info.CharacterWidth, args[5] * scale / font.CapHeight, 1));
            position = Vec3.Zero; scale = 1; positions = new();
            try
            {
                Call(args[1]);
                if (positions.Count != 0) throw new FormatException("Extended BIGFONT primitive leaves an unbalanced position stack.");
                Flush();
            }
            finally
            {
                position = savedPosition; scale = savedScale; output = savedOutput;
                down = savedPen; positions = savedStack;
            }
        }
        private void Tick()
        {
            if (++work > limits.MaximumInstructions) throw new InvalidOperationException("SHX execution budget exceeded.");
            if ((work & 127) == 0) token.ThrowIfCancellationRequested();
        }
        private void Check()
        {
            if (!position.IsFinite || !(scale > 0) || !double.IsFinite(scale)) throw new ArithmeticException("SHX coordinates/scale are outside the numeric range.");
        }
        private void Move(double x, double y)
        {
            var target = position + new Vec3(x * scale, y * scale);
            Emit(new LineCurve(position, target)); position = target; Check();
        }
        private void Bulge(int x, int y, int amount)
        {
            if (x == -128 || y == -128 || amount == -128) throw new FormatException("SHX bulge instructions exclude -128 operands.");
            var target = position + new Vec3(x * scale, y * scale);
            Emit(StandardEntityProjector.Bulge(position, target, amount / 127.0)); position = target; Check();
        }
        private void Arc(double radius, int startOffset, int endOffset, int spec)
        {
            if (!(radius > 0) || !double.IsFinite(radius)) throw new FormatException("SHX arc radius must be finite and positive.");
            var octant = (spec >> 4) & 7; var count = spec & 15; var sign = (spec & 128) == 0 ? 1 : -1;
            if (count > 7) throw new FormatException("SHX octant count must be 0..7; zero represents eight octants.");
            if (count == 0) count = 8;
            var angle = (octant + sign * startOffset / 256.0) * Math.PI / 4;
            var sweep = sign * (count - 1 + (endOffset - startOffset) / 256.0) * Math.PI / 4;
            if (Math.Abs(sweep) > Math.Tau || Math.Sign(sweep) != sign) throw new FormatException("SHX fractional arc has an invalid sweep.");
            var center = position - new Vec3(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
            var curve = new EllipseCurve(center, Vec3.UnitX * radius, Vec3.UnitY * radius, angle, sweep);
            Emit(curve); position = curve.End; Check();
        }
        private void Emit(Curve3 curve)
        {
            if (!down) { Flush(); return; }
            if (output != Affine3.Identity) curve = curve.Transform(output);
            if (!curve.Start.IsFinite || !curve.End.IsFinite) throw new ArithmeticException("SHX curve is outside the coordinate range.");
            if (++curves > limits.MaximumCurves) throw new InvalidOperationException("SHX curve budget exceeded.");
            run.Add(curve); bounds = bounds.Include(curve.Start).Include(curve.End);
            if (curve is EllipseCurve arc)
            {
                // Extended primitive placement can produce a nonuniform ellipse. Use parameter extrema,
                // not circular cardinal angles, so retained ink bounds contain the actual geometry.
                var x = Math.Atan2(arc.V.X, arc.U.X); var y = Math.Atan2(arc.V.Y, arc.U.Y);
                Include(x); Include(x + Math.PI); Include(y); Include(y + Math.PI);
                void Include(double angle)
                {
                    var distance = arc.Sweep >= 0 ? Positive(angle - arc.StartAngle) : Positive(arc.StartAngle - angle);
                    if (distance <= Math.Abs(arc.Sweep) + 1e-12)
                        bounds = bounds.Include(arc.Center + arc.U * Math.Cos(angle) + arc.V * Math.Sin(angle));
                }
            }
        }
        private static double Positive(double angle) => (angle % Math.Tau + Math.Tau) % Math.Tau;
        private void Flush()
        {
            if (run.Count == 0) return;
            contours.Add(new(run.ToImmutable(), run[0].Start == run[^1].End)); run.Clear();
        }
        internal ShxGlyph Finish()
        {
            Flush(); token.ThrowIfCancellationRequested();
            if (positions.Count != 0) throw new FormatException("SHX shape leaves saved positions on the stack.");
            return new(contours.ToImmutable(), position, bounds) { ExecutedInstructions = work, ExitScale = scale };
        }
    }
}
