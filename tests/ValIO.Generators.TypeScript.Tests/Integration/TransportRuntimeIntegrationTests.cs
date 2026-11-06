using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Tests.Fixtures;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Integration;

/// <summary>Executes generated request serialization against native FormData, Blob, and Headers values.</summary>
[TestFixture]
[Category("NodeIntegration")]
public sealed class TransportRuntimeIntegrationTests
{
    /// <summary>Verifies actual wire field values and omitted bodies through each generated transport boundary.</summary>
    /// <param name="transport">The generated HTTP client transport.</param>
    /// <returns>The asynchronous generated-code runtime check.</returns>
    [TestCase("fetch")]
    [TestCase("axios")]
    [TestCase("ky")]
    [TestCase("got")]
    [TestCase("ofetch")]
    [TestCase("undici")]
    [TestCase("nextjs")]
    [TestCase("solid")]
    [TestCase("angular-rxjs")]
    [TestCase("angular-promise")]
    public async Task GeneratedRequests_PreserveMultipartHeadersAndNoContentSemantics(string transport)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(Document());
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions
        {
            Transports = [transport],
            ValidationLibrary = transport == "angular-promise" ? "zod4" : null,
        });
        var captionName = context.IrDocument.Models.SelectMany(model => model.Properties)
            .First(property => property.OriginalName == "Title.Text").Name;
        var output = await GeneratedTypeScriptRuntime.Run(context, $$"""
            const transport = {{JsonSerializer.Serialize(transport)}};
            const Module = require('node:module');
            const originalLoad = Module._load;
            let captured;
            const unexpectedJson = async () => { throw new Error('A bodyless response was decoded as JSON'); };
            const response = () => ({ status: 204, statusCode: 204, data: undefined, _data: undefined,
                                      json: unexpectedJson, body: { json: unexpectedJson } });
            async function send(url, options) { captured = options; return response(); }
            const axios = { request: async options => { captured = { ...options, body: options.data }; return response(); } };
            const ky = Object.assign(send, { get: send, post: send, put: send, patch: send, delete: send, head: send });
            class HttpFields {
              constructor(values = {}) { this.values = { ...values }; }
              set(name, value) { return new HttpFields({ ...this.values, [name]: value }); }
            }
            Module._load = function(name, parent, isMain) {
              if (name === 'axios') return { __esModule: true, default: axios };
              if (name === 'ky') return { __esModule: true, default: ky };
              if (name === 'got') return { __esModule: true, default: send };
              if (name === 'ofetch') return { $fetch: { raw: send } };
              if (name === 'undici') return { request: send };
              if (name === '@angular/core') return { Injectable: () => value => value };
              if (name === '@angular/common/http') return { HttpParams: HttpFields, HttpHeaders: HttpFields };
              return originalLoad.call(this, name, parent, isMain);
            };
            globalThis.fetch = send;
            function endpoint(name) {
              if (transport === 'angular-promise') {
                const { UploadsPromiseService } = require(path.join(root, 'api', 'uploadsPromiseService.service.js'));
                const { of } = require('rxjs');
                const http = { post: (url, body, options = {}) => {
                  captured = { ...options, body };
                  return of(options.observe === 'response' ? { status: 204, body: null } : null);
                } };
                const service = new UploadsPromiseService(http);
                return service[name].bind(service);
              }
              if (transport === 'angular-rxjs') {
                const { UploadsService } = require(path.join(root, 'api', 'uploadsService.service.js'));
                const http = { post: async (url, body, options = {}) => { captured = { ...options, body }; } };
                const service = new UploadsService(http);
                return service[name].bind(service);
              }
              return require(path.join(root, 'api', name, name + '.' + transport + '.js'))[name];
            }
            function headers() {
              return new Headers(captured.headers instanceof HttpFields ? captured.headers.values : captured.headers);
            }
            (async () => {
              const form = { file: new Blob(['picture']), metadata: { published: true }, tags: ['one', null, 'two'],
                             bytes: new Uint8Array([0, 255]), [{{JsonSerializer.Serialize(captionName)}}]: 'wire caption' };
              await endpoint('upload')({ form, headers: { X_Request_ID: 'request-42' } });
              assert(captured.body instanceof FormData, transport + ' did not send FormData');
              assert.equal(headers().get('X-Request-ID'), 'request-42');
              assert.equal(headers().has('X-Trace-ID'), false);
              assert.equal(headers().has('Content-Type'), false, 'multipart boundary must be generated by the transport');
              assert.equal(await captured.body.get('file').text(), 'picture');
              assert.equal(captured.body.get('Title.Text'), 'wire caption');
              assert.equal(captured.body.get('metadata'), '{"published":true}');
              assert.deepEqual(captured.body.getAll('tags'), ['one', 'two']);
              assert.deepEqual(Array.from(new Uint8Array(await captured.body.get('bytes').arrayBuffer())), [0, 255]);

              await endpoint('uploadOptional')({});
              assert.equal(captured.body, undefined, transport + ' serialized an absent optional body');

              await endpoint('encoded')({ form: { [{{JsonSerializer.Serialize(captionName)}}]: 'space & + Unicode: π', count: 0, active: false, tags: ['one', 'two'] } });
              assert.equal(typeof captured.body, 'string', transport + ' did not send URL-encoded text');
              const encoded = new URLSearchParams(captured.body);
              assert.equal(encoded.get('Title.Text'), 'space & + Unicode: π');
              assert.equal(encoded.get('count'), '0');
              assert.equal(encoded.get('active'), 'false');
              assert.deepEqual(encoded.getAll('tags'), ['one', 'two']);
              assert.equal(headers().get('Content-Type'), 'application/x-www-form-urlencoded');

              if (transport !== 'angular-rxjs') {
                const result = await endpoint('mixed')({ body: 'input' });
                assert.deepEqual(result, { status: 204, body: undefined, isSuccessful: true, isFailed: false });
                if (transport === 'nextjs' || transport === 'undici')
                  assert.equal(headers().get('Content-Type'), 'application/json');
                if (transport === 'angular-promise')
                  await assert.rejects(async () => endpoint('mixed')({ body: 42 }), 'Promise requests must validate before dispatch');
                if (transport === 'nextjs') {
                  await endpoint('mixed')({ body: 'input' }, { headers: new Headers([['content-type', 'application/custom'], ['x-extra', 'extra']]) });
                  assert.equal(headers().get('Content-Type'), 'application/custom');
                  assert.equal(headers().get('X-Extra'), 'extra');
                  await endpoint('mixed')({ body: 'input' }, { headers: [['x-tuple', 'tuple']] });
                  assert.equal(headers().get('X-Tuple'), 'tuple');
                  assert.equal(headers().get('Content-Type'), 'application/json');
                }
              }
              console.log('transport runtime passed');
            })().catch(error => { console.error(error); process.exitCode = 1; });
            """);

        output.Should().Contain("transport runtime passed");
    }

    /// <summary>Creates multipart, optional multipart, and mixed typed/no-body response contracts.</summary>
    /// <returns>The OpenAPI request serialization fixture.</returns>
    private static string Document() => OpenApiFixture.Read("Transport/runtime-contracts-3.0.json");
}
