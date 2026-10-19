import {screen} from './core.mjs';
import {resolveInsertCommand} from './block-contracts.mjs';

async function decodeSvg(svg) {
  const url=URL.createObjectURL(new Blob([svg],{type:'image/svg+xml'})),image=new Image();
  try {image.src=url;await image.decode();return image;} finally {URL.revokeObjectURL(url);}
}

/** Receipt-bound transient graphics. Pointer motion repositions one decoded native
 * preview; it never sends per-pointer edits or duplicates CAD geometry in JavaScript. */
export class InsertionPreview {
  constructor(client,{invalidate=()=>{},error=()=>{},decode=decodeSvg}={}) {
    this.client=client;this.invalidate=invalidate;this.error=error;this.decode=decode;this.generation=0;
  }
  clear() {
    this.generation++;this.abort?.abort();this.abort=null;this.frame?.image.close?.();this.frame=null;
    this.signature=null;this.session=null;this.pending=null;
  }
  sync(session) {
    const command=session?.command,client=this.client;
    if(session?.name!=='INSERT'||!command?.block&&!command?.reference||session.selecting||client.busy||!client.state){this.clear();return;}
    if(this.session===session&&this.key===client.key&&this.epoch===client.epoch&&this.inputVersion===session.version)return;
    // Placement is a screen translation from a zero-anchor native insertion. Scale,
    // units, rotation, alternate base point and text frames come from the planner.
    let resolved;try{resolved=resolveInsertCommand(command,client.data?.blocks||[]);}catch{this.clear();return;}
    const insert={command:{...resolved,points:[{x:0,y:0,z:0}]}};
    if(session.attributeAssignments)insert.attributes=structuredClone(session.attributeAssignments);
    const signature=JSON.stringify([client.key,insert]);
    if(this.signature===signature&&this.session===session&&this.epoch===client.epoch){this.inputVersion=session.version;return;}
    this.clear();this.signature=signature;this.session=session;this.key=client.key;this.epoch=client.epoch;this.inputVersion=session.version;
    const generation=this.generation,abort=this.abort=new AbortController(),key=client.key;
    const current=()=>generation===this.generation&&key===client.key&&this.session===session&&!client.busy;
    this.pending=(async()=>{
      try {
        const response=await client.read('insert-preview',{body:{revision:client.state.revision,insert},signal:abort.signal});
        if(!response||!current())return;
        const b=response.imageBounds;
        if(typeof response.svg!=='string'||response.svg.length>8*1024*1024||!b||
            !['minX','minY','maxX','maxY'].every(axis=>Number.isFinite(b[axis]))||b.maxX<=b.minX||b.maxY<=b.minY)
          throw new Error('The insertion preview has invalid image bounds.');
        const image=await this.decode(response.svg);
        if(!current()){image.close?.();return;}
        this.frame={image,bounds:b,key};this.invalidate();
      } catch(error) {if(current()&&error.name!=='AbortError')this.error(error);}
      finally {if(generation===this.generation)this.pending=null;}
    })();
  }
  draw(context,camera,size,cursor,pixel) {
    const frame=this.frame,session=this.session;
    if(!frame||frame.key!==this.client.key||this.client.busy||!session)return false;
    const position=session.command.points?.[0]||cursor;
    if(!position||!pixel&&!session.command.points?.length)return false;
    const b=frame.bounds,topLeft=screen({x:position.x+b.minX,y:position.y+b.maxY},camera,size);
    context.save();context.globalAlpha=.72;
    context.drawImage(frame.image,topLeft.x,topLeft.y,(b.maxX-b.minX)*camera.scale,(b.maxY-b.minY)*camera.scale);
    context.restore();return true;
  }
}
