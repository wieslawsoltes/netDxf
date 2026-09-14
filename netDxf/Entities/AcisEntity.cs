// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Inert ACIS SAT data for BODY, REGION and 3DSOLID in DXF 2000 through 2010.</summary>
    /// <remarks>
    /// This class preserves the DXF envelope and decodes its text substitution only. It does not
    /// parse, validate, evaluate or transform the proprietary model. DXF 2013 and later require
    /// SAB and ACDSDATA lifecycle support and cannot be written with this model.
    /// </remarks>
    public abstract class AcisEntity : EntityObject
    {
        /// <summary>Maximum number of encoded chunks, including empty chunks.</summary>
        public const int MaximumSatChunks = 65536;
        /// <summary>Maximum total encoded characters.</summary>
        public const int MaximumSatCharacters = 16 * 1024 * 1024;
        /// <summary>Maximum encoded characters in one logical line.</summary>
        public const int MaximumSatLineCharacters = 1024 * 1024;
        private ReadOnlyCollection<AcisSatChunk> chunks = new List<AcisSatChunk>().AsReadOnly();
        private ReadOnlyCollection<string> lines = new List<string>().AsReadOnly();

        internal AcisEntity(EntityType type, string code) : base(type, code) { }

        /// <summary>Gets DXF group 70, the supported modeler envelope version, currently 1.</summary>
        /// <remarks>This is independent of the SAT content version in the uninterpreted first SAT line.</remarks>
        public short ModelerFormatVersion { get { return 1; } }
        /// <summary>Gets an immutable snapshot of the exact encoded group 1/3 chunks.</summary>
        public IReadOnlyList<AcisSatChunk> EncodedSatChunks { get { return this.chunks; } }
        /// <summary>Gets an immutable snapshot of the decoded logical lines, without newline delimiters.</summary>
        public IReadOnlyList<string> SatLines { get { return this.lines; } }

        /// <summary>Replaces the payload transactionally from decoded printable ASCII lines.</summary>
        /// <remarks>
        /// Encodes and splits each line into canonical chunks of up to 255 characters. Whitespace
        /// is retained. An empty sequence clears the payload; saving an empty entity is rejected.
        /// SAT syntax and modeler compatibility are the caller's responsibility.
        /// </remarks>
        public void SetSatLines(IEnumerable<string> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var result = new List<AcisSatChunk>();
            int total = 0;
            foreach (string line in value)
            {
                if (line == null) throw new ArgumentException("SAT lines cannot be null.", nameof(value));
                if (line.Length > MaximumSatLineCharacters) throw new ArgumentOutOfRangeException(nameof(value));
                ValidateAscii(line, nameof(value));
                string encoded = Encode(line);
                if (encoded.Length > MaximumSatLineCharacters || encoded.Length > MaximumSatCharacters - total)
                    throw new ArgumentOutOfRangeException(nameof(value), "SAT character limit exceeded.");
                total += encoded.Length;
                for (int offset = 0; offset < encoded.Length || offset == 0; offset += 255)
                {
                    if (result.Count == MaximumSatChunks) throw new ArgumentOutOfRangeException(nameof(value), "SAT chunk limit exceeded.");
                    result.Add(new AcisSatChunk((short)(offset == 0 ? 1 : 3), encoded.Substring(offset, Math.Min(255, encoded.Length - offset))));
                }
            }
            this.SetEncodedSatChunks(result);
        }

        /// <summary>Replaces the payload transactionally, preserving exact encoded chunk boundaries.</summary>
        /// <remarks>Group 3 requires a preceding group 1. Encoded A escapes must include their following space.</remarks>
        public void SetEncodedSatChunks(IEnumerable<AcisSatChunk> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var result = new List<AcisSatChunk>();
            var decoded = new List<string>();
            StringBuilder line = null;
            int total = 0;
            foreach (AcisSatChunk part in value)
            {
                if (part == null) throw new ArgumentException("SAT chunks cannot be null.", nameof(value));
                if (result.Count == MaximumSatChunks || part.Text.Length > MaximumSatCharacters - total)
                    throw new ArgumentOutOfRangeException(nameof(value), "SAT payload limit exceeded.");
                total += part.Text.Length;
                if (part.GroupCode == 1)
                {
                    if (line != null) decoded.Add(Decode(line.ToString()));
                    line = new StringBuilder();
                }
                if (line == null) throw new ArgumentException("A SAT continuation requires a preceding group 1.", nameof(value));
                if (part.Text.Length > MaximumSatLineCharacters - line.Length)
                    throw new ArgumentOutOfRangeException(nameof(value), "SAT logical line limit exceeded.");
                line.Append(part.Text);
                result.Add(part);
            }
            if (line != null) decoded.Add(Decode(line.ToString()));
            this.chunks = result.AsReadOnly();
            this.lines = decoded.AsReadOnly();
        }

        internal static void ValidateAscii(string text, string parameter)
        {
            foreach (char c in text)
                if (c < 32 || c > 126) throw new ArgumentException("SAT text must contain printable ASCII without newline delimiters.", parameter);
        }

        // DXF SAT substitution as independently documented by ezdxf's MIT-licensed tools/crypt.py
        // (Copyright (c) 2014-2018 Manfred Moitzi). This is an envelope codec, not an ACIS parser.
        // Permission is hereby granted, free of charge, to any person obtaining a copy of this
        // software and associated documentation files (the "Software"), to deal in the Software
        // without restriction, including without limitation the rights to use, copy, modify,
        // merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
        // permit persons to whom the Software is furnished to do so, subject to the following
        // conditions: The above copyright notice and this permission notice shall be included
        // in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS",
        // WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
        // WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
        // IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
        // OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        private static string Encode(string text)
        {
            var result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == ' ') result.Append(c);
                else if (c == '_') result.Append('@');
                else if (c == '@') result.Append('_');
                else if (c >= 'A' && c <= '^')
                {
                    result.Append((char)('^' - (c - 'A')));
                    if (c == 'A') result.Append(' ');
                }
                else result.Append((char)(c ^ 0x5f));
            }
            return result.ToString();
        }

        private static string Decode(string text)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ') result.Append(c);
                else if (c == '@') result.Append('_');
                else if (c == '_') result.Append('@');
                else if (c >= 'A' && c <= '^')
                {
                    result.Append((char)('A' + ('^' - c)));
                    if (c == '^' && (++i >= text.Length || text[i] != ' '))
                        throw new ArgumentException("Malformed SAT encoded A escape.");
                }
                else result.Append((char)(c ^ 0x5f));
            }
            return result.ToString();
        }

        /// <summary>Accepts exact identity only; other transforms require an ACIS modeler.</summary>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            if (transformation.M11 == 1 && transformation.M22 == 1 && transformation.M33 == 1 &&
                transformation.M12 == 0 && transformation.M13 == 0 && transformation.M21 == 0 &&
                transformation.M23 == 0 && transformation.M31 == 0 && transformation.M32 == 0 &&
                translation.X == 0 && translation.Y == 0 && translation.Z == 0) return;
            throw new NotSupportedException("ACIS entities cannot be transformed without rewriting their modeler payload.");
        }

        /// <summary>Accepts exact 4x4 identity only, including the complete homogeneous bottom row.</summary>
        public override void TransformBy(Matrix4 transformation)
        {
            if (transformation.M41 != 0 || transformation.M42 != 0 || transformation.M43 != 0 || transformation.M44 != 1)
                throw new NotSupportedException("ACIS entities cannot be transformed without rewriting their modeler payload.");
            base.TransformBy(transformation);
        }

        internal void CopyTo(AcisEntity copy)
        {
            copy.SetEncodedSatChunks(this.chunks);
            copy.Layer = (Layer)this.Layer.Clone(); copy.Linetype = (Linetype)this.Linetype.Clone();
            copy.Color = (AciColor)this.Color.Clone(); copy.Lineweight = this.Lineweight;
            copy.Transparency = (Transparency)this.Transparency.Clone(); copy.LinetypeScale = this.LinetypeScale;
            copy.IsVisible = this.IsVisible; copy.Normal = this.Normal;
            foreach (XData data in this.XData.Values) copy.XData.Add((XData)data.Clone());
            this.CopyCommonDataTo(copy);
        }
    }
}
