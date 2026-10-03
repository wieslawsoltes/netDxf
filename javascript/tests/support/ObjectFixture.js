import { DxfTag, DxfRawDocument, DxfVersionStringValues } from '../../index.js';
/** Independent raw fixture, not a substitute for JavaScript typed DxfDocument authoring. */
export function ObjectFixture(version = 18, binary = false) {
  const tags = [
    [0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,DxfVersionStringValues[version]],
    [9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[9,'$HANDSEED'],[5,'100'],[0,'ENDSEC'],
    [0,'SECTION'],[2,'TABLES'],[0,'TABLE'],[2,'BLOCK_RECORD'],[5,'1'],
    [0,'BLOCK_RECORD'],[5,'2'],[330,'1'],[100,'AcDbSymbolTableRecord'],[100,'AcDbBlockTableRecord'],[2,'*Model_Space'],
    [0,'ENDTAB'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],
    [0,'LINE'],[5,'A'],[330,'2'],[100,'AcDbEntity'],[100,'AcDbLine'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6],
    [0,'CIRCLE'],[5,'B'],[330,'2'],[100,'AcDbEntity'],[100,'AcDbCircle'],[10,7],[20,8],[30,9],[40,2.5],
    [0,'ENDSEC'],[0,'SECTION'],[2,'OBJECTS'],[0,'DICTIONARY'],[5,'10'],[330,'0'],[100,'AcDbDictionary'],[281,1],
    [0,'ENDSEC'],[0,'EOF'],
  ].map(([code,value])=>new DxfTag(code,value));
  return DxfRawDocument.Load(DxfRawDocument.Create(tags,binary).ToBytes());
}
