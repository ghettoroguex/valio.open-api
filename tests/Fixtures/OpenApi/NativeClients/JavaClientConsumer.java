import com.example.generated.api.EntriesApi;
import com.example.generated.models.Entry;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import okhttp3.Call;
import okhttp3.HttpUrl;
import okhttp3.MediaType;
import okhttp3.OkHttpClient;
import okhttp3.Protocol;
import okhttp3.Request;
import okhttp3.Response;
import okhttp3.ResponseBody;

public final class JavaClientConsumer {
    private JavaClientConsumer() { }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    public static void main(String[] arguments) throws Exception {
        AtomicInteger before = new AtomicInteger();
        AtomicInteger after = new AtomicInteger();
        AtomicInteger refreshes = new AtomicInteger();
        AtomicInteger wireRequests = new AtomicInteger();
        AtomicReference<String> token = new AtomicReference<>("expired");
        AtomicBoolean queryAndHeader = new AtomicBoolean();
        AtomicBoolean encodedPath = new AtomicBoolean();
        AtomicBoolean wireCanceled = new AtomicBoolean();
        OkHttpClient http = new OkHttpClient.Builder()
            .addInterceptor(chain -> {
                before.incrementAndGet();
                Request request = chain.request().newBuilder().header("Authorization", "Bearer " + token.get()).build();
                Response first = chain.proceed(request);
                Response result = first;
                if (first.code() == 401) {
                    first.close();
                    refreshes.incrementAndGet();
                    token.set("fresh");
                    result = chain.proceed(request.newBuilder().header("Authorization", "Bearer fresh").build());
                }
                after.incrementAndGet();
                return result;
            })
            .addInterceptor(chain -> {
                wireRequests.incrementAndGet();
                Request request = chain.request();
                String path = request.url().encodedPath();
                if (path.equals("/slow")) {
                    while (!chain.call().isCanceled()) {
                        try { Thread.sleep(10); }
                        catch (InterruptedException error) { Thread.currentThread().interrupt(); throw new IOException(error); }
                    }
                    wireCanceled.set(true);
                    throw new IOException("Canceled");
                }
                int status = 200;
                String payload = "{\"id\":7,\"label\":\"native\",\"score\":1.5,\"active\":true,\"tags\":[\"first\",\"second\"]}";
                if (path.equals("/entries/missing")) { status = 404; payload = "missing"; }
                else if (!"Bearer fresh".equals(request.header("Authorization"))) { status = 401; payload = "expired"; }
                else if (path.equals("/ping")) { status = 204; payload = ""; }
                else if (path.equals("/entries")) payload = "[" + payload + "]";
                else {
                    queryAndHeader.set("x y".equals(request.url().queryParameter("query.value")) &&
                        "request-7".equals(request.header("X-Request-ID")));
                    encodedPath.set(path.equalsIgnoreCase("/entries/a%2Fb"));
                }
                return new Response.Builder().request(request).protocol(Protocol.HTTP_1_1).code(status)
                    .message("native fixture").body(ResponseBody.create(payload, MediaType.get("application/json"))).build();
            }).build();
        EntriesApi client = new EntriesApi(http, HttpUrl.get("https://native.invalid/"), new ObjectMapper());
        Entry entry = client.getEntry("a/b", "x y", "request-7");
        check(entry.id() == 7 && entry.label().equals("native") && entry.score() == 1.5 && entry.active() && entry.tags().size() == 2,
            "Typed primitive and collection deserialization failed.");
        check(queryAndHeader.get(), "Wire query/header names were changed.");
        check(encodedPath.get(), "A slash in a path parameter was interpreted as an extra path segment.");
        check(refreshes.get() == 1 && before.get() == 1 && after.get() == 1 && wireRequests.get() == 2,
            "The injected interceptor/refresh pipeline was bypassed.");
        List<Entry> entries = client.listEntries();
        check(entries.size() == 1 && entries.getFirst().id() == 7, "Generic collection responses lost their element type.");
        client.ping();
        boolean rejected = false;
        try { client.getEntry("missing", null, null); }
        catch (IOException error) { rejected = error.getMessage().contains("404"); }
        check(rejected, "Non-success HTTP status was deserialized as success.");
        int beforeCanceled = wireRequests.get();
        boolean canceledBeforeSend = false;
        try { client.slow(Call::cancel); }
        catch (IOException error) { canceledBeforeSend = true; }
        check(canceledBeforeSend && wireRequests.get() == beforeCanceled, "An already canceled Call reached the transport.");
        AtomicReference<Call> activeCall = new AtomicReference<>();
        CompletableFuture<Boolean> active = CompletableFuture.supplyAsync(() -> {
            try { client.slow(activeCall::set); return false; }
            catch (IOException error) { return true; }
        });
        long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(5);
        while (activeCall.get() == null && System.nanoTime() < deadline) Thread.sleep(10);
        check(activeCall.get() != null, "The native Call was not exposed before execution.");
        while (wireRequests.get() == beforeCanceled && System.nanoTime() < deadline) Thread.sleep(10);
        activeCall.get().cancel();
        check(active.get(5, TimeUnit.SECONDS) && wireCanceled.get(), "Native Call cancellation did not reach the active transport.");
        http.dispatcher().executorService().shutdown();
        http.connectionPool().evictAll();
        System.out.println("native Java client pipeline passed");
    }
}
