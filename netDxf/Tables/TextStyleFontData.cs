using System;

namespace netDxf.Tables
{
    /// <summary>Immutable stored STYLE extended font family and complete group 1071 value.</summary>
    /// <remarks>Only the bold and italic bits are interpreted. All other bits are retained without font substitution or rendering claims.</remarks>
    public sealed class TextStyleFontData
    {
        /// <summary>Creates extended font data, including an explicitly empty family name if required.</summary>
        /// <param name="familyName">Font family name, at most 255 characters.</param>
        /// <param name="flags">Complete signed 32-bit group 1071 value.</param>
        public TextStyleFontData(string familyName, int flags)
        {
            if (familyName == null) throw new ArgumentNullException(nameof(familyName));
            if (familyName.Length > 255) throw new ArgumentOutOfRangeException(nameof(familyName), "The font family XData string cannot exceed 255 characters.");
            this.FamilyName = familyName;
            this.Flags = flags;
        }

        /// <summary>Gets the stored family name.</summary>
        public string FamilyName { get; }
        /// <summary>Gets all stored group 1071 bits, including pitch, family, charset and unknown bits.</summary>
        public int Flags { get; }
        /// <summary>Gets the bold and italic bits.</summary>
        public FontStyle FontStyle { get { return (FontStyle)((this.Flags >> 24) & 3); } }
    }
}
