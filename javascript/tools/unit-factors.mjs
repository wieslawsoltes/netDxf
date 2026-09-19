// Development-only generation: preserve the production Decimal conversion, not double division.
import fs from 'node:fs';
import path from 'node:path';
import { OracleClient } from './OracleClient.mjs';
import { oracleRoot, javascriptRoot, baseline } from './dotnet.mjs';
import { fromBits } from './wire.mjs';
const oracle = new OracleClient({args:[path.join(oracleRoot,'GeometryOracle.dll')]});
try {
  const rows = await oracle.request({op:'unit-factors'});
  if (!Array.isArray(rows) || rows.length !== 25 || rows.some(row=>row.length!==25)) throw new Error('Invalid production unit-factor response.');
  const output = '// Generated from UnitHelper.ConversionFactor in the pinned production .NET assembly.\n' +
    '// Source: '+baseline.ref+'; Decimal-to-double semantics retained as binary64 literals.\n' +
    'export const UnitFactors = Object.freeze([\n' + rows.map(row=>'  '+row.map(bits=>String(fromBits(bits))).join(', ')).join(',\n') + '\n]);\n';
  const target=path.join(javascriptRoot,'runtime','UnitFactors.generated.js');
  if(process.argv.includes('--check')) { if(fs.readFileSync(target,'utf8')!==output) throw new Error('Unit conversion data drift.'); }
  else fs.writeFileSync(target,output);
  console.log('Verified 625 production Decimal conversion factors.');
} finally { await oracle.close(); }
