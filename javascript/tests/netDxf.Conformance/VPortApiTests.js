// Complete original validation case; registered-document cases remain unported.
import {VPort,Vector2,Vector3} from '../../index.js';
import {ArgumentNullException,ArgumentException,ArgumentOutOfRangeException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Near,Throws} from './TestHarness.js';
export function RegisterVPortApiTests() { Run('vport/api/validation-nonmutation',VPortValidation); }
    export function VPortValidation()
    {
        const record=new VPort("Validation");
        Throws(ArgumentNullException,()=>new VPort(null)); Throws(ArgumentNullException,()=>new VPort("  "));
        Throws(ArgumentException,()=>new VPort("*Other"));
        Check(new VPort("*aCtIvE").IsReserved,"Active constructor did not recognize case");
        for(const bad of [NaN,Infinity,-Infinity])
        {
            Throws(ArgumentOutOfRangeException,()=>{record.LowerLeftCorner=new Vector2(bad,0);});
            Throws(ArgumentOutOfRangeException,()=>{record.ViewTarget=new Vector3(0,bad,0);});
            Throws(ArgumentOutOfRangeException,()=>{record.ViewDirection=new Vector3(0,1,bad);});
            Throws(ArgumentOutOfRangeException,()=>{record.ViewHeight=bad;});
            Throws(ArgumentOutOfRangeException,()=>{record.ViewAspectRatio=bad;});
            Throws(ArgumentOutOfRangeException,()=>{record.LensLength=bad;});
            Throws(ArgumentOutOfRangeException,()=>{record.FrontClippingPlane=bad;});
            Throws(ArgumentOutOfRangeException,()=>{record.UcsElevation=bad;});
        }
        Throws(ArgumentException,()=>{record.ViewDirection=Vector3.Zero;});
        Throws(ArgumentException,()=>{record.UcsXAxis=Vector3.Zero;});
        Throws(ArgumentOutOfRangeException,()=>{record.ViewHeight=0;});
        Throws(ArgumentOutOfRangeException,()=>{record.ViewAspectRatio=-1;});
        Throws(ArgumentOutOfRangeException,()=>{record.CircleSides=0;});
        Throws(ArgumentOutOfRangeException,()=>{record.UcsIcon=4;});
        Throws(ArgumentOutOfRangeException,()=>{record.SnapStyle=2;});
        Throws(ArgumentOutOfRangeException,()=>{record.SnapIsopair=3;});
        Throws(ArgumentOutOfRangeException,()=>{record.RenderMode=7;});
        Throws(ArgumentOutOfRangeException,()=>{record.ViewMode=65536;});
        Equal(Vector3.UnitZ,record.ViewDirection,"Rejected vector assignment mutated state");
        Equal(Vector2.Zero,record.LowerLeftCorner,"Rejected corner assignment mutated state");
        Near(10,record.ViewHeight,"Rejected scalar assignment mutated state");
    }

