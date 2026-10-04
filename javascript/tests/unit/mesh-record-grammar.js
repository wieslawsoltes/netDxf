// Functional subsets of allocation-sensitive pinned mesh tests. These are
// deliberately supplemental: no JavaScript heap metric replaces .NET allocation.
import test from 'node:test';
import { DxfVersion } from '../../index.js';
import { SetTypedIOConfiguration, GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { SupportedVersions, VersionName, BooleanName } from '../netDxf.Conformance/TestHarness.js';
import { MeshReadLargeDeclarationsFunctional } from '../netDxf.Conformance/MeshReadValidationTests.js';
import { MeshOverrideRejectFunctional } from '../netDxf.Conformance/MeshOverrideDeclarationTests.js';
import { MeshFramingDuplicateFunctional, MeshFramingOrphanFunctional, MeshFramingReentryFunctional,
  MeshFramingEarlyInvalidOverrideFunctional, MeshFramingLargeDuplicateFunctional } from '../netDxf.Conformance/MeshFieldFramingTests.js';
for(const config of ['Release','Debug'])for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2010))for(const b of [false,true]){
  const id=`${config}/${VersionName(v)}/${BooleanName(b)}`;
  const add=(name,action)=>test(`mesh functional ${id}/${name}`,()=>{const before=GetTypedIOConfiguration();SetTypedIOConfiguration(config);try{action();}finally{SetTypedIOConfiguration(before);}});
  add('large-declarations',()=>MeshReadLargeDeclarationsFunctional(v,b));
  for(const s of ['nonzero','marker','property-count','negative','large','private-then-public','subclass-then-public'])add('override/'+s,()=>MeshOverrideRejectFunctional(v,b,s));
  for(const code of [71,72,91,92,93,94,95])for(const changed of [false,true])for(const after of [false,true])add(`duplicate/${code}/${changed}/${after}`,()=>MeshFramingDuplicateFunctional(v,b,code,changed,after));
  for(const after of [false,true]){add(`duplicate/90/${after}`,()=>MeshFramingDuplicateFunctional(v,b,90,false,after));for(const code of [92,93,94,95])add(`empty-duplicate/${code}/${after}`,()=>MeshFramingDuplicateFunctional(v,b,code,false,after,true));}
  for(const code of [10,20,30,140])for(const p of [0,1,2])add(`orphan/${code}/${p}`,()=>MeshFramingOrphanFunctional(v,b,code,p));
  for(const code of [71,92,95])for(const count of [-1,1,2147483647])add(`early-override/${code}/${count}`,()=>MeshFramingEarlyInvalidOverrideFunctional(v,b,code,count));
  for(const code of [92,93,94,95])for(const after of [false,true])add(`large-duplicate/${code}/${after}`,()=>MeshFramingLargeDuplicateFunctional(v,b,code,after));
  for(const scope of ['private','subclass'])add(`reentry/${scope}`,()=>MeshFramingReentryFunctional(v,b,scope));
}
