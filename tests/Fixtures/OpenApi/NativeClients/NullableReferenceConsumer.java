import com.example.generated.models.NullableReferences;
import com.example.generated.models.NullableState;
import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.core.JsonGenerator;
import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.databind.DeserializationContext;
import com.fasterxml.jackson.databind.JsonDeserializer;
import com.fasterxml.jackson.databind.JsonSerializer;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializerProvider;
import com.fasterxml.jackson.databind.module.SimpleModule;
import java.io.IOException;

/** Executes the nullable-reference wire contract against actual generated records and Jackson. */
public final class NullableReferenceConsumer {
    private NullableReferenceConsumer() { }

    /** Checks nulls, model objects, enums and nullable container members without Object casts. */
    public static void main(String[] arguments) throws Exception {
        var mapper = new ObjectMapper();
        // Java's existing dependency-free enum contract delegates wire codecs to the caller's mapper.
        var codecs = new SimpleModule();
        codecs.addSerializer(NullableState.class, new JsonSerializer<NullableState>() {
            @Override public void serialize(NullableState value, JsonGenerator output, SerializerProvider provider) throws IOException {
                output.writeString(value.value());
            }
        });
        codecs.addDeserializer(NullableState.class, new JsonDeserializer<NullableState>() {
            @Override public NullableState deserialize(JsonParser input, DeserializationContext context) throws IOException {
                if (!input.hasToken(com.fasterxml.jackson.core.JsonToken.VALUE_STRING))
                    return (NullableState) context.handleUnexpectedToken(NullableState.class, input);
                for (var candidate : NullableState.values()) {
                    if (candidate.value().equals(input.getText())) return candidate;
                }
                return (NullableState) context.handleWeirdStringValue(NullableState.class, input.getText(), "Undeclared enum token");
            }
        });
        mapper.registerModule(codecs);
        var empty = mapper.readValue("""
            {"country":null,"state":null,"countries":[null],"states":{"one":null}}
            """, NullableReferences.class);
        if (empty.country() != null || empty.state() != null || empty.optionalCountry() != null ||
            empty.countries().getFirst() != null || empty.states().get("one") != null)
            throw new AssertionError("Null references were lost.");
        var populated = mapper.readValue("""
            {"country":{"id":7,"name":"DE"},"state":"granted","optionalCountry":{"id":8,"name":"RU"},"optionalState":"pending","countries":[null,{"id":9,"name":"US"}],"states":{"one":"pending","two":null}}
            """, NullableReferences.class);
        if (populated.country().id() != 7 || populated.state() != NullableState.GRANTED ||
            populated.optionalCountry().id() != 8 || populated.countries().get(1).id() != 9 ||
            populated.states().get("one") != NullableState.PENDING)
            throw new AssertionError("Structured references lost their specific types.");
        var roundTrip = mapper.readValue(mapper.writeValueAsString(populated), NullableReferences.class);
        if (roundTrip.country().id() != 7 || roundTrip.state() != NullableState.GRANTED || roundTrip.states().get("two") != null)
            throw new AssertionError("Nullable reference round-trip failed.");
        for (var malformed : new String[] {
            "{\"country\":\"DE\",\"state\":null,\"countries\":[],\"states\":{}}",
            "{\"country\":null,\"state\":\"unknown\",\"countries\":[],\"states\":{}}"
        }) {
            try {
                mapper.readValue(malformed, NullableReferences.class);
                throw new AssertionError("Malformed reference was accepted.");
            } catch (JsonProcessingException expected) { }
        }
        System.out.println("Java nullable references passed");
    }
}
