using System;
using System.Collections.Generic;
using netDxf.Blocks;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Gets a host block's typed geographic metadata, or null when absent or preserved opaquely.</summary>
        public DxfGeoData GetGeoData(BlockRecord hostBlock)
        {
            if (hostBlock == null) throw new ArgumentNullException(nameof(hostBlock));
            this.CheckRegistered(hostBlock);
            DxfDictionary extension = hostBlock.ExtensionDictionary;
            return extension != null && extension.Contains("ACAD_GEOGRAPHICDATA") ? extension["ACAD_GEOGRAPHICDATA"] as DxfGeoData : null;
        }
        /// <summary>Attaches detached GEODATA under its host block's ACAD_GEOGRAPHICDATA extension entry.</summary>
        /// <remarks>An existing entry is never overwritten. Edit the existing typed object through GetGeoData instead.</remarks>
        public void SetGeoData(DxfGeoData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            this.CheckRegistered(data.HostBlock);
            if (data.Database != null || data.Owner != null) throw new ArgumentException("GEODATA must be detached and unowned.", nameof(data));
            var errors = new List<string>(); data.ValidateValues(this, errors);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            DxfDictionary extension = data.HostBlock.ExtensionDictionary;
            if (extension != null && extension.Contains("ACAD_GEOGRAPHICDATA")) throw new InvalidOperationException("The host already has geographic metadata.");
            if (extension != null) extension.Add("ACAD_GEOGRAPHICDATA", data);
            else
            {
                extension = new DxfDictionary(); extension.Add("ACAD_GEOGRAPHICDATA", data);
                try { this.SetExtensionDictionary(data.HostBlock, extension); }
                catch { if (data.Database == null) data.Owner = null; throw; }
            }
        }
    }
}
