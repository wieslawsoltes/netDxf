// Five complete original detached API cases; original collection/IO cases remain unregistered.
import {View,Vector2,Vector3,ViewFlags,ViewModeFlags,ViewRenderMode,DxfVersion,XData,XDataRecord,XDataCode,ApplicationRegistry} from '../../index.js';
import {ArgumentNullException,ArgumentException,ArgumentOutOfRangeException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Near,Throws} from './TestHarness.js';
const ViewApplication='DXF_VIEW_CONFORMANCE';
export function RegisterNamedViewTests() {
        Run("view/api/defaults", () =>
        {
            const view = new View("Default");
            Equal(Vector3.UnitZ, view.ViewDirection, "default view direction");
            Equal(Vector2.Zero, view.ViewCenter, "default view center");
            Near(40, view.LensLength, "legacy default lens length");
            Near(1, view.Height, "default view height");
            Near(1, view.Width, "default view width");
            Equal(ViewFlags.None, view.Flags, "default flags");
            Equal(ViewRenderMode.TwoDimensionalOptimized, view.RenderMode, "default render mode");
            Check(!view.IsCameraPlottable && !view.IsPaperSpace, "Unexpected view defaults.");
            Throws(ArgumentNullException, () => new View(null));
        });
        Run("view/api/aliases", () =>
        {
            const view = Object.assign(new View("Aliases"),{Camera:new Vector3(2,3,4),Fov:65,Viewmode:ViewModeFlags.Perspective});
            Equal(new Vector3(2, 3, 4), view.ViewDirection, "camera alias must not normalize");
            Near(65, view.LensLength, "lens alias");
            Equal(ViewModeFlags.Perspective, view.ViewMode, "mode alias");
            view.ViewDirection = new Vector3(5, 6, 7); view.LensLength = 82; view.ViewMode = ViewModeFlags.BackClippingPlane;
            Equal(new Vector3(5, 6, 7), view.Camera, "reverse camera alias");
            Near(82, view.Fov, "reverse lens alias");
            Equal(ViewModeFlags.BackClippingPlane, view.Viewmode, "reverse mode alias");
        });
        Run("view/api/validation", () =>
        {
            const view = new View("Validation");
            for (const bad of [NaN, Infinity, -Infinity])
            {
                Throws(ArgumentOutOfRangeException, () => {view.Height = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.Width = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.LensLength = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.Rotation = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.FrontClippingPlane = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.BackClippingPlane = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.Target = new Vector3(1, bad, 3);});
                Throws(ArgumentOutOfRangeException, () => {view.ViewCenter = new Vector2(bad, 2);});
                Throws(ArgumentOutOfRangeException, () => {view.ViewDirection = new Vector3(1, 2, bad);});
            }
            for (const bad of [0, -1.0])
            {
                Throws(ArgumentOutOfRangeException, () => {view.Height = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.Width = bad;});
                Throws(ArgumentOutOfRangeException, () => {view.LensLength = bad;});
            }
            Throws(ArgumentException, () => {view.Camera = Vector3.Zero;});
            Throws(ArgumentOutOfRangeException, () => {view.RenderMode = 7;});
            Throws(ArgumentOutOfRangeException, () => {view.RenderMode = (-1);});
            Throws(ArgumentOutOfRangeException, () => {view.ViewMode = 65536;});
            Equal(Vector3.UnitZ, view.ViewDirection, "rejected direction assignment changed state");
            Near(1, view.Height, "rejected height assignment changed state");
        });
        Run("view/api/flags", () =>
        {
            const view = Object.assign(new View("Flags"),{Flags:ViewFlags.ExternallyDependent | ViewFlags.ExternalReferenceResolved | ViewFlags.Referenced});
            const original = view.Flags;
            view.IsPaperSpace = true;
            Equal(original | ViewFlags.PaperSpace, view.Flags, "set paper flag");
            view.IsPaperSpace = false;
            Equal(original, view.Flags, "clear paper flag must retain other flags");
        });
        Run("view/api/clone", () =>
        {
            const view = NamedView(3, DxfVersion.AutoCad2018);
            const copy =view.Clone("Copy");
            AssertViewValues(view, copy);
            const bytes = copy.XData.get_Item(ViewApplication).XDataRecord.get_Item(1).Value;
            bytes[0] = 255;
            Equal(3, view.XData.get_Item(ViewApplication).XDataRecord.get_Item(1).Value[0], "view clone binary metadata isolation");
            Check(copy.Owner == null && copy.Handle == null, "Clone retained document ownership.");
        });
}

function NamedView(index,version) {
 const view=Object.assign(new View('View_'+index+'_Zażółć_東京'),{
  ViewCenter:new Vector2(index+.25,-index-.5),ViewDirection:new Vector3(index+1,2,3),Target:new Vector3(index+10,-20,30),
  Height:index+12.125,Width:index+20.25,LensLength:index+85.5,Rotation:index-37.25,FrontClippingPlane:index-4.5,BackClippingPlane:index+50.5,
  Flags:ViewFlags.Referenced|(index%2===0?ViewFlags.PaperSpace:ViewFlags.None),
  ViewMode:ViewModeFlags.Perspective|ViewModeFlags.FrontClippingPlane|ViewModeFlags.BackClippingPlane|ViewModeFlags.FrontClipNotAtEye,
  RenderMode:index,IsCameraPlottable:version>=DxfVersion.AutoCad2007&&index%2===1
 });
 const metadata=new XData(new ApplicationRegistry(ViewApplication));metadata.XDataRecord.Add(new XDataRecord(XDataCode.String,'View metadata '+index));
 metadata.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(index,0,255)));view.XData.Add(metadata);return view;
}
function AssertViewValues(expected,actual) {
 for(const name of ['ViewCenter','ViewDirection','Target','Flags','ViewMode','RenderMode','IsCameraPlottable'])Equal(expected[name],actual[name],name);
 for(const name of ['Height','Width','LensLength','Rotation','FrontClippingPlane','BackClippingPlane'])Near(expected[name],actual[name],name);
}
