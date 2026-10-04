// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Globalization;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        // Encoding conversion tables can be available even when the provider does not
        // supply Encoding.WindowsCodePage metadata. Do not make R12 plan construction
        // depend on that optional runtime property. These OEM mappings follow Microsoft's
        // documented WindowsCodePage table; all targets are known DXF ANSI encodings.
        // https://learn.microsoft.com/dotnet/api/system.text.encoding.windowscodepage
        internal static string ModernSelectionCodePage(string declaration, int codePage)
        {
            if (!declaration.StartsWith("DOS", StringComparison.OrdinalIgnoreCase))
                return declaration;

            int target;
            switch (codePage)
            {
                case 720: case 864: target = 1256; break;
                case 737: case 869: target = 1253; break;
                case 775: target = 1257; break;
                case 852: target = 1250; break;
                case 857: target = 1254; break;
                case 862: target = 1255; break;
                case 866: target = 1251; break;
                case 874: case 932: case 936: case 949: case 950:
                case 1250: case 1251: case 1252: case 1253: case 1254:
                case 1255: case 1256: case 1257: case 1258:
                    target = codePage; break;
                default:
                    // Western OEM pages (437/850/855/858/860/861/863/865) map to
                    // 1252. Other numeric DOS aliases accepted by the raw codec use
                    // the same explicit portable target, not a guessed platform ACP.
                    // Modern Unicode escapes retain characters outside that target.
                    target = 1252; break;
            }
            return "ANSI_" + target.ToString(CultureInfo.InvariantCulture);
        }
    }
}
