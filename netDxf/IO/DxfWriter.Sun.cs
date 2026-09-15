using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareSunClass(DxfClassCollection definitions)
        { this.PrepareStoredEnvelopeClass(definitions, "SUN", "AcDbSun", "SCENEOE", 1153, this.doc.Objects.Items.Any(item => item is DxfSun)); }
        private bool WriteSunPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfSun sun)) return false;
            this.chunk.Write(100, "AcDbSun");
            this.chunk.Write(90, sun.StoredVersion);
            this.chunk.Write(290, sun.Enabled);
            this.chunk.Write(63, sun.ColorIndex);
            if (sun.TrueColor.HasValue) this.chunk.Write(421, sun.TrueColor.Value);
            this.chunk.Write(40, sun.Intensity);
            this.chunk.Write(291, sun.ShadowsEnabled);
            this.chunk.Write(91, sun.JulianDay);
            this.chunk.Write(92, sun.StoredTime);
            this.chunk.Write(292, sun.DaylightSavingTime);
            this.chunk.Write(70, (short)sun.ShadowType);
            this.chunk.Write(71, sun.ShadowMapSize);
            this.chunk.Write(280, (short)sun.ShadowSoftness);
            return true;
        }
        private void WriteSunReference(DxfObject owner)
        {
            if (!SunReferences.IsPresent(owner)) return;
            SunReferences.CheckProfile(owner, this.doc.DrawingVariables.AcadVer);
            this.chunk.Write(361, SunReferences.Get(owner)?.Handle ?? "0");
        }
    }
}
