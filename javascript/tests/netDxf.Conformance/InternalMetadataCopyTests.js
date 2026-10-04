// Original public override-dispatch case. Document/database cases remain unported.
import { ApplicationRegistry, XData } from '../../index.js';
import { Run, Equal } from './TestHarness.js';
export function RegisterInternalMetadataCopyTests(){
  Run('metadata/public-clone-keeps-override',()=>{
    class MetadataCallbackRegistry extends ApplicationRegistry { Callback=null; Clone(...args){if(args.length===0)this.Callback?.();return super.Clone(...args);} }
    const app=new MetadataCallbackRegistry('PUBLIC_COPY');let called=0;app.Callback=()=>called++;
    new XData(app).Clone();Equal(1,called);
  });
}
