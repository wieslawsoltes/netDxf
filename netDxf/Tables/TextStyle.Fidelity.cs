using System;

namespace netDxf.Tables
{
    public partial class TextStyle
    {
        private TextStyleFlags flags;
        private double? lastHeight;

        /// <summary>Creates a text style from independently stored DXF font file metadata.</summary>
        /// <param name="name">The text style name.</param>
        /// <param name="fontFile">Stored group 3, including an empty or extensionless value.</param>
        /// <param name="bigFont">Stored group 4, independent of the primary font file.</param>
        /// <returns>A detached text style retaining both file strings exactly.</returns>
        /// <remarks>
        /// This factory does not resolve paths, load fonts, or substitute filenames. It accepts
        /// well-formed Unicode supported by STYLE transport, including escaped control characters.
        /// ExtendedFontData may be assigned independently. Authoring constructors and setters retain
        /// their font-format validation.
        /// </remarks>
        public static TextStyle FromStoredFontFiles(string name, string fontFile, string bigFont = "")
        {
            ValidateStoredFontString(name, nameof(name));
            ValidateStoredFontString(fontFile, nameof(fontFile));
            ValidateStoredFontString(bigFont, nameof(bigFont));
            var style = new TextStyle(name, DefaultFont);
            style.SetStoredFontFiles(fontFile, bigFont);
            return style;
        }

        private static void ValidateStoredFontString(string value, string parameter)
        {
            if (value == null) throw new ArgumentNullException(parameter);
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsSurrogate(value[i])) continue;
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                { i++; continue; }
                throw new ArgumentException("Stored STYLE strings cannot contain unpaired UTF-16 surrogates.", parameter);
            }
        }

        /// <summary>Gets or sets all stored group 70 bits.</summary>
        /// <remarks>The shape discriminator cannot be set on a TextStyle. Other bits are retained.</remarks>
        public TextStyleFlags Flags
        {
            get { return this.flags; }
            set
            {
                if ((value & TextStyleFlags.Shape) != 0) throw new ArgumentException("Use ShapeStyle for a shape STYLE record.", nameof(value));
                this.flags = value;
            }
        }

        /// <summary>Gets or sets all stored group 71 bits. IsBackward and IsUpsideDown project bits 2 and 4.</summary>
        public short TextGenerationFlags { get; set; }

        /// <summary>Gets or sets optional group 42, the last text height used. Null omits the group.</summary>
        /// <remarks>This stored editing value is independent of the fixed Height; zero is explicitly preserved.</remarks>
        public double? LastHeight
        {
            get { return this.lastHeight; }
            set
            {
                if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                    throw new ArgumentOutOfRangeException(nameof(value), "The last height must be finite.");
                this.lastHeight = value;
            }
        }

        /// <summary>Gets or replaces the canonical ACAD XData font prefix, or null when that prefix is absent.</summary>
        /// <remarks>
        /// The prefix consists of the first two ACAD records: string 1000 followed by integer 1071.
        /// Other records are retained in order. Setting null removes only a recognized prefix.
        /// Removal rejects a suffix beginning with another string/integer pair, because it would become a new font prefix on reload.
        /// This property is a view of XData, so direct edits to that prefix are immediately visible.
        /// It can coexist with FontFile; setting it does not change either font file.
        /// </remarks>
        public TextStyleFontData ExtendedFontData
        {
            get
            {
                if (!this.XData.TryGetValue(ApplicationRegistry.DefaultName, out XData data) || !HasFontPrefix(data)) return null;
                return new TextStyleFontData((string)data.XDataRecord[0].Value, (int)data.XDataRecord[1].Value);
            }
            set
            {
                if (!this.XData.TryGetValue(ApplicationRegistry.DefaultName, out XData data))
                {
                    if (value == null) return;
                    data = new XData(new ApplicationRegistry(ApplicationRegistry.DefaultName));
                    data.XDataRecord.Add(new XDataRecord(XDataCode.String, value.FamilyName));
                    data.XDataRecord.Add(new XDataRecord(XDataCode.Int32, value.Flags));
                    this.XData.Add(data);
                    return;
                }
                if (HasFontPrefix(data))
                {
                    if (value == null && data.XDataRecord.Count >= 4 &&
                        data.XDataRecord[2].Code == XDataCode.String && data.XDataRecord[3].Code == XDataCode.Int32)
                        throw new InvalidOperationException("Removing this font prefix would expose another font prefix. Edit the ACAD XData explicitly to disambiguate its structure.");
                    data.XDataRecord.RemoveAt(0);
                    data.XDataRecord.RemoveAt(0);
                }
                if (value != null)
                {
                    data.XDataRecord.Insert(0, new XDataRecord(XDataCode.Int32, value.Flags));
                    data.XDataRecord.Insert(0, new XDataRecord(XDataCode.String, value.FamilyName));
                }
            }
        }

        private static bool HasFontPrefix(XData data)
        {
            return data.XDataRecord.Count >= 2 && data.XDataRecord[0].Code == XDataCode.String && data.XDataRecord[1].Code == XDataCode.Int32;
        }

        // File names read from DXF are stored independently of the optional font prefix.
        internal void SetStoredFontFiles(string font, string bigFontFile)
        {
            this.file = font;
            this.bigFont = bigFontFile;
        }
    }
}
