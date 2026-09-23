using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Core.Documents;
using GraphEngineering.Core.Execution;
using GraphEngineering.Tests;
using Xunit;

namespace GraphEngineering.Core.Tests;

public sealed class ExecutionBindingTests
{
    private static readonly IReadOnlyDictionary<string, NodeOutput> NoOutputs = new Dictionary<string, NodeOutput>();

    [Fact]
    public void LegacyDocumentsStayUnchangedAndRequireExplicitExecutionUpgrade()
    {
        var legacy = DocumentFixture.Create();
        var before = legacy.ToJsonString();
        var parsed = DocumentFixture.Read(legacy);
        var plan = ExecutionPlanner.Validate(parsed.Document!);
        Assert.False(plan.Ready);
        Assert.Equal(3, plan.Issues.Count(issue => issue.Code == "execution_upgrade_required"));
        Assert.Equal(before, legacy.ToJsonString());
        var roundTrip = JsonSerializer.SerializeToNode(parsed.Document, DocumentJson.Options)!;
        Assert.True(JsonNode.DeepEquals(legacy["definition"], roundTrip["definition"]));
        Assert.True(JsonNode.DeepEquals(legacy["layout"], roundTrip["layout"]));
        Assert.Equal(parsed.Document!.Workflow, DocumentFixture.Read(roundTrip).Document!.Workflow);

        Upgrade(legacy);
        Assert.True(ExecutionPlanner.Validate(Read(legacy)).Ready);
        var upgraded = JsonSerializer.SerializeToNode(Read(legacy), DocumentJson.Options)!;
        Assert.True(JsonNode.DeepEquals(legacy["definition"], upgraded["definition"]));
        Assert.True(JsonNode.DeepEquals(DocumentFixture.Create()["layout"], upgraded["layout"]));
        Assert.Equal("Summarize this input.", Config(legacy, 1)["prompt"]!.GetValue<string>());
    }

    [Fact]
    public void ReadinessUsesEdgeOrderAndIndependentProfilesRatherThanArrayOrderOrLayout()
    {
        var graph = Executable();
        Config(graph, 1)["providerProfileId"] = "profile-A";
        Config(graph, 2)["providerProfileId"] = "profile-B";
        var nodes = DocumentFixture.Nodes(graph);
        var first = nodes[1]!;
        nodes.RemoveAt(1);
        nodes.Add(first);
        var plan = ExecutionPlanner.Validate(Read(graph));
        Assert.True(plan.Ready);
        Assert.Equal(["start", "model-1", "model-2", "end"], plan.OrderedNodes.Select(node => node.Id));
        Assert.Equal("profile-A", plan.OrderedNodes[1].Configuration.GetProperty("providerProfileId").GetString());
        Assert.Equal("profile-B", plan.OrderedNodes[2].Configuration.GetProperty("providerProfileId").GetString());
    }

    [Fact]
    public void EndBindingIsRequiredForExecutionButIncompleteDraftRemainsSaveable()
    {
        var graph = Executable();
        Config(graph, 3)["resultBinding"] = null;
        Assert.True(DocumentFixture.Read(graph).Success);
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "result_binding_required");
    }

    [Theory]
    [InlineData("start")]
    [InlineData("model-2")]
    [InlineData("model-1")]
    [InlineData("foreign-workflow-node")]
    public void BindingSourcesMustBeEarlierModelCallsInThisWorkflow(string sourceNode)
    {
        var graph = Executable();
        ConfigureBinding(graph, 1, "x", NodeSource("nodeText", sourceNode), "{{inputs.x}}");
        var plan = ExecutionPlanner.Validate(Read(graph));
        Assert.False(plan.Ready);
        Assert.Contains(plan.Issues, issue => issue.Code == "invalid_binding_source" && issue.NodeId == "model-1");
    }

    [Fact]
    public void RenamingIsStableButDeletionAndReconnectionInvalidateDependencies()
    {
        var graph = Executable();
        ConfigureBinding(graph, 2, "x", NodeSource("nodeText", "model-1"), "{{inputs.x}}");
        DocumentFixture.Nodes(graph)[1]!["name"] = "Renamed stable ID";
        Assert.True(ExecutionPlanner.Validate(Read(graph)).Ready);
        DocumentFixture.RemoveNode(graph, "model-1");
        DocumentFixture.AddEdge(graph, "replacement", "start", "model-2");
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "invalid_binding_source");

        graph = Executable();
        ConfigureBinding(graph, 2, "x", NodeSource("nodeText", "model-1"), "{{inputs.x}}");
        DocumentFixture.Edges(graph).Clear();
        DocumentFixture.AddEdge(graph, "a", "start", "model-2");
        DocumentFixture.AddEdge(graph, "b", "model-2", "model-1");
        DocumentFixture.AddEdge(graph, "c", "model-1", "end");
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "invalid_binding_source");
    }

    [Fact]
    public void JsonSourceRequiresExplicitJsonObjectOutputMode()
    {
        var graph = Executable();
        ConfigureBinding(graph, 2, "x", NodeSource("nodeJson", "model-1", "/x"), "{{inputs.x}}");
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "binding_source_not_json");
        Config(graph, 1)["outputMode"] = "jsonObject";
        Assert.True(ExecutionPlanner.Validate(Read(graph)).Ready);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1x")]
    [InlineData("two words")]
    [InlineData("x.y")]
    [InlineData("é")]
    [InlineData("x\n")]
    public void InvalidAliasesAreSaveableDraftsButCannotExecute(string alias)
    {
        var graph = Executable();
        ConfigureBinding(graph, 1, alias, RunSource("/x"), "{{inputs.x}}");
        Assert.True(DocumentFixture.Read(graph).Success);
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "invalid_binding_alias");
    }

    [Fact]
    public void AliasLimitsDuplicatesCaseAndUnusedWarningsAreExplicit()
    {
        var graph = Executable();
        var alias = new string('a', 64);
        ConfigureBinding(graph, 1, alias, RunSource("/x"), "{{inputs." + alias + "}}");
        Assert.True(ExecutionPlanner.Validate(Read(graph)).Ready);
        Config(graph, 1)["inputBindings"]![0]!["alias"] = alias + "a";
        Assert.False(DocumentFixture.Read(graph).Success);

        ConfigureBinding(graph, 1, "x", RunSource("/x"), "{{inputs.X}}");
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "unbound_placeholder");
        Config(graph, 1)["inputBindings"]!.AsArray().Add(Config(graph, 1)["inputBindings"]![0]!.DeepClone());
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "duplicate_binding_alias");

        ConfigureBinding(graph, 1, "x", RunSource("/x"), "A literal sentence in bindings mode.");
        var plan = ExecutionPlanner.Validate(Read(graph));
        Assert.True(plan.Ready);
        Assert.Contains(plan.Issues, issue => issue.Code == "unused_binding" && issue.Severity == "warning");
    }

    [Fact]
    public void LiteralModePreservesEveryBraceAndRejectsConfiguredBindings()
    {
        var graph = Executable();
        const string prompt = "{{inputs.x}} {{other}} {\"x\":1} {{ inputs.x }}";
        Config(graph, 1)["prompt"] = prompt;
        var rendered = BindingResolver.Render(Read(graph).Definition.Nodes[1], Input("{}"), NoOutputs);
        Assert.Equal(prompt, rendered.Prompt);
        Assert.Empty(rendered.Inputs);
        ConfigureBinding(graph, 1, "x", RunSource("/x"), prompt);
        Config(graph, 1)["promptMode"] = "literal";
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "literal_bindings");
        Assert.Equal("literal_bindings", Assert.Throws<ExecutionException>(() => BindingResolver.Render(Read(graph).Definition.Nodes[1], Input("{\"x\":1}"), NoOutputs)).Code);
    }

    [Fact]
    public void TypedSubstitutionPreservesLargeNumbersAndNeverRecursesOrIncludesUnselectedHistory()
    {
        var graph = Executable();
        ConfigureBinding(graph, 1, "selected", RunSource("/selected"), "prefix={{inputs.selected}}; repeated={{inputs.selected}}; {\"example\":1}; {{other}}; {{inputs.selected.path}}");
        var input = Input("""{"selected":{"n":900719925474099312345678901,"decimal":1.2300000000000000001,"bool":true,"null":null,"arr":["{{inputs.secret}}"]},"secret":"DO-NOT-FORWARD"}""");
        var rendered = BindingResolver.Render(Read(graph).Definition.Nodes[1], input, NoOutputs);
        Assert.Contains("900719925474099312345678901", rendered.Prompt);
        Assert.Contains("1.2300000000000000001", rendered.Prompt);
        Assert.Contains("\"bool\":true,\"null\":null", rendered.Prompt);
        Assert.Contains("{{inputs.secret}}", rendered.Prompt);
        Assert.DoesNotContain("DO-NOT-FORWARD", rendered.Prompt);
        Assert.Contains("{{inputs.selected.path}}", rendered.Prompt);
        Assert.Single(rendered.Inputs);
        Assert.Equal(input.GetProperty("selected").GetProperty("n").GetRawText(), rendered.Inputs["selected"].GetProperty("n").GetRawText());
    }

    [Theory]
    [InlineData("\"hello \\n world\"", "hello \n world")]
    [InlineData("42", "42")]
    [InlineData("-12.5e+3", "-12.5e+3")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "null")]
    [InlineData("[ 1, 2 ]", "[1,2]")]
    [InlineData("{ \"a\": 1 }", "{\"a\":1}")]
    public void StringsInsertAsTextAndOtherValuesAsCompactJson(string value, string expected)
    {
        var graph = Executable();
        ConfigureBinding(graph, 1, "x", RunSource("/x"), "{{inputs.x}}");
        Assert.Equal(expected, BindingResolver.Render(Read(graph).Definition.Nodes[1], Input("{\"x\":" + value + "}"), NoOutputs).Prompt);
    }

    [Theory]
    [InlineData("", "Object")]
    [InlineData("/a~1b/~0key/0", "Null")]
    [InlineData("/a~1b/~0key/1", "Number")]
    [InlineData("/", "String")]
    [InlineData("/~01", "String")]
    [InlineData("/__proto__/constructor", "String")]
    [InlineData("/toString", "String")]
    public void PointerSelectsExactKeysEscapesArraysNullAndPrototypeNamesAsData(string pointer, string expectedKind)
    {
        var root = Input("""{"a/b":{"~key":[null,9007199254740993]},"":"empty key","~1":"literal tilde one","__proto__":{"constructor":"data only"},"toString":"own key"}""");
        Assert.Equal(expectedKind, BindingResolver.SelectPointer(root, pointer).ValueKind.ToString());
        Assert.Equal("9007199254740993", BindingResolver.SelectPointer(root, "/a~1b/~0key/1").GetRawText());
        Assert.Equal("literal tilde one", BindingResolver.SelectPointer(root, "/~01").GetString());
        Assert.Equal("binding_path_missing", Assert.Throws<ExecutionException>(() => BindingResolver.SelectPointer(Input("{}"), "/__proto__")).Code);
    }

    [Theory]
    [InlineData("#")]
    [InlineData("#/x")]
    [InlineData("x")]
    [InlineData("/bad~")]
    [InlineData("/bad~2")]
    public void MalformedPointersFailAtReadinessAndResolution(string pointer)
    {
        var graph = Executable();
        ConfigureBinding(graph, 1, "x", RunSource(pointer), "{{inputs.x}}");
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "invalid_json_pointer");
        Assert.Equal("invalid_json_pointer", Assert.Throws<ExecutionException>(() => BindingResolver.SelectPointer(Input("{}"), pointer)).Code);
    }

    [Theory]
    [InlineData("/array/01")]
    [InlineData("/array/-")]
    [InlineData("/array/-1")]
    [InlineData("/array/+1")]
    [InlineData("/array/2")]
    [InlineData("/array/9999999999999999999999999999")]
    [InlineData("/array/１")]
    [InlineData("/array/")]
    [InlineData("/missing")]
    [InlineData("/null/child")]
    [InlineData("/X")]
    public void UnresolvedPathsAndNoncanonicalArrayIndexesFailInsteadOfGuessing(string pointer)
    {
        var root = Input("{\"array\":[null,2],\"null\":null,\"x\":1}");
        Assert.Equal("binding_path_missing", Assert.Throws<ExecutionException>(() => BindingResolver.SelectPointer(root, pointer)).Code);
    }

    [Fact]
    public void PreviousOutputAndEndResolutionUseOnlyProvidedSuccessfulRunContext()
    {
        var json = StrictExecutionJson.ParseObjectOutput("{\"varX\":14,\"ignored\":\"other\"}");
        var outputs = new Dictionary<string, NodeOutput> { ["model-1"] = new("{\"varX\":14,\"ignored\":\"other\"}", json) };
        var graph = Executable();
        ConfigureBinding(graph, 2, "varX", NodeSource("nodeJson", "model-1", "/varX"), "Multiply {{inputs.varX}} by ten.");
        Assert.Equal("Multiply 14 by ten.", BindingResolver.Render(Read(graph).Definition.Nodes[2], Input("{\"x\":7}"), outputs).Prompt);
        Assert.Equal(json.GetRawText(), BindingResolver.Resolve(Element(NodeSource("nodeJson", "model-1", "")), Input("{}"), outputs).GetRawText());
        Assert.Equal(outputs["model-1"].Text, BindingResolver.Resolve(Element(NodeSource("nodeText", "model-1")), Input("{}"), outputs).GetString());
        Assert.Equal("binding_source_unavailable", Assert.Throws<ExecutionException>(() => BindingResolver.Resolve(Element(NodeSource("nodeText", "model-1")), Input("{}"), NoOutputs)).Code);
        outputs["model-1"] = new("text only", null);
        Assert.Equal("binding_source_not_json", Assert.Throws<ExecutionException>(() => BindingResolver.Resolve(Element(NodeSource("nodeJson", "model-1", "")), Input("{}"), outputs)).Code);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    [InlineData("{} {}")]
    [InlineData("```json\n{}\n```")]
    [InlineData("Before {}")]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"a\":[{\"x\":1,\"x\":2}]}")]
    [InlineData("{\"a\":NaN}")]
    [InlineData("{\"a\":Infinity}")]
    [InlineData("{\"a\":01}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{/* comment */\"a\":1}")]
    [InlineData("{\"a\":\"\\uD800\"}")]
    public void StrictObjectOutputRejectsUnsupportedPayloadsWithoutRepair(string text)
    {
        var exception = Assert.Throws<ExecutionException>(() => StrictExecutionJson.ParseObjectOutput(text));
        Assert.Equal("invalid_json_output", exception.Code);
        Assert.DoesNotContain(text, exception.Message);
    }

    [Fact]
    public void StrictObjectParserPreservesOriginalNumberTokensAndBoundsDepth()
    {
        var parsed = StrictExecutionJson.ParseObjectOutput(" \n {\"integer\":9007199254740993123456789,\"precise\":0.12345678901234567890123456789} \n");
        Assert.Equal("9007199254740993123456789", parsed.GetProperty("integer").GetRawText());
        Assert.Equal("0.12345678901234567890123456789", parsed.GetProperty("precise").GetRawText());
        var maxDepth = string.Concat(Enumerable.Repeat("{\"x\":", 32)) + "null" + new string('}', 32);
        Assert.Equal(JsonValueKind.Object, StrictExecutionJson.ParseObjectOutput(maxDepth).ValueKind);
        Assert.Equal("invalid_json_output", Assert.Throws<ExecutionException>(() => StrictExecutionJson.ParseObjectOutput("{\"x\":" + maxDepth + "}")).Code);
    }

    [Fact]
    public void ByteLimitsUseUtf8AndNeverTruncateOrDropLargeBindings()
    {
        var maxInput = "{\"s\":\"" + new string('a', StrictExecutionJson.MaximumInputBytes - 8) + "\"}";
        Assert.Equal(StrictExecutionJson.MaximumInputBytes, Encoding.UTF8.GetByteCount(maxInput));
        Assert.Equal(JsonValueKind.Object, Input(maxInput).ValueKind);
        Assert.Equal("input_too_large", Assert.Throws<ExecutionException>(() => Input(maxInput + " ")).Code);
        Assert.Equal("input_too_large", Assert.Throws<ExecutionException>(() => Input("{\"s\":\"" + new string('é', 9000) + "\"}")).Code);
        Assert.Equal("output_too_large", Assert.Throws<ExecutionException>(() => StrictExecutionJson.ParseObjectOutput("{\"s\":\"" + new string('é', 66000) + "\"}")).Code);
        Assert.Equal("empty_output", Assert.Throws<ExecutionException>(() => StrictExecutionJson.ValidateText(" \n")).Code);

        var graph = Executable();
        ConfigureBinding(graph, 2, "x", NodeSource("nodeText", "model-1"), "{{inputs.x}}{{inputs.x}}");
        var outputs = new Dictionary<string, NodeOutput> { ["model-1"] = new(new string('é', 16384), null) };
        Assert.Equal(65536, Encoding.UTF8.GetByteCount(BindingResolver.Render(Read(graph).Definition.Nodes[2], Input("{}"), outputs).Prompt));
        outputs["model-1"] = new(new string('é', 16385), null);
        Assert.Equal("prompt_too_large", Assert.Throws<ExecutionException>(() => BindingResolver.Render(Read(graph).Definition.Nodes[2], Input("{}"), outputs)).Code);
        Config(graph, 1)["prompt"] = new string('é', 32769);
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "prompt_too_large");
    }

    [Fact]
    public void StrictRunInputRequiresObjectUniqueKeysAndDoesNotExposeParserPayload()
    {
        foreach (var text in new[] { "[]", "null", "{\"sensitive-fixture\":1,\"sensitive-fixture\":2}", "{\"value\":NaN}" })
        {
            var exception = Assert.Throws<ExecutionException>(() => Input(text));
            Assert.Equal("invalid_run_input", exception.Code);
            Assert.DoesNotContain("sensitive-fixture", exception.Message);
        }
    }

    [Fact]
    public void NodeVersionAndNestedSourceShapesRemainStrict()
    {
        var graph = Executable();
        Config(graph, 1)["tools"] = new JsonArray();
        Assert.Contains(DocumentFixture.Read(graph).Issues, issue => issue.Path.EndsWith(".tools", StringComparison.Ordinal));
        graph = Executable();
        ConfigureBinding(graph, 1, "x", RunSource("/x"), "{{inputs.x}}");
        Config(graph, 1)["inputBindings"]![0]!["source"]!["nodeId"] = "model-1";
        Assert.Contains(DocumentFixture.Read(graph).Issues, issue => issue.Path.EndsWith(".source.nodeId", StringComparison.Ordinal));
        graph = DocumentFixture.Create();
        Config(graph, 1)["promptMode"] = "literal";
        Assert.Contains(DocumentFixture.Read(graph).Issues, issue => issue.Path.EndsWith(".promptMode", StringComparison.Ordinal));
        graph = Executable();
        DocumentFixture.Nodes(graph)[0]!["typeVersion"] = 2;
        Assert.Contains(DocumentFixture.Read(graph).Issues, issue => issue.Path == "definition.nodes[0].typeVersion");
        graph = Executable();
        Config(graph, 1)["promptMode"] = "javascript";
        Assert.Contains(DocumentFixture.Read(graph).Issues, issue => issue.Code == "invalid_choice");
        graph = Executable();
        Config(graph, 3)["resultBinding"] = new JsonObject { ["kind"] = "nodeText", ["nodeId"] = "model-2", ["pointer"] = "" };
        Assert.Contains(DocumentFixture.Read(graph).Issues, issue => issue.Code == "unknown_property");
    }

    [Fact]
    public void BindingAndModelCountLimitsAreEnforced()
    {
        var graph = Executable();
        var bindings = Config(graph, 1)["inputBindings"]!.AsArray();
        for (var i = 0; i < 33; i++) bindings.Add(new JsonObject { ["alias"] = "x" + i, ["source"] = RunSource("") });
        Assert.Contains(DocumentFixture.Read(graph).Issues, issue => issue.Code == "too_many_items");

        graph = Executable();
        var nodes = DocumentFixture.Nodes(graph);
        DocumentFixture.Edges(graph).Clear();
        var previous = "model-2";
        DocumentFixture.AddEdge(graph, "a", "start", "model-1");
        DocumentFixture.AddEdge(graph, "b", "model-1", "model-2");
        for (var i = 3; i <= 17; i++)
        {
            var node = nodes[1]!.DeepClone();
            node["id"] = "model-" + i;
            nodes.Add(node);
            graph["layout"]!["nodes"]!.AsArray().Add(new JsonObject { ["nodeId"] = "model-" + i, ["x"] = i, ["y"] = 0 });
            DocumentFixture.AddEdge(graph, "edge-" + i, previous, "model-" + i);
            previous = "model-" + i;
        }
        DocumentFixture.AddEdge(graph, "last", previous, "end");
        Assert.Contains(ExecutionPlanner.Validate(Read(graph)).Issues, issue => issue.Code == "too_many_model_calls");
    }

    private static JsonObject Executable()
    {
        var graph = DocumentFixture.Create();
        Upgrade(graph);
        return graph;
    }

    private static void Upgrade(JsonObject graph)
    {
        foreach (var node in DocumentFixture.Nodes(graph))
        {
            var type = node!["type"]!.GetValue<string>();
            if (type == "start") continue;
            node["typeVersion"] = 2;
            var config = node["configuration"]!.AsObject();
            if (type == "modelCall")
            {
                config["promptMode"] = "literal";
                config["inputBindings"] = new JsonArray();
                config["outputMode"] = "text";
                if (config["providerProfileId"] is null) config["providerProfileId"] = "profile-one";
            }
            else config["resultBinding"] = NodeSource("nodeText", "model-2");
        }
    }

    private static JsonObject Config(JsonObject graph, int index) => DocumentFixture.Nodes(graph)[index]!["configuration"]!.AsObject();
    private static void ConfigureBinding(JsonObject graph, int index, string alias, JsonObject source, string prompt)
    {
        var config = Config(graph, index);
        config["promptMode"] = "bindings";
        config["prompt"] = prompt;
        config["inputBindings"] = new JsonArray(new JsonObject { ["alias"] = alias, ["source"] = source });
    }
    private static JsonObject RunSource(string pointer) => new() { ["kind"] = "runInput", ["pointer"] = pointer };
    private static JsonObject NodeSource(string kind, string id, string? pointer = null)
    {
        var source = new JsonObject { ["kind"] = kind, ["nodeId"] = id };
        if (pointer is not null) source["pointer"] = pointer;
        return source;
    }
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static JsonElement Input(string text) => StrictExecutionJson.ParseInput(Encoding.UTF8.GetBytes(text));
    private static WorkflowDocument Read(JsonObject graph)
    {
        var parsed = DocumentFixture.Read(graph);
        Assert.True(parsed.Success, string.Join("; ", parsed.Issues.Select(issue => issue.Path + ": " + issue.Message)));
        return parsed.Document!;
    }
}
