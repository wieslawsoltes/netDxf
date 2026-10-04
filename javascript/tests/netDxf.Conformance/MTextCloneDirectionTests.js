// Complete detached clone cases from the pinned C# source; nested/wire cases remain unported.
import { MText, Vector3, MTextDrawingDirection, MTextAttachmentPoint, MTextLineSpacingStyle } from '../../index.js';
import { Run, Check, Equal, Near } from './TestHarness.js';
export function RegisterMTextCloneDirectionTests() {
  for (const [name,direction] of Object.entries(MTextDrawingDirection)) Run(`mtext/direction/clone/${name}`,()=>MTextDirectionClone(direction));
}
export function DirectionalMText(direction) {
  const text = new MText('Line one\\PZażółć 東京',new Vector3(1.25,-2.5,3.75),4.5,20);
  Object.assign(text,{DrawingDirection:direction,Rotation:30,AttachmentPoint:MTextAttachmentPoint.MiddleRight,LineSpacingFactor:1.5,LineSpacingStyle:MTextLineSpacingStyle.Exact});return text;
}
export function MTextDirectionClone(direction) {
  const original=DirectionalMText(direction),copy=original.Clone();
  Equal(direction,copy.DrawingDirection,'MText clone lost drawing direction');Equal(original.Value,copy.Value,'MText clone value');
  Equal(original.Position,copy.Position,'MText clone position');Equal(original.AttachmentPoint,copy.AttachmentPoint,'MText clone attachment');
  Near(original.Height,copy.Height,'MText clone height');Near(original.Rotation,copy.Rotation,'MText clone rotation');Near(original.RectangleWidth,copy.RectangleWidth,'MText clone width');
  Equal(original.LineSpacingStyle,copy.LineSpacingStyle,'MText clone line spacing style');Near(original.LineSpacingFactor,copy.LineSpacingFactor,'MText clone line spacing');
  Check(copy.Owner===null && copy.Handle===null,'MText clone retained document identity.');
  copy.DrawingDirection=direction===MTextDrawingDirection.TopToBottom?MTextDrawingDirection.LeftToRight:MTextDrawingDirection.TopToBottom;
  Equal(direction,original.DrawingDirection,'Editing the clone changed source direction');
}
