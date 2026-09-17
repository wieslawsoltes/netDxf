// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Units;

namespace netDxf.Objects
{
    /// <summary>An immutable explicitly supplied variable for bounded standard FIELD evaluation.</summary>
    public sealed class DxfFieldVariable
    {
        /// <summary>Creates a null, string, Int32 or finite double binding with explicit units.</summary>
        /// <param name="value">No conversion callbacks run.</param>
        /// <param name="storedUnitType">0 unitless, 1 distance, 2 radians, 4 area, 8 volume, 16 currency or 32 percentage.</param>
        public DxfFieldVariable(object value, int storedUnitType = 0)
        {
            if (storedUnitType != 0 && storedUnitType != 1 && storedUnitType != 2 && storedUnitType != 4 && storedUnitType != 8 && storedUnitType != 16 && storedUnitType != 32)
                throw new ArgumentOutOfRangeException(nameof(storedUnitType));
            if (value != null && !(value is int) && !(value is double) && !(value is string))
                throw new ArgumentException("Unsupported explicit FIELD variable type.", nameof(value));
            if (value is double real) UnitFormatMath.CheckFinite(real, nameof(value));
            if (value is string text) DxfDateTimeFormat.CheckText(text, nameof(value));
            if ((value == null || value is string) && storedUnitType != 0)
                throw new ArgumentException("Only numeric variables can have numeric units.", nameof(storedUnitType));
            this.Value = value; this.StoredUnitType = storedUnitType;
        }
        /// <summary>Gets the scalar; date variables contain a midnight-based DXF serial.</summary>
        public object Value { get; private set; }
        /// <summary>Gets the explicit unit type.</summary>
        public int StoredUnitType { get; private set; }
        /// <summary>Gets the exact supplied clock, or null for non-date values.</summary>
        public DateTime? Clock { get; private set; }
        /// <summary>Creates an explicit date variable; no current clock or timezone conversion runs.</summary>
        /// <remarks>Exact clock fields are used for display; binary64 cache serials cannot preserve every DateTime tick.</remarks>
        public static DxfFieldVariable FromDateTime(DateTime value)
        { return new DxfFieldVariable(DrawingTime.ToJulianCalendar(value)) { Clock = value }; }
    }
}
