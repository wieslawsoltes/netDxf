// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private Light ReadLight()
        {
            if (this.chunk.Code != 100 || this.chunk.ReadString() != SubclassMarker.Light)
                throw new InvalidDataException("LIGHT requires the AcDbLight subclass.");
            var light = new Light();
            var seen = new HashSet<short>();
            Vector3 position = light.Position, target = light.Target;
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                short code = this.chunk.Code;
                if (code == 1001)
                {
                    string app = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    light.XData.Add(this.ReadXDataRecord(this.GetApplicationRegistry(app)));
                    continue;
                }
                // Unknown field preservation is provided by DxfRawDocument, not guessed here.
                if (code == 100 || code == 101 || code == 102)
                    throw new InvalidDataException("Unsupported or duplicate LIGHT subclass/payload marker.");
                bool known = code == 90 || code == 1 || code == 70 || code == 290 || code == 291 || code == 40 ||
                    code == 10 || code == 20 || code == 30 || code == 11 || code == 21 || code == 31 || code == 72 ||
                    code == 292 || code == 41 || code == 42 || code == 50 || code == 51 || code == 293 ||
                    code == 73 || code == 91 || code == 280;
                if (known && !seen.Add(code)) throw new InvalidDataException("Duplicate LIGHT group " + code + ".");
                try
                {
                    switch (code)
                    {
                        case 90: light.VersionNumber = this.chunk.ReadInt(); break;
                        case 1: light.Name = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                        case 70: light.LightType = (LightType)this.chunk.ReadShort(); break;
                        case 290: light.IsOn = this.chunk.ReadBool(); break;
                        case 291: light.PlotGlyph = this.chunk.ReadBool(); break;
                        case 40: light.Intensity = this.chunk.ReadDouble(); break;
                        case 10: position.X = this.chunk.ReadDouble(); break;
                        case 20: position.Y = this.chunk.ReadDouble(); break;
                        case 30: position.Z = this.chunk.ReadDouble(); break;
                        case 11: target.X = this.chunk.ReadDouble(); break;
                        case 21: target.Y = this.chunk.ReadDouble(); break;
                        case 31: target.Z = this.chunk.ReadDouble(); break;
                        case 72: light.AttenuationType = (LightAttenuationType)this.chunk.ReadShort(); break;
                        case 292: light.UseAttenuationLimits = this.chunk.ReadBool(); break;
                        case 41: light.AttenuationStartLimit = this.chunk.ReadDouble(); break;
                        case 42: light.AttenuationEndLimit = this.chunk.ReadDouble(); break;
                        case 50: light.HotspotAngle = this.chunk.ReadDouble(); break;
                        case 51: light.FalloffAngle = this.chunk.ReadDouble(); break;
                        case 293: light.CastShadows = this.chunk.ReadBool(); break;
                        case 73: light.ShadowType = (LightShadowType)this.chunk.ReadShort(); break;
                        case 91: light.ShadowMapSize = this.chunk.ReadInt(); break;
                        case 280: light.ShadowMapSoftness = this.chunk.ReadShort(); break;
                        default:
                            if (code >= 1000 && code <= 1071)
                                throw new InvalidDataException("LIGHT XData must start with an application registry.");
                            break;
                    }
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException("Invalid LIGHT group " + code + " at position " + this.chunk.CurrentPosition + ".", error);
                }
                this.chunk.Next();
            }
            foreach (short start in new short[] { 10, 11 })
            {
                int count = (seen.Contains(start) ? 1 : 0) + (seen.Contains((short)(start + 10)) ? 1 : 0) +
                    (seen.Contains((short)(start + 20)) ? 1 : 0);
                if (count != 0 && count != 3) throw new InvalidDataException("Incomplete LIGHT point at group " + start + ".");
            }
            light.Position = position; light.Target = target;
            return light;
        }
    }

    internal sealed partial class DxfWriter
    {
        private void ValidateLightVersions()
        {
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007) return;
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                    if (entity is Light)
                        throw new NotSupportedException("LIGHT requires AutoCAD 2007 (AC1021) or later in this writer profile.");
        }

        private void WriteLight(Light light)
        {
            this.chunk.Write(100, SubclassMarker.Light);
            this.chunk.Write(90, light.VersionNumber);
            this.chunk.Write(1, this.EncodeNonAsciiCharacters(light.Name));
            this.chunk.Write(70, (short)light.LightType);
            this.chunk.Write(290, light.IsOn); this.chunk.Write(291, light.PlotGlyph);
            this.chunk.Write(40, light.Intensity);
            this.chunk.Write(10, light.Position.X); this.chunk.Write(20, light.Position.Y); this.chunk.Write(30, light.Position.Z);
            this.chunk.Write(11, light.Target.X); this.chunk.Write(21, light.Target.Y); this.chunk.Write(31, light.Target.Z);
            this.chunk.Write(72, (short)light.AttenuationType); this.chunk.Write(292, light.UseAttenuationLimits);
            this.chunk.Write(41, light.AttenuationStartLimit); this.chunk.Write(42, light.AttenuationEndLimit);
            this.chunk.Write(50, light.HotspotAngle); this.chunk.Write(51, light.FalloffAngle);
            this.chunk.Write(293, light.CastShadows); this.chunk.Write(73, (short)light.ShadowType);
            this.chunk.Write(91, light.ShadowMapSize); this.chunk.Write(280, light.ShadowMapSoftness);
            this.WriteXData(light.XData);
        }
    }
}
