/** Host-independent ROTATE/SCALE option acquisition. The host supplies its bounded
 * numeric/coordinate parser; no drawing is changed until the session completes. */
export class TransformAcquisition {
  constructor(session, math) {
    Object.defineProperties(this,{session:{value:session},math:{value:math}});
    this.phase='value'; this.first=null; this.reference=null;
  }
  get rotate() { return this.session.name==='ROTATE'; }
  get base() { return this.session.command.points[0]; }
  get anchor() { return this.first || this.base; }
  get previewPoints() { return this.first ? [this.first] : this.base ? [this.base] : []; }
  get step() {
    const unit=this.rotate?'angle':'length', copy=this.session.command.transform?.copy?' (Copy)':'';
    if(this.phase==='reference-second')return {kind:'point',prompt:`Specify second reference ${unit} point${copy}`};
    if(this.phase==='target-first')return {kind:'point',prompt:`Specify first new ${unit} point${copy}`};
    if(this.phase==='target-second')return {kind:'point',prompt:`Specify second new ${unit} point${copy}`};
    const prompt=this.phase==='reference'?`Specify reference ${unit} or first point${copy}`:
      this.phase==='target'?`Specify new ${unit} or [Points]${copy}`:
      `${this.rotate?'Rotation in degrees':'Scale factor'} or [Copy/Reference]${copy}`;
    const defaultValue=this.phase==='value'?(this.rotate?90:2):this.phase==='reference'?(this.rotate?0:1):this.measure(this.reference);
    return {kind:'number',key:'value',prompt,defaultValue,min:this.rotate?-1e12:1e-9,max:1e12};
  }
  scalar(value) {
    this.math.finite(value);
    if(!this.rotate && value<1e-9)throw new Error('Specify a positive length or factor of at least 1e-9.');
    return value;
  }
  measure(measure) {
    if(measure.value!==undefined)return this.scalar(measure.value);
    const [a,b]=measure.points, dx=b.x-a.x,dy=b.y-a.y,dz=(b.z??0)-(a.z??0);
    const length=this.rotate?Math.hypot(dx,dy):Math.hypot(dx,dy,dz);
    this.math.finite(length);
    if(length<1e-9)throw new Error(`Specify distinct ${this.rotate?'XY ':''}reference points.`);
    return this.scalar(this.rotate?Math.atan2(dy,dx)*180/Math.PI:length);
  }
  change(phase, first=null) { this.phase=phase;this.first=first;this.session.version++; }
  input(text,cursor) {
    const value=String(text).trim(),option=value.toUpperCase();
    if(['value','reference','target'].includes(this.phase)&&['C','COPY'].includes(option)) {
      this.session.command.transform={...this.session.command.transform,copy:true};this.session.version++;return;
    }
    if(this.phase==='value'&&['R','REFERENCE'].includes(option)){this.change('reference');return;}
    if(this.phase==='target'&&['P','POINTS'].includes(option)){this.change('target-first');return;}
    if(this.step.kind==='point'||/[,@<#]/.test(value))
      return this.pick(this.math.parsePoint(value,this.anchor,cursor));
    const number=this.scalar(value?this.math.number(value):this.step.defaultValue);
    if(this.phase==='reference'){this.reference={value:number};this.change('target');return;}
    this.finish({value:number});
  }
  pick(value) {
    for(const n of [value.x,value.y,value.z??0])this.math.finite(n);
    const p={x:value.x,y:value.y,z:value.z??0};
    if(this.phase==='reference'){this.change('reference-second',p);return;}
    if(this.phase==='target-first'){this.change('target-second',p);return;}
    const measure={points:[{...(this.first||this.base)},p]};
    this.measure(measure); // Validate before changing an acquisition receipt.
    if(this.phase==='reference-second'){this.reference=measure;this.change('target');return;}
    this.finish(measure);
  }
  finish(target) {
    const value=this.measure(target);
    if(this.reference) {
      const reference=this.measure(this.reference);
      if(!this.rotate)this.scalar(value/reference);
      this.session.command.transform={...this.session.command.transform,reference:this.reference,target};
    } else this.session.command.value=value;
    this.session.complete=true;this.session.version++;
  }
}
