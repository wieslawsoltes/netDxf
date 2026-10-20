// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace netDxf.Objects
{
    public partial class LayerState
    {
        /// <summary>Maximum UTF-8 LAS transfer size accepted by the strict, in-memory codec.</summary>
        public const int MaximumLasBytes = 2 * 1024 * 1024;
        /// <summary>Maximum saved layer entries accepted by the strict transfer codec.</summary>
        public const int MaximumLasLayers = 10000;

        /// <summary>Reads one detached LAS state without changing a document or closing the input stream.</summary>
        /// <remarks>UTF-8, optionally with BOM. Bounded input; cancellation and malformed or unsupported records throw.
        /// Unlike legacy Load/manager Import, this never repairs resources or restores drawing properties.</remarks>
        public static LayerState ReadLas(Stream stream, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (var buffer = new MemoryStream())
            {
                var bytes = new byte[8192];
                int count;
                while ((count = stream.Read(bytes, 0, bytes.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (buffer.Length + count > MaximumLasBytes) throw new InvalidDataException("LAS transfer exceeds 2 MiB.");
                    buffer.Write(bytes, 0, count);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return FromLasText(new UTF8Encoding(false, true).GetString(buffer.ToArray()), cancellationToken);
            }
        }

        /// <summary>Parses one bounded LAS state, rejecting unsupported fields rather than silently discarding them.</summary>
        public static LayerState FromLasText(string text, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            cancellationToken.ThrowIfCancellationRequested();
            var utf8 = new UTF8Encoding(false, true);
            if (text.Length > MaximumLasBytes || utf8.GetByteCount(text) > MaximumLasBytes)
                throw new InvalidDataException("LAS transfer exceeds 2 MiB.");
            if (text.Length != 0 && text[0] == '\uFEFF') text = text.Substring(1);
            ValidateLasPacket(text, cancellationToken);
            using (var stream = new MemoryStream(utf8.GetBytes(text)))
            {
                var state = Read(stream);
                state.ValidateLasExport();
                cancellationToken.ThrowIfCancellationRequested();
                return state;
            }
        }

        /// <summary>Serializes one bounded LAS state. Does not allocate drawing handles or change resource ownership.</summary>
        public string ToLasText(CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.Properties.Count > MaximumLasLayers) throw new InvalidDataException("LAS transfer exceeds 10000 layers.");
            // Bound text before allocating serialized output, including hostile public property values.
            var utf8 = new UTF8Encoding(false, true);
            long characters = this.Name.Length + (long)this.Description.Length + this.CurrentLayer.Length + 256L;
            if (characters > MaximumLasBytes) throw new InvalidDataException("LAS transfer exceeds 2 MiB.");
            long bytes = utf8.GetByteCount(this.Name) + (long)utf8.GetByteCount(this.Description) + utf8.GetByteCount(this.CurrentLayer) + 256L;
            if (bytes > MaximumLasBytes) throw new InvalidDataException("LAS transfer exceeds 2 MiB.");
            foreach (var property in this.Properties.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (property == null || property.Name == null || property.LinetypeName == null)
                    throw new InvalidDataException("LAS export requires complete named layer properties.");
                if (property.Name.Length + (long)property.LinetypeName.Length + bytes > MaximumLasBytes)
                    throw new InvalidDataException("LAS transfer exceeds 2 MiB.");
                bytes += utf8.GetByteCount(property.Name) + (long)utf8.GetByteCount(property.LinetypeName) + 256L;
                if (bytes > MaximumLasBytes) throw new InvalidDataException("LAS transfer exceeds 2 MiB.");
            }
            this.ValidateLasExport();
            using (var stream = new MemoryStream())
            {
                Write(stream, this);
                if (stream.Length > MaximumLasBytes) throw new InvalidDataException("LAS transfer exceeds 2 MiB.");
                var text = new UTF8Encoding(false, true).GetString(stream.ToArray());
                // Apply the same lossless, supported-packet contract in both directions.
                ValidateLasPacket(text.Length != 0 && text[0] == '\uFEFF' ? text.Substring(1) : text, cancellationToken);
                return text;
            }
        }

        /// <summary>Writes a complete validated UTF-8 packet without closing the destination stream.</summary>
        /// <remarks>Validation and cancellation happen before the first write. An I/O failure may partially write the stream.
        /// Use SaveAtomic for atomic filesystem replacement.</remarks>
        public void WriteLas(Stream stream, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var bytes = new UTF8Encoding(false, true).GetBytes(this.ToLasText(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void ValidateLasPacket(string text, CancellationToken token)
        {
            using (var reader = new StringReader(text))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var fields = new HashSet<int>();
                bool layer = false, named = false, ended = false;
                int pair = 0;
                string codeLine;
                while ((codeLine = reader.ReadLine()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    string value = reader.ReadLine();
                    int code;
                    if (ended || value == null || !int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                        throw new InvalidDataException("LAS requires complete code/value pairs for one layer state.");
                    ValidateLasLine(value);
                    if (pair++ < 2)
                    {
                        if (code != 0 || value != (pair == 1 ? LayerStateDictionary : LayerStateName))
                            throw new InvalidDataException("Invalid LAS header.");
                        continue;
                    }
                    if (code == 0)
                    {
                        if (value != "EOF") throw new InvalidDataException("LAS transfer accepts exactly one layer state.");
                        ended = true; continue;
                    }
                    if (code == 8)
                    {
                        if (string.IsNullOrWhiteSpace(value) || !names.Add(value))
                            throw new InvalidDataException("LAS layer names must be nonempty and unique ignoring case.");
                        if (names.Count > MaximumLasLayers) throw new InvalidDataException("LAS transfer exceeds 10000 layers.");
                        layer = true; fields.Clear(); continue;
                    }
                    if (!fields.Add(code)) throw new InvalidDataException("LAS contains duplicate fields.");
                    if (!layer)
                    {
                        if (code != 1 && code != 91 && code != 301 && code != 290 && code != 302)
                            throw new InvalidDataException("Unsupported LAS state field: " + code);
                        if (code == 1) named = !string.IsNullOrWhiteSpace(value);
                        if (code == 91) ParseLasInteger(value);
                        if (code == 290 && value != "0" && value != "1") throw new InvalidDataException("Invalid LAS paper-space flag.");
                    }
                    else
                    {
                        if (code != 90 && code != 62 && code != 370 && code != 6 && code != 440 && code != 92)
                            throw new InvalidDataException("Unsupported LAS layer field: " + code + ". Plot styles and viewport overrides cannot be transferred.");
                        if (code != 6)
                        {
                            int number = ParseLasInteger(value);
                            if (code == 370 && !Enum.IsDefined(typeof(Lineweight), number))
                                throw new InvalidDataException("Invalid LAS lineweight.");
                            if (code == 62 && (number < -255 || number > 256)) throw new InvalidDataException("Invalid LAS indexed color.");
                            if (code == 62 && fields.Contains(92)) throw new InvalidDataException("LAS indexed color must precede true color.");
                            // LAS marks explicit true colors with high byte C2; plain DXF RGB uses 00.
                            if (code == 92 && ((uint)number >> 24) != 0 && ((uint)number >> 24) != 0xC2)
                                throw new InvalidDataException("Unsupported LAS true-color encoding.");
                        }
                        else if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("LAS requires a linetype name.");
                    }
                }
                if (pair < 3 || !named) throw new InvalidDataException("LAS requires one named state.");
            }
        }

        private static int ParseLasInteger(string value)
        {
            int number;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                throw new InvalidDataException("Invalid LAS integer field.");
            return number;
        }
    }
}
