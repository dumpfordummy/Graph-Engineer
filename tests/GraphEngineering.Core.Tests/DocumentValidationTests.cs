using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Core.Documents;
using GraphEngineering.Core.Validation;
using GraphEngineering.Tests;
using Xunit;

namespace GraphEngineering.Core.Tests;

public sealed class DocumentValidationTests
{
    [Fact]
    public void LinearDocumentRoundTripsConfigurationIdentityAndLayout()
    {
        var input = DocumentFixture.Create();
        var parsed = DocumentFixture.Read(input);
        Assert.True(parsed.Success);
        Assert.True(GraphValidator.Validate(parsed).Valid);
        Assert.Contains("Execution is unavailable", GraphValidator.Validate(parsed).Scope);
        var document = parsed.Document!;
        var serialized = JsonSerializer.SerializeToNode(document, DocumentJson.Options)!;
        Assert.True(JsonNode.DeepEquals(input["definition"], serialized["definition"]));
        Assert.True(JsonNode.DeepEquals(input["layout"], serialized["layout"]));
        Assert.Equal(Guid.Parse(input["workflow"]!["id"]!.GetValue<string>()), document.Workflow.Id);
    }

    [Fact]
    public void ProviderReferenceMayBeAbsentWithoutInvalidatingDraft()
    {
        var input = DocumentFixture.Create();
        DocumentFixture.Nodes(input)[1]!["configuration"]!.AsObject().Remove("providerProfileId");
        Assert.True(GraphValidator.Validate(DocumentFixture.Read(input)).Valid);
    }

    [Theory]
    [InlineData("start", "missing_start")]
    [InlineData("end", "missing_end")]
    public void MissingTerminalIsSaveableButReported(string id, string code)
    {
        var input = DocumentFixture.Create();
        DocumentFixture.RemoveNode(input, id);
        var parsed = DocumentFixture.Read(input);
        Assert.True(parsed.Success);
        var report = GraphValidator.Validate(parsed);
        Assert.True(report.StructurallyValid);
        Assert.False(report.Valid);
        Assert.Contains(report.Issues, issue => issue.Code == code);
    }

    [Fact]
    public void EmptyDraftNamesAndPromptAreSemanticErrors()
    {
        var input = DocumentFixture.Create();
        input["workflow"]!["name"] = " ";
        DocumentFixture.Nodes(input)[1]!["name"] = "";
        DocumentFixture.Nodes(input)[1]!["configuration"]!["prompt"] = "\n";
        var report = GraphValidator.Validate(DocumentFixture.Read(input));
        Assert.True(report.StructurallyValid);
        Assert.Contains(report.Issues, issue => issue.Code == "empty_workflow_name");
        Assert.Contains(report.Issues, issue => issue.Code == "empty_node_name" && issue.NodeId == "model-1");
        Assert.Contains(report.Issues, issue => issue.Code == "empty_prompt" && issue.Path == "definition.nodes[1].configuration.prompt");
    }

    [Fact]
    public void BranchMergeDisconnectedAndUnreachableEndHaveActionableReferences()
    {
        var input = DocumentFixture.Create();
        DocumentFixture.AddEdge(input, "branch", "start", "model-2");
        DocumentFixture.Edges(input).RemoveAt(2);
        var report = GraphValidator.Validate(DocumentFixture.Read(input));
        Assert.True(report.StructurallyValid);
        Assert.Contains(report.Issues, issue => issue.Code == "unsupported_branch" && issue.NodeId == "start");
        Assert.Contains(report.Issues, issue => issue.Code == "unsupported_merge" && issue.NodeId == "model-2");
        Assert.Contains(report.Issues, issue => issue.Code == "disconnected_node" && issue.NodeId == "end");
        Assert.Contains(report.Issues, issue => issue.Code == "unreachable_end" && issue.NodeId == "end");
    }

    [Fact]
    public void CycleDiagnosticPointsToCycleInsteadOfItsDownstreamTail()
    {
        var input = DocumentFixture.Create();
        // The downstream edge c appears before the cycle-closing edge in document order.
        DocumentFixture.AddEdge(input, "cycle-back", "model-2", "model-1");
        var report = GraphValidator.Validate(DocumentFixture.Read(input));
        Assert.True(report.StructurallyValid);
        Assert.Contains(report.Issues, issue => issue.Code == "unsupported_cycle" && issue.EdgeId == "cycle-back");
    }

    [Fact]
    public void CycleInDisconnectedComponentIsStillReported()
    {
        var input = DocumentFixture.Create();
        DocumentFixture.Edges(input).RemoveAt(0);
        DocumentFixture.AddEdge(input, "cycle-back", "model-2", "model-1");
        var report = GraphValidator.Validate(DocumentFixture.Read(input));
        Assert.Contains(report.Issues, issue => issue.Code == "unsupported_cycle");
        Assert.Contains(report.Issues, issue => issue.Code == "unreachable_node" && issue.NodeId == "model-1");
    }

    public static IEnumerable<object[]> InvalidShapes()
    {
        yield return Case("formatVersion", input => input["formatVersion"] = 2);
        yield return Case("workflow.name", input => input["workflow"]!["name"] = new string('x', 121));
        yield return Case("workflow.revision", input => input["workflow"]!["revision"] = 9007199254740992L);
        yield return Case("workflow.id", input => input["workflow"]!["id"] = "not-a-uuid");
        yield return Case("workflow.createdAt", input => input["workflow"]!["createdAt"] = "2026-09-23T03:00:00+03:00");
        yield return Case("workflow.createdAt", input => input["workflow"]!["createdAt"] = "2026-9-3T00:00:00Z");
        yield return Case("workflow.name", input => input["workflow"]!.AsObject().Remove("name"));
        yield return Case("workflow.description", input => input["workflow"]!["description"] = null);
        yield return Case("runtime", input => input["runtime"] = new JsonObject());
        yield return Case("definition.nodes[1].type", input => DocumentFixture.Nodes(input)[1]!["type"] = "shell");
        yield return Case("definition.nodes[1].typeVersion", input => DocumentFixture.Nodes(input)[1]!["typeVersion"] = 2);
        yield return Case("definition.nodes[1].id", input => DocumentFixture.Nodes(input)[1]!["id"] = "start");
        yield return Case("definition.nodes[1].configuration.apiKey", input => DocumentFixture.Nodes(input)[1]!["configuration"]!["apiKey"] = "synthetic-only");
        yield return Case("definition.nodes[1].configuration.prompt", input => DocumentFixture.Nodes(input)[1]!["configuration"]!["prompt"] = 123);
        yield return Case("definition.edges[0].sourceNodeId", input => DocumentFixture.Edges(input)[0]!["sourceNodeId"] = "absent");
        yield return Case("definition.edges[0].targetPort", input => DocumentFixture.Edges(input)[0]!["targetPort"] = "out");
        yield return Case("definition.edges[0].sourcePort", input => DocumentFixture.Edges(input)[0]!["sourceNodeId"] = "end");
        yield return Case("definition.edges[0].targetPort", input => DocumentFixture.Edges(input)[0]!["targetNodeId"] = "start");
        yield return Case("definition.edges[1].id", input => DocumentFixture.Edges(input)[1]!["id"] = "a");
        yield return Case("definition.edges[3]", input => DocumentFixture.AddEdge(input, "duplicate", "start", "model-1"));
        yield return Case("layout.nodes", input => input["layout"]!["nodes"]!.AsArray().RemoveAt(0));
        yield return Case("layout.nodes[1].nodeId", input => input["layout"]!["nodes"]![1]!["nodeId"] = "start");
        yield return Case("layout.nodes[0].x", input => input["layout"]!["nodes"]![0]!["x"] = 100001);
        yield return Case("layout.viewport.zoom", input => input["layout"]!["viewport"]!["zoom"] = 0);
        yield return Case("layout.viewport.selected", input => input["layout"]!["viewport"]!["selected"] = true);
        yield return Case("definition.nodes", input => input["definition"]!["nodes"] = new JsonArray(Enumerable.Range(0, 201).Select(_ => DocumentFixture.Nodes(DocumentFixture.Create())[0]!.DeepClone()).ToArray()));
    }

    private static object[] Case(string expectedPath, Action<JsonObject> change)
    {
        var input = DocumentFixture.Create();
        change(input);
        return [expectedPath, input.ToJsonString()];
    }

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public void MalformedStructureIsRejectedWithFieldPaths(string expectedPath, string input)
    {
        using var json = DocumentReader.ParseJson(Encoding.UTF8.GetBytes(input));
        var parsed = DocumentReader.Read(json.RootElement);
        Assert.False(parsed.Success);
        Assert.Null(parsed.Document);
        Assert.Contains(parsed.Issues, issue => issue.Path == expectedPath);
        Assert.False(GraphValidator.Validate(parsed).StructurallyValid);
    }

    [Theory]
    [InlineData("{\"document\":{},\"document\":{}}")]
    [InlineData("{\"nested\":{\"prompt\":\"a\",\"prompt\":\"b\"}}")]
    [InlineData("{\"incomplete\":")]
    public void DuplicatePropertiesAndMalformedJsonAreRejected(string input) =>
        Assert.ThrowsAny<JsonException>(() => DocumentReader.ParseJson(Encoding.UTF8.GetBytes(input)));

    [Fact]
    public void ExtremelyLargeNumericLiteralIsRejectedWithoutThrowing()
    {
        var input = DocumentFixture.Create().ToJsonString().Replace("\"x\":80", "\"x\":1e1000", StringComparison.Ordinal);
        using var json = DocumentReader.ParseJson(Encoding.UTF8.GetBytes(input));
        Assert.Contains(DocumentReader.Read(json.RootElement).Issues, issue => issue.Path == "layout.nodes[0].x");
    }

    [Fact]
    public void InvalidUnicodeIsRejectedAtJsonBoundary()
    {
        var input = DocumentFixture.Create().ToJsonString().Replace("Synthetic workflow", "\\uD800", StringComparison.Ordinal);
        Assert.ThrowsAny<JsonException>(() =>
        {
            using var json = DocumentReader.ParseJson(Encoding.UTF8.GetBytes(input));
            DocumentReader.Read(json.RootElement);
        });
    }
}
