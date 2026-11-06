const Module = require('node:module');
const originalLoad = Module._load;
const { Observable, of } = require('rxjs');
Module._load = function(name, parent, isMain) {
  if (name === '@angular/core') return { Injectable: () => value => value };
  if (name === '@angular/common/http') return {};
  return originalLoad.call(this, name, parent, isMain);
};
const { UploadsPromiseService } = require(path.join(root, 'api', 'uploadsPromiseService.service.js'));
let subscriptions = 0;
let teardowns = 0;
let status = 200;
let body = 'accepted';
let wait = false;
const service = new UploadsPromiseService({
  post: () => new Observable(subscriber => {
    subscriptions++;
    if (!wait) {
      subscriber.next({ status, body });
      subscriber.complete();
    }
    return () => teardowns++;
  })
});

(async () => {
  const preAborted = new AbortController();
  const reason = new Error('caller aborted Angular request');
  preAborted.abort(reason);
  await assert.rejects(service.mixed({ body: 'input' }, preAborted.signal), error => error === reason);
  assert.equal(subscriptions, 0, 'Pre-abort must avoid subscribing the HTTP request');

  const controller = new AbortController();
  let listeners = 0;
  const add = controller.signal.addEventListener.bind(controller.signal);
  const remove = controller.signal.removeEventListener.bind(controller.signal);
  controller.signal.addEventListener = (...args) => { listeners++; return add(...args); };
  controller.signal.removeEventListener = (...args) => { listeners--; return remove(...args); };
  wait = true;
  const pending = service.mixed({ body: 'input' }, controller.signal);
  assert.equal(subscriptions, 1);
  assert.equal(listeners, 1);
  controller.abort(reason);
  await assert.rejects(pending, error => error === reason);
  assert.equal(teardowns, 1, 'Abort must unsubscribe the active HttpClient observable');
  assert.equal(listeners, 0, 'Abort listeners must be released after cancellation');

  wait = false;
  const completion = new AbortController();
  const result = await service.mixed({ body: 'input' }, completion.signal);
  assert.deepEqual(result, { status: 200, body: 'accepted', isSuccessful: true, isFailed: false });
  assert.equal(teardowns, 2);
  body = 42;
  await assert.rejects(service.mixed({ body: 'input' }), 'The response schema must still validate after cancellation adaptation');
  status = 204;
  body = null;
  const noContent = await service.mixed({ body: 'input' });
  assert.deepEqual(noContent, { status: 204, body: undefined, isSuccessful: true, isFailed: false });
  const previous = subscriptions;
  await assert.rejects(async () => service.mixed({ body: 42 }));
  assert.equal(subscriptions, previous, 'Invalid requests must fail before transport subscription');
  console.log('Angular Promise cancellation runtime passed');
})().catch(error => { console.error(error); process.exitCode = 1; });
