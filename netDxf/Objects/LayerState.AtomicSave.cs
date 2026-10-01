// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.IO;
using System.Threading;
using netDxf.IO;

namespace netDxf.Objects
{
    public partial class LayerState
    {
        /// <summary>Stages a complete LAS file before replacing the destination.</summary>
        /// <param name="file">Destination in an existing directory.</param>
        /// <param name="cancellationToken">Checked before staging, after serialization and before commit.</param>
        /// <remarks>
        /// Exceptions propagate in Debug and Release. Invalid single-line text, missing property values,
        /// serialization failure and cancellation before commit preserve an existing destination, or
        /// leave an absent destination absent. The existing Save method keeps its Boolean/error policy.
        /// Output uses the existing LAS serializer and allocates no drawing handles. No full-file memory
        /// copy or delete/copy fallback is used. Destination symlinks, directories and read-only files
        /// reject. Filesystem replacement support is required. File metadata, old hard links, power-loss
        /// durability, concurrent writers and hostile parent-directory changes are not guaranteed.
        /// Staging-file cleanup is best effort. Cancellation does not interrupt serialization itself.
        /// </remarks>
        public void SaveAtomic(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            DxfAtomicFile.Write(file, stream =>
            {
                this.ValidateLasExport();
                Write(stream, this);
            }, cancellationToken);
        }

        private void ValidateLasExport()
        {
            ValidateLasLine(this.Name);
            ValidateLasLine(this.Description);
            ValidateLasLine(this.CurrentLayer);
            foreach (LayerStateProperties property in this.Properties.Values)
            {
                if (property == null || property.Color == null || property.Transparency == null)
                    throw new InvalidDataException("LAS export requires complete layer properties, color and transparency.");
                ValidateLasLine(property.Name);
                ValidateLasLine(property.LinetypeName);
            }
        }

        private static void ValidateLasLine(string value)
        {
            if (value == null) throw new InvalidDataException("A LAS text value cannot be null.");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r' || c == '\n' || c == '\0')
                    throw new InvalidDataException("LAS text values cannot contain line breaks or NUL.");
                if (!char.IsSurrogate(c)) continue;
                if (!char.IsHighSurrogate(c) || ++i == value.Length || !char.IsLowSurrogate(value[i]))
                    throw new InvalidDataException("LAS text values require paired Unicode surrogates.");
            }
        }
    }
}
