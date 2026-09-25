// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Mesh
    {
        /// <summary>Replaces one WCS coordinate without changing face or edge indices.</summary>
        /// <param name="index">Zero-based index of the existing vertex.</param>
        /// <param name="position">Finite replacement position in world coordinates.</param>
        /// <remarks>
        /// All existing coordinates must be finite and the vertex count must not exceed
        /// the existing four-million-vertex editing budget. Exact binary64 no-ops preserve
        /// common proxy graphics; any changed component, including signed zero, clears them.
        /// Face/edge collections, crease values, subdivision headers and Normal are unchanged.
        /// This is coordinate editing, not topology validation or subdivision evaluation.
        /// </remarks>
        public void SetVertex(int index, Vector3 position)
        {
            if (index < 0 || index >= this.vertexes.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            VertexAffineTransform.Validate(this.vertexes);
            VertexAffineTransform.CheckedPoint(position, nameof(position));
            if (SameVertexEditBits(this.vertexes[index], position)) return;
            this.vertexes[index] = position;
            this.ClearProxyGraphics();
        }

        /// <summary>Replaces all WCS coordinates while preserving vertex count and topology identities.</summary>
        /// <param name="positions">Exactly one finite position per existing vertex, in index order.</param>
        /// <remarks>
        /// Input is enumerated once, within the existing vertex editing budget, and disposed
        /// before any coordinate is published. Aliasing the existing Vertexes list is allowed.
        /// Invalid count, nonfinite values, enumeration failures and source-coordinate drift
        /// reject before this operation writes coordinates or clears common graphics.
        /// Caller enumeration side effects are not rolled back; concurrent editing is unsupported.
        /// Direct mutation of the public lists still bypasses this API and remains caller-managed.
        /// </remarks>
        public void SetVertexes(IEnumerable<Vector3> positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            Vector3[] source = this.CaptureVertexEditSource();
            Vector3[] candidate = VertexAffineTransform.LimitedExactValues(positions, source.Length);
            this.PublishVertexEdits(source, candidate);
        }

        /// <summary>Applies a count-preserving batch of WCS coordinate replacements.</summary>
        /// <param name="positions">Unique zero-based vertex indices and finite replacement coordinates.</param>
        /// <remarks>
        /// Duplicate and out-of-range indices reject the complete batch, including duplicate
        /// no-ops. An empty batch validates the existing coordinate state and otherwise does
        /// nothing. Enumeration and disposal finish before publication; source drift rejects.
        /// Exact unchanged bits preserve common graphics. No handle or topology object is replaced.
        /// Caller callbacks and direct/concurrent list mutations are outside the transaction.
        /// </remarks>
        public void SetVertexPositions(IEnumerable<KeyValuePair<int, Vector3>> positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            Vector3[] source = this.CaptureVertexEditSource();
            Vector3[] candidate = (Vector3[])source.Clone();
            bool[] seen = new bool[source.Length];
            foreach (KeyValuePair<int, Vector3> edit in positions)
            {
                if (edit.Key < 0 || edit.Key >= source.Length)
                    throw new ArgumentOutOfRangeException(nameof(positions), "A vertex index is outside the existing mesh.");
                if (seen[edit.Key])
                    throw new ArgumentException("A vertex index occurs more than once.", nameof(positions));
                VertexAffineTransform.CheckedPoint(edit.Value, nameof(positions));
                seen[edit.Key] = true;
                candidate[edit.Key] = edit.Value;
            }
            // At most Count unique indices can be consumed; a further element must reject.
            this.PublishVertexEdits(source, candidate);
        }

        private Vector3[] CaptureVertexEditSource()
        {
            VertexAffineTransform.Validate(this.vertexes);
            return this.vertexes.ToArray();
        }

        private void PublishVertexEdits(Vector3[] source, Vector3[] candidate)
        {
            // External enumerators may access this Mesh. Never overwrite a coordinate edit
            // they made while preparing our candidate. Their own side effects remain theirs.
            if (this.vertexes.Count != source.Length)
                throw new InvalidOperationException("Mesh vertex count changed during replacement enumeration.");
            bool changed = false;
            for (int i = 0; i < source.Length; i++)
            {
                if (!SameVertexEditBits(source[i], this.vertexes[i]))
                    throw new InvalidOperationException("Mesh coordinates changed during replacement enumeration.");
                if (!SameVertexEditBits(source[i], candidate[i])) changed = true;
            }
            if (!changed) return;
            // Only private List storage is written; no virtual geometry accessor or input
            // enumerator runs after validation. Existing list/face/edge identities survive.
            for (int i = 0; i < source.Length; i++)
                if (!SameVertexEditBits(source[i], candidate[i])) this.vertexes[i] = candidate[i];
            this.ClearProxyGraphics();
        }

        private static bool SameVertexEditBits(Vector3 left, Vector3 right)
        {
            return BitConverter.DoubleToInt64Bits(left.X) == BitConverter.DoubleToInt64Bits(right.X)
                && BitConverter.DoubleToInt64Bits(left.Y) == BitConverter.DoubleToInt64Bits(right.Y)
                && BitConverter.DoubleToInt64Bits(left.Z) == BitConverter.DoubleToInt64Bits(right.Z);
        }
    }
}
