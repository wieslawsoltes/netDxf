using System;
using netDxf.Header;
using netDxf.Tables;

namespace netDxf.IO
{
    internal partial class DxfWriter
    {
        internal static bool IsStoredDimensionHeader(string name)
        {
            return string.Equals(name, "$DIMTSZ", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "$DIMTVP", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "$DIMUPT", StringComparison.OrdinalIgnoreCase);
        }

        internal static void ValidateStoredDimensionHeader(HeaderVariable variable)
        {
            if (string.Equals(variable.Name, "$DIMUPT", StringComparison.OrdinalIgnoreCase))
            {
                if (variable.GroupCode != 70 || !(variable.Value is short flag) || (flag != 0 && flag != 1))
                    throw new FormatException("$DIMUPT requires group 70 with a short value of zero or one.");
            }
            else
            {
                if (variable.GroupCode != 40 || !(variable.Value is double number))
                    throw new FormatException(variable.Name + " requires group 40 with a double value.");
                DimensionStyle.ValidateStoredDimensionReal(number, string.Equals(variable.Name, "$DIMTSZ", StringComparison.OrdinalIgnoreCase), nameof(variable));
            }
        }

        private void ValidateStoredDimensionHeaders()
        {
            foreach (HeaderVariable variable in this.doc.DrawingVariables.CustomValues())
                if (IsStoredDimensionHeader(variable.Name)) ValidateStoredDimensionHeader(variable);
        }

        private void WriteStoredDimensionHeaders(DimensionStyle style)
        {
            this.WriteStoredDimensionHeader("$DIMTSZ", 40, style.TickSize);
            this.WriteStoredDimensionHeader("$DIMTVP", 40, style.TextVerticalPosition);
            this.WriteStoredDimensionHeader("$DIMUPT", 70, style.UserPositionedText ? (short) 1 : (short) 0);
        }

        private void WriteStoredDimensionHeader(string name, short code, object fallback)
        {
            object value = this.doc.DrawingVariables.TryGetCustomVariable(name, out HeaderVariable variable) ? variable.Value : fallback;
            this.chunk.Write(9, name);
            this.chunk.Write(code, value);
        }
    }
}
