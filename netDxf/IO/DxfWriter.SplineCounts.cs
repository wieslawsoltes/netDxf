// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.IO;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private static bool GetSplineCounts(Spline spline, out long knots, out long controls, out long fits)
        {
            knots = spline.Knots.Length;
            controls = (long)spline.ControlPoints.Length + (spline.IsClosedPeriodic ? spline.Degree : 0);
            fits = spline.FitPoints.Count;
            return knots <= short.MaxValue && controls >= 0 && controls <= short.MaxValue && fits <= short.MaxValue;
        }

        private void ValidateSplineCounts()
        {
            if (this.doc.SplineCountPolicy != DxfSplineCountPolicy.RequireRepresentable) return;
            // Registered nested and unreferenced blocks are serialized too. Do not
            // initialize OBJECTS or allocate/register handles during this preflight.
            foreach (var block in this.doc.Blocks)
                foreach (var entity in block.Entities)
                    if (entity is Spline spline && !GetSplineCounts(spline, out _, out _, out _))
                        throw new InvalidDataException("SPLINE counts exceed the nonnegative 16-bit DXF range required by SplineCountPolicy.");
        }

        // Entity dispatch chooses this exact single-argument overload rather than
        // the legacy writer's optional-bool overload. Both entry points below use
        // that one existing serializer, including its periodic prefix and XData.
        private void WriteSpline(Spline spline)
        { this.WriteSplineWithCountPolicy(spline, true); }

        // HELIX already uses the two-argument form with a statically typed Helix.
        // This overload adds the same policy without changing its existing caller.
        private void WriteSpline(Helix helix, bool writeXData)
        { this.WriteSplineWithCountPolicy(helix, writeXData); }

        private void WriteSplineWithCountPolicy(Spline spline, bool writeXData)
        {
            if (this.doc.SplineCountPolicy == DxfSplineCountPolicy.LegacyOmit)
            { this.WriteSpline(spline, writeXData); return; }
            long knots, controls, fits;
            if (!GetSplineCounts(spline, out knots, out controls, out fits))
            {
                if (this.doc.SplineCountPolicy == DxfSplineCountPolicy.RequireRepresentable)
                    throw new InvalidDataException("SPLINE counts exceed the nonnegative 16-bit DXF range required by SplineCountPolicy.");
                this.WriteSpline(spline, writeXData);
                return; // Never wrap, truncate, saturate, or emit partial counts.
            }
            // Add metadata at the existing degree write, without a second copy of
            // the geometry serializer. Only opt-in, representable output is wrapped.
            ICodeValueWriter output = this.chunk;
            try
            {
                this.chunk = new SplineCountWriter(output, (short)knots, (short)controls, (short)fits);
                this.WriteSpline(spline, writeXData);
            }
            finally { this.chunk = output; }
        }

        private sealed class SplineCountWriter : ICodeValueWriter
        {
            private readonly ICodeValueWriter output;
            private readonly short knots, controls, fits;
            private bool pending = true;
            internal SplineCountWriter(ICodeValueWriter output, short knots, short controls, short fits)
            { this.output = output; this.knots = knots; this.controls = controls; this.fits = fits; }
            public short Code { get { return this.output.Code; } }
            public object Value { get { return this.output.Value; } }
            public long CurrentPosition { get { return this.output.CurrentPosition; } }
            public void Write(short code, object value)
            {
                this.output.Write(code, value);
                if (this.pending && code == 71)
                {
                    this.pending = false;
                    this.output.Write(72, this.knots);
                    this.output.Write(73, this.controls);
                    this.output.Write(74, this.fits);
                }
            }
            public void WriteByte(byte value) { this.output.WriteByte(value); }
            public void WriteBytes(byte[] value) { this.output.WriteBytes(value); }
            public void WriteShort(short value) { this.output.WriteShort(value); }
            public void WriteInt(int value) { this.output.WriteInt(value); }
            public void WriteLong(long value) { this.output.WriteLong(value); }
            public void WriteBool(bool value) { this.output.WriteBool(value); }
            public void WriteDouble(double value) { this.output.WriteDouble(value); }
            public void WriteString(string value) { this.output.WriteString(value); }
            public void Flush() { this.output.Flush(); }
        }
    }
}
