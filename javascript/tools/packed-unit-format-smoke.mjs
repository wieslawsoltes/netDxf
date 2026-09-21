// Runs solely from the offline-installed tarball; no test oracle is a runtime dependency.
import * as packedUnitFormats from '@netdxf/javascript';
{
  const {AngleUnitFormat,LinearUnitFormat,UnitStyleFormat,Culture,FractionFormatType}=packedUnitFormats;
  const format=new UnitStyleFormat();format.LinearDecimalPlaces=2;format.AngularDecimalPlaces=0;
  if(LinearUnitFormat.ToDecimal(1.005,format)!=='1.01')throw new Error('Packed decimal midpoint rounding differs');
  if(AngleUnitFormat.ToDecimal(2.5,format)!=='3°'||AngleUnitFormat.ToDegreesMinutesSeconds(2.5,format)!=='2°')throw new Error('Packed angle rounding modes differ');
  format.FractionType=FractionFormatType.NotStacked;format.LinearDecimalPlaces=3;
  if(LinearUnitFormat.ToFractional(-.5,format)!=='0 -4/8')throw new Error('Packed signed fraction reduction differs');
  format.LinearDecimalPlaces=2;format.SuppressZeroFeet=true;
  const previous=Culture.Current;
  try{Culture.Current='pl-PL';if(LinearUnitFormat.ToEngineering(1.2345,format)!=='1,2345"')throw new Error('Packed engineering culture/hidden-feet behavior differs');}
  finally{Culture.Current=previous;}
  format.AngularDecimalPlaces=0;format.DegreesSymbol='{0:X4}';
  if(AngleUnitFormat.ToDegreesMinutesSeconds(15,format)!=='15000F')throw new Error('Packed embedded-symbol formatting differs');
}
