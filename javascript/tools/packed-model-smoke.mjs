// Runs against the offline-installed tarball rather than checkout modules.
import * as packedModels from '@netdxf/javascript';
{
  const {DimensionStyle,DimensionStyleOverride,DimensionStyleOverrideDictionary,DimensionStyleOverrideType,DimensionArrowhead,
    MultiLeader,MLeaderMTextContent,MLeaderNode,MLeaderLine,DxfMLeaderStyle,TextStyle,Linetype,Vector3}=packedModels;
  const dimension=DimensionStyle.Iso25,dimensionCopy=dimension.Clone('Packed');
  if(dimensionCopy.TextHeight!==2.5||dimensionCopy.Tolerances===dimension.Tolerances)throw new Error('Packed dimension style clone failed');
  const overrides=new DimensionStyleOverrideDictionary();overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.ArrowSize,2));
  if(overrides.get_Item(DimensionStyleOverrideType.ArrowSize).Value!==2||DimensionArrowhead.Open.Entities.Count!==3)throw new Error('Packed dimension overrides or arrowhead failed');
  const leader=new MultiLeader(),style=new DxfMLeaderStyle(),textStyle=TextStyle.Default;
  style.Properties.TextStyle=textStyle;leader.Properties.Style=style;leader.Properties.LeaderLinetype=Linetype.ByLayer;leader.Properties.TextStyle=textStyle;
  leader.Context.MText=new MLeaderMTextContent();leader.Context.MText.Style=textStyle;leader.Context.MText.Text='Packed leader';
  const node=new MLeaderNode(),line=new MLeaderLine();line.Vertices.Add(Vector3.Zero);line.Vertices.Add(Vector3.UnitX);node.Lines.Add(line);leader.Context.Leaders.Add(node);
  leader.Validate();const copy=leader.Clone();
  if(copy.Context.MText===leader.Context.MText||copy.Properties.Style!==style||copy.Context.Leaders.get_Item(0).Parent!==copy.Context)throw new Error('Packed MULTILEADER ownership clone failed');
  copy.Context.Leaders.get_Item(0).Lines.get_Item(0).Vertices.Clear();
  if(line.Vertices.Count!==2||copy.Context.MText.Text!=='Packed leader')throw new Error('Packed MULTILEADER source mutated by clone');
}
