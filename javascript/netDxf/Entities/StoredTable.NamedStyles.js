// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { StoredTableState } from '../../runtime/StoredTableState.js';
import { InvalidOperationException, ArgumentException } from '../../runtime/Errors.js';
export function ResolveTableNamedStyles(owner) {
  const s=StoredTableState(owner),tags=Array.from(owner.Payload),at=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbTable');if(at<0)return;
  const header=[];for(let i=at+1;i<tags.length&&tags[i].Code!==100&&tags[i].Code!==171;i++)header.push(tags[i]);
  const flags=header.filter(t=>t.Code===90);if(flags.length!==1||flags[0].Value!==22)return;
  let table=false,value=false,depth=0;
  for(const tag of tags){
    if(tag.Code===100){table=tag.Value==='AcDbTable';continue;}if(!table)continue;
    if(tag.Code===102){if(tag.Value.startsWith('{'))depth++;else if(tag.Value==='}'&&depth>0)depth--;continue;}if(depth>0)continue;
    if(tag.Code===301&&tag.Value==='CELL_VALUE'){value=true;continue;}if(tag.Code===304&&tag.Value==='ACVALUE_END'){value=false;continue;}if(value||tag.Code!==7)continue;
    const result={};if(!s.source.TextStyles.TryGetValue(s.decode(tag.Value),result))continue;
    if(s.namedStyles.has(tag))throw new ArgumentException('An item with the same key has already been added.');
    s.namedStyles.set(tag,[result.value,result.value.Name]);s.references.push(result.value);
  }
}
export function ChangedTableTextStyle(owner,tag){const binding=StoredTableState(owner).namedStyles.get(tag);return !binding||binding[0].Name===binding[1]?null:binding[0].Name;}
export function ValidateTableNamedStyles(owner){const s=StoredTableState(owner);for(const [style] of s.namedStyles.values())if(s.source.GetObjectByHandle(style.Handle)!==style)throw new InvalidOperationException('A stored TABLE text-style dependency is no longer registered.');}
