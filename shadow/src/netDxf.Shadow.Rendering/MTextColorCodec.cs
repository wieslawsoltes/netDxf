#region netDxf library licensed under the MIT License
//
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
#endregion

namespace netDxf.Shadow.Rendering;

/// <summary>
/// Detached color decoding for MTEXT controls. The indexed values deliberately
/// match netDxf.AciColor, including its historical palette entries. There is no
/// dependency on mutable source color instances or a graphics backend.
/// </summary>
internal static class MTextColorCodec
{
    // Unmanaged constant data is emitted by the compiler; no mutable palette is
    // exposed and no per-lookup arrays, objects or source colors are allocated.
    private static ReadOnlySpan<uint> IndexedColors =>
    [
        0xffff0000u, 0xffffff00u, 0xff00ff00u, 0xff00ffffu, 0xff0000ffu, 0xffff00ffu, 0xffffffffu, 0xff808080u,
        0xffc0c0c0u, 0xffff0000u, 0xffff7f7fu, 0xffa50000u, 0xffa55252u, 0xff7f0000u, 0xff7f3f3fu, 0xff4c0000u,
        0xff4c2626u, 0xff260000u, 0xff261313u, 0xffff3f00u, 0xffff9f7fu, 0xffa52900u, 0xffa56752u, 0xff7f1f00u,
        0xff7f4f3fu, 0xff4c1300u, 0xff4c2f26u, 0xff260900u, 0xff261c13u, 0xffff7f00u, 0xffffbf7fu, 0xffa55200u,
        0xffa57c52u, 0xff7f3f00u, 0xff7f5f3fu, 0xff4c2600u, 0xff4c3926u, 0xff261300u, 0xff261c13u, 0xffffbf00u,
        0xffffdf7fu, 0xffa57c00u, 0xffa59152u, 0xff7f5f00u, 0xff7f6f3fu, 0xff4c3900u, 0xff4c4226u, 0xff261c00u,
        0xff262113u, 0xffffff00u, 0xffffff7fu, 0xffa5a500u, 0xffa5a552u, 0xff7f7f00u, 0xff7f7f3fu, 0xff4c4c00u,
        0xff4c4c26u, 0xff262600u, 0xff262613u, 0xffbfff00u, 0xffdfff7fu, 0xff7ca500u, 0xff91a552u, 0xff5f7f00u,
        0xff6f7f3fu, 0xff394c00u, 0xff424c26u, 0xff1c2600u, 0xff212613u, 0xff7fff00u, 0xffbfff7fu, 0xff52a500u,
        0xff7ca552u, 0xff3f7f00u, 0xff5f7f3fu, 0xff264c00u, 0xff394c26u, 0xff132600u, 0xff1c2613u, 0xff3fff00u,
        0xff9fff7fu, 0xff29a500u, 0xff67a552u, 0xff1f7f00u, 0xff4f7f3fu, 0xff134c00u, 0xff2f4c26u, 0xff092600u,
        0xff172613u, 0xff00ff00u, 0xff7dff7fu, 0xff00a500u, 0xff52a552u, 0xff007f00u, 0xff3f7f3fu, 0xff004c00u,
        0xff264c26u, 0xff002600u, 0xff132613u, 0xff00ff3fu, 0xff7fff9fu, 0xff00a529u, 0xff52a567u, 0xff007f1fu,
        0xff3f7f4fu, 0xff004c13u, 0xff264c2fu, 0xff002609u, 0xff135817u, 0xff00ff7fu, 0xff7fffbfu, 0xff00a552u,
        0xff52a57cu, 0xff007f3fu, 0xff3f7f5fu, 0xff004c26u, 0xff264c39u, 0xff002613u, 0xff13581cu, 0xff00ffbfu,
        0xff7fffdfu, 0xff00a57cu, 0xff52a591u, 0xff007f5fu, 0xff3f7f6fu, 0xff004c39u, 0xff264c42u, 0xff00261cu,
        0xff135858u, 0xff00ffffu, 0xff7fffffu, 0xff00a5a5u, 0xff52a5a5u, 0xff007f7fu, 0xff3f7f7fu, 0xff004c4cu,
        0xff264c4cu, 0xff002626u, 0xff135858u, 0xff00bfffu, 0xff7fdfffu, 0xff007ca5u, 0xff5291a5u, 0xff005f7fu,
        0xff3f6fd9u, 0xff00394cu, 0xff26427eu, 0xff001c26u, 0xff135858u, 0xff007fffu, 0xff7fbfffu, 0xff0052a5u,
        0xff527ca5u, 0xff003f7fu, 0xff3f5f7fu, 0xff00264cu, 0xff26397eu, 0xff001326u, 0xff131c58u, 0xff003fffu,
        0xff7f9fffu, 0xff0029a5u, 0xff5267a5u, 0xff001f7fu, 0xff3f4f7fu, 0xff00134cu, 0xff262f7eu, 0xff000926u,
        0xff131758u, 0xff0000ffu, 0xff7f7fffu, 0xff0000a5u, 0xff5252a5u, 0xff00007fu, 0xff3f3f7fu, 0xff00004cu,
        0xff26267eu, 0xff000026u, 0xff131358u, 0xff3f00ffu, 0xff9f7fffu, 0xff2900a5u, 0xff6752a5u, 0xff1f007fu,
        0xff4f3f7fu, 0xff13004cu, 0xff2f267eu, 0xff090026u, 0xff171358u, 0xff7f00ffu, 0xffbf7fffu, 0xffa50052u,
        0xff7c52a5u, 0xff3f007fu, 0xff5f3f7fu, 0xff26004cu, 0xff39267eu, 0xff130026u, 0xff1c1358u, 0xffbf00ffu,
        0xffdf7fffu, 0xff7c00a5u, 0xff8e52a5u, 0xff5f007fu, 0xff6f3f7fu, 0xff39004cu, 0xff42264cu, 0xff1c0026u,
        0xff581358u, 0xffff00ffu, 0xffff7fffu, 0xffa500a5u, 0xffa552a5u, 0xff7f007fu, 0xff7f3f7fu, 0xff4c004cu,
        0xff4c264cu, 0xff260026u, 0xff581358u, 0xffff00bfu, 0xffff7fdfu, 0xffa5007cu, 0xffa55291u, 0xff7f005fu,
        0xff7f3f6fu, 0xff4c0039u, 0xff4c2642u, 0xff26001cu, 0xff581358u, 0xffff007fu, 0xffff7fbfu, 0xffa50052u,
        0xffa5527cu, 0xff7f003fu, 0xff7f3f5fu, 0xff4c0026u, 0xff4c2639u, 0xff260013u, 0xff58131cu, 0xffff003fu,
        0xffff7f9fu, 0xffa50029u, 0xffa55267u, 0xff7f001fu, 0xff7f3f4fu, 0xff4c0013u, 0xff4c262fu, 0xff260009u,
        0xff581317u, 0xff000000u, 0xff656565u, 0xff666666u, 0xff999999u, 0xffccccccu, 0xffffffffu,
    ];

    /// <summary>
    /// Returns opaque ARGB for ACI 1..255. ByBlock (0), ByLayer (256), and invalid
    /// indices return null; the parser retains responsibility for inheritance
    /// and diagnostics. Index zero is not a paint entry in this API.
    /// </summary>
    internal static uint? ResolveIndexed(int index)
        => index is >= 1 and <= 255 ? IndexedColors[index - 1] : null;

    /// <summary>
    /// MTEXT's numeric true-color control stores red in the least-significant
    /// byte (0x00BBGGRR), unlike the managed scene's 0xAARRGGBB paint convention.
    /// Shift explicitly so decoding is independent of host byte order.
    /// </summary>
    internal static uint DecodeTrueColor(int value)
    {
        if (value is < 0 or > 0x00ffffff)
            throw new ArgumentOutOfRangeException(nameof(value), "MTEXT true color must be a 24-bit value.");
        var packed = (uint)value;
        return 0xff000000u | ((packed & 0xffu) << 16)
            | (packed & 0xff00u) | ((packed >> 16) & 0xffu);
    }
}
