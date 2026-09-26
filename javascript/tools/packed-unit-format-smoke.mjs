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

// Drawing utilities are imported from the installed package, never the checkout.
{
  const {DrawingTime,HeaderDateTime,StringEnum,StringComparison,DxfVersion}=await import('@netdxf/javascript');
  const standalone=await import('@netdxf/javascript/netDxf/Units/DrawingTime.js');
  const enumModule=await import('@netdxf/javascript/netDxf/StringEnum.js');
  if(standalone.DrawingTime!==DrawingTime||enumModule.StringEnum!==StringEnum)throw new Error('Drawing utility exports differ.');
  if(DrawingTime.ToJulianCalendar(HeaderDateTime.MinValue)!==1721426||DrawingTime.FromJulianCalendar(1721426).Ticks!==0n||
     DrawingTime.EditingTime(-1.25).Ticks!==-1080000000000n)throw new Error('DrawingTime installed package mismatch.');
  const Helper=StringEnum.For(DxfVersion),values=new Helper().GetValues();
  if(Helper.Parse('ac1032',StringComparison.OrdinalIgnoreCase)!==18||Helper.GetStringValue(18)!=='AC1032'||
     values.get_Item(18)!=='AC1032')throw new Error('StringEnum installed package mismatch.');
  values.Clear();if(new Helper().GetValues().Count!==19)throw new Error('StringEnum snapshots share state.');
}
