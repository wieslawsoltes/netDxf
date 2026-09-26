// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfVersion } from '../Header/DxfVersion.js';
import { SubclassMarker } from '../SubclassMarker.js';
import { EncodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { WriteViewUcs } from './DxfViewUcs.js';
import { WriteDatabaseMetadata } from './DxfWriter.Objects.js';
import { WriteSunReference } from './DxfWriter.Sun.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export function WriteView(chunk, document, view) {
            if (view.IsCameraPlottable && document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007)
            {
                throw new NotSupportedException("A plottable named-view camera requires AutoCAD 2007 DXF or later.");
            }
            chunk.Write(0, view.CodeName);
            chunk.Write(5, view.Handle);
            WriteDatabaseMetadata(chunk, document, view);
            chunk.Write(330, view.Owner.Handle);
            chunk.Write(100, SubclassMarker.TableRecord);
            chunk.Write(100, SubclassMarker.View);
            chunk.Write(2, EncodeDxfText(view.Name, document.DrawingVariables.AcadVer));
            chunk.Write(70, view.Flags);
            chunk.Write(40, view.Height);
            chunk.Write(10, view.ViewCenter.X);
            chunk.Write(20, view.ViewCenter.Y);
            chunk.Write(41, view.Width);
            chunk.Write(11, view.ViewDirection.X);
            chunk.Write(21, view.ViewDirection.Y);
            chunk.Write(31, view.ViewDirection.Z);
            chunk.Write(12, view.Target.X);
            chunk.Write(22, view.Target.Y);
            chunk.Write(32, view.Target.Z);
            chunk.Write(42, view.LensLength);
            chunk.Write(43, view.FrontClippingPlane);
            chunk.Write(44, view.BackClippingPlane);
            chunk.Write(50, view.Rotation);
            chunk.Write(71, view.ViewMode);
            chunk.Write(281, view.RenderMode);
            WriteViewUcs(chunk, view.Ucs);
            if (document.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007)
            {
                chunk.Write(73, view.IsCameraPlottable ? 1 : 0);
            }
            if (view.HasStoredLiveSection) chunk.Write(334, view.LiveSection === null ? "0" : view.LiveSection.Handle);
            WriteSunReference(chunk, document.DrawingVariables.AcadVer, view);
            WriteXData(chunk, () => document.DrawingVariables.AcadVer, view.XData);
}
