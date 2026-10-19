/** Remember explicit disclosure choices by tool call across polling and checkpoint hydration. */
export class ToolDisclosure {
  constructor(maximum = 1200) { this.maximum = maximum; this.choices = new Map(); }
  bind(detail, item) {
    const key = item.callId || item.id, approval = item.status === 'approval';
    detail.open = approval || this.choices.get(key) === true;
    let observed = detail.open;
    const capture = () => {
      if (approval) { if (!detail.open) detail.open = true; return; }
      if (detail.open === observed) return;
      observed = detail.open; this.choices.delete(key); this.choices.set(key, observed);
      while (this.choices.size > this.maximum) this.choices.delete(this.choices.keys().next().value);
    };
    detail.addEventListener('toggle', capture);
    return { dispose: () => { capture(); detail.removeEventListener('toggle', capture); } };
  }
  clear() { this.choices.clear(); }
}
