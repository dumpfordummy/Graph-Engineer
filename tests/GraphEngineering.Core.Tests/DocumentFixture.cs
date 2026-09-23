using System.Text.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Core.Documents;

namespace GraphEngineering.Tests;

internal static class DocumentFixture
{
    public static JsonObject Create() => JsonNode.Parse("""
        {
          "formatVersion": 1,
          "workflow": {
            "id": "11111111-1111-4111-8111-111111111111",
            "name": "Synthetic workflow", "description": "Draft with two Model Calls", "revision": 0,
            "createdAt": "2026-09-23T00:00:00Z", "updatedAt": "2026-09-23T00:00:00Z"
          },
          "definition": {
            "nodes": [
              {"id":"start","type":"start","typeVersion":1,"name":"Start","description":"Input","configuration":{"sampleInput":"Synthetic input"}},
              {"id":"model-1","type":"modelCall","typeVersion":1,"name":"Summarize","description":"First model","configuration":{"prompt":"Summarize this input.","providerProfileId":null}},
              {"id":"model-2","type":"modelCall","typeVersion":1,"name":"Review","description":"Second model","configuration":{"prompt":"Review the summary.","providerProfileId":"future-profile"}},
              {"id":"end","type":"end","typeVersion":1,"name":"End","description":"Output","configuration":{"resultReference":"model-2 output (description only)"}}
            ],
            "edges": [
              {"id":"a","sourceNodeId":"start","sourcePort":"out","targetNodeId":"model-1","targetPort":"in"},
              {"id":"b","sourceNodeId":"model-1","sourcePort":"out","targetNodeId":"model-2","targetPort":"in"},
              {"id":"c","sourceNodeId":"model-2","sourcePort":"out","targetNodeId":"end","targetPort":"in"}
            ]
          },
          "layout": {
            "nodes":[{"nodeId":"start","x":80,"y":100},{"nodeId":"model-1","x":400,"y":150},{"nodeId":"model-2","x":700,"y":180},{"nodeId":"end","x":1000,"y":100}],
            "viewport":{"x":-14,"y":23,"zoom":0.8}
          }
        }
        """)!.AsObject();

    public static DocumentReadResult Read(JsonNode document)
    {
        using var json = DocumentReader.ParseJson(JsonSerializer.SerializeToUtf8Bytes(document));
        return DocumentReader.Read(json.RootElement);
    }
    public static JsonArray Nodes(JsonNode document) => document["definition"]!["nodes"]!.AsArray();
    public static JsonArray Edges(JsonNode document) => document["definition"]!["edges"]!.AsArray();
    public static void AddEdge(JsonNode document, string id, string source, string target) => Edges(document).Add(new JsonObject
    {
        ["id"] = id, ["sourceNodeId"] = source, ["sourcePort"] = "out", ["targetNodeId"] = target, ["targetPort"] = "in"
    });
    public static void RemoveNode(JsonNode document, string id)
    {
        var nodes = Nodes(document);
        nodes.Remove(nodes.First(node => node!["id"]!.GetValue<string>() == id));
        var edges = Edges(document);
        foreach (var edge in edges.Where(edge => edge!["sourceNodeId"]!.GetValue<string>() == id || edge["targetNodeId"]!.GetValue<string>() == id).ToArray()) edges.Remove(edge);
        var positions = document["layout"]!["nodes"]!.AsArray();
        positions.Remove(positions.First(position => position!["nodeId"]!.GetValue<string>() == id));
    }
}
