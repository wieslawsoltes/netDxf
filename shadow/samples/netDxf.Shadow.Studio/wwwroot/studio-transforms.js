'use strict';
document.querySelectorAll('[data-transform]').forEach(button => {
  button.addEventListener('click', () => action('transform', {
    operation: button.dataset.transform,
    amount: Number(button.dataset.amount || 1)
  }).catch(notify));
});
