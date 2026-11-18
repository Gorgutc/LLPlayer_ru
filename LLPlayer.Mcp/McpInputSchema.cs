using System.Text.Json.Nodes;

namespace LLPlayer.Mcp;

internal static class McpInputSchema
{
    // Some MCP registries only accept the structural subset of JSON Schema.
    // ControlHost retains its full capability schemas and validates the original DTOs on invocation.
    public static JsonObject Project(JsonObject schema)
    {
        if (schema.Remove("format", out JsonNode? format) && format?.GetValue<string>() == "uuid")
        {
            string? description = schema["description"]?.GetValue<string>();
            schema["description"] = description is null ? "Use a UUID." : $"{description} Use a UUID.";
        }
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, value) in properties.ToArray())
            {
                if (value is not JsonObject child) continue;
                JsonObject projected = Project(child);
                if (!ReferenceEquals(child, projected)) properties[name] = projected;
            }
        }

        if (schema["items"] is JsonObject items)
        {
            JsonObject projected = Project(items);
            if (!ReferenceEquals(items, projected)) schema["items"] = projected;
        }

        if (schema["additionalProperties"] is JsonObject)
            schema["additionalProperties"] = true;

        if (schema["type"] is JsonArray { Count: 2 } types && types.Any(type => type?.GetValue<string>() == "null"))
        {
            schema["type"] = types.Single(type => type?.GetValue<string>() != "null")!.GetValue<string>();
            return WithNullAlternative(schema);
        }

        if (schema["type"] is null && schema["enum"] is JsonArray { Count: > 0 } values)
        {
            // System.Text.Json exports string enums without an explicit type, including null as a value.
            schema["type"] = "string";
            if (values.Any(value => value is null))
            {
                schema["enum"] = new JsonArray(values.Where(value => value is not null).Select(value => value!.DeepClone()).ToArray());
                return WithNullAlternative(schema);
            }
        }

        return schema;
    }

    private static JsonObject WithNullAlternative(JsonObject schema)
    {
        var nonNull = schema.DeepClone().AsObject();
        var projected = new JsonObject();
        foreach (string annotation in new[] { "description", "title", "default", "examples" })
        {
            if (nonNull.Remove(annotation, out JsonNode? value))
                projected[annotation] = value;
        }
        projected["oneOf"] = new JsonArray(nonNull, new JsonObject { ["type"] = "null" });
        return projected;
    }
}
