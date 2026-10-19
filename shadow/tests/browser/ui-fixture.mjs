// Small DOM adapter for async UI contract tests; it records real element trees and event handlers.
export class Element {
  constructor(tag) { this.tagName=tag.toUpperCase();this.children=[];this.attributes={};this.handlers=new Map();this.dataset={};this.parentNode=null;this.disabled=false;this.hidden=false;this.classList={add(){},toggle(){}}; }
  append(...values){for(const value of values){if(value instanceof Element){value.remove();value.parentNode=this;}this.children.push(value);}}
  replaceChildren(...values){for(const child of this.children)if(child instanceof Element)child.parentNode=null;this.children=[];this.append(...values);}
  remove(){if(this.parentNode){const siblings=this.parentNode.children;siblings.splice(siblings.indexOf(this),1);this.parentNode=null;}}
  setAttribute(name,value){this.attributes[name]=String(value);if(name==='value')this.value=value;if(name==='class')this.className=value;}
  getAttribute(name){return this.attributes[name]??null;}
  addEventListener(name,callback){if(!this.handlers.has(name))this.handlers.set(name,[]);this.handlers.get(name).push(callback);}
  dispatch(name,options={}){return Promise.all((this.handlers.get(name)||[]).map(callback=>callback({target:this,preventDefault(){},...options})));}
  click(){if(!this.disabled)return this.dispatch('click');return Promise.resolve();}
  set textContent(value){this.replaceChildren(String(value));}
  get textContent(){return this.children.map(child=>child instanceof Element?child.textContent:String(child)).join('');}
  set value(value){this.inputValue=String(value);}
  get value(){return this.inputValue??(this.tagName==='SELECT'?this.children.find(child=>child.tagName==='OPTION')?.value:'')??'';}
  get isConnected(){return this.attached===true||this.parentNode?.isConnected===true;}
  contains(value){return all(this).includes(value);}
  focus(){document.activeElement=this;}
  querySelectorAll(selector){return all(this).filter(node=>selector==='details[open]'?node.tagName==='DETAILS'&&node.open:node.tagName.toLowerCase()===selector);}
}
export function all(root){return [root,...root.children.flatMap(child=>child instanceof Element?all(child):[])];}
export function byText(root,text){return all(root).find(node=>node.tagName==='BUTTON'&&node.textContent===text);}
export function byLabel(root,label){return all(root).find(node=>node.getAttribute('aria-label')===label);}
export const tick=()=>new Promise(resolve=>setImmediate(resolve));
export function gate(){let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});return {promise,resolve,reject};}
export function installDom(t){
  const original=globalThis.document,storage=globalThis.localStorage;
  const root=new Element('main');root.attached=true;const head=new Element('head');head.attached=true;
  globalThis.document={createElement:tag=>new Element(tag),createElementNS:(namespace,tag)=>Object.assign(new Element(tag),{namespaceURI:namespace}),createDocumentFragment:()=>new Element('#fragment'),baseURI:'https://studio.test/',head,activeElement:root,querySelector:()=>null};
  globalThis.localStorage={getItem:()=>null,setItem(){}};
  t.after(()=>{globalThis.document=original;globalThis.localStorage=storage;});return root;
}
