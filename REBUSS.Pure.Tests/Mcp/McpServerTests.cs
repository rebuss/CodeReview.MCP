using NSubstitute;
using REBUSS.Pure.Mcp;
using REBUSS.Pure.Mcp.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace REBUSS.Pure.Tests.Mcp
{
    public class McpServerTests
    {
        private readonly ILogger<McpServer> _logger = Substitute.For<ILogger<McpServer>>();
        private readonly IMcpToolHandler _toolHandler = Substitute.For<IMcpToolHandler>();

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public McpServerTests()
        {
            _toolHandler.ToolName.Returns("get_pr_diff");
            _toolHandler.GetToolDefinition().Returns(new McpTool
            {
                Name = "get_pr_diff",
                Description = "Test tool",
                InputSchema = new ToolInputSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolProperty>
                    {
                        ["prNumber"] = new ToolProperty { Type = "integer", Description = "PR number" }
                    },
                    Required = new List<string> { "prNumber" }
                }
            });
        }

        // --- helpers ---------------------------------------------------------------

        private async Task<List<JsonDocument>> SendRequestsAsync(params string[] requests)
        {
            var inputText = string.Join("\n", requests) + "\n";
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(inputText));
            using var output = new MemoryStream();

            var server = new McpServer(_logger, new[] { _toolHandler }, input, output);
            await server.RunAsync(CancellationToken.None);

            output.Position = 0;
            var lines = new StreamReader(output, Encoding.UTF8)
                .ReadToEnd()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            return lines.Select(l => JsonDocument.Parse(l)).ToList();
        }

        private async Task<JsonDocument> SendSingleRequestAsync(string request)
        {
            var docs = await SendRequestsAsync(request);
            Assert.Single(docs);
            return docs[0];
        }

        // --- initialize ------------------------------------------------------------

        [Fact]
        public async Task Initialize_ReturnsProtocolVersionAndServerInfo()
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "1",
                method = "initialize",
                @params = new { }
            });

            var doc = await SendSingleRequestAsync(request);
            var root = doc.RootElement;

            Assert.Equal("1", root.GetProperty("id").GetString());
            Assert.False(root.TryGetProperty("error", out _));

            var result = root.GetProperty("result");
            Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
            Assert.Equal("REBUSS.Pure", result.GetProperty("serverInfo").GetProperty("name").GetString());
            Assert.Equal("1.0.0", result.GetProperty("serverInfo").GetProperty("version").GetString());
        }

        // --- tools/list ------------------------------------------------------------

        [Fact]
        public async Task ToolsList_ReturnsRegisteredTools()
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "2",
                method = "tools/list"
            });

            var doc = await SendSingleRequestAsync(request);
            var tools = doc.RootElement.GetProperty("result").GetProperty("tools");

            Assert.Equal(1, tools.GetArrayLength());
            Assert.Equal("get_pr_diff", tools[0].GetProperty("name").GetString());
        }

        // --- tools/call (valid) ---------------------------------------------------

        [Fact]
        public async Task ToolsCall_DelegatesToRegisteredHandler()
        {
            var expectedResult = new ToolResult
            {
                Content = new List<ContentItem> { new() { Type = "text", Text = "ok" } },
                IsError = false
            };

            _toolHandler
                .ExecuteAsync(Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
                .Returns(expectedResult);

            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "3",
                method = "tools/call",
                @params = new { name = "get_pr_diff", arguments = new { prNumber = 42 } }
            });

            var doc = await SendSingleRequestAsync(request);
            var result = doc.RootElement.GetProperty("result");

            Assert.False(result.GetProperty("isError").GetBoolean());
            Assert.Equal("ok", result.GetProperty("content")[0].GetProperty("text").GetString());

            await _toolHandler.Received(1)
                .ExecuteAsync(Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
        }

        // --- tools/call (unknown tool) -------------------------------------------

        [Fact]
        public async Task ToolsCall_UnknownTool_ReturnsError()
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "4",
                method = "tools/call",
                @params = new { name = "nonexistent_tool", arguments = new { } }
            });

            var doc = await SendSingleRequestAsync(request);
            var error = doc.RootElement.GetProperty("error");

            Assert.Equal(-32603, error.GetProperty("code").GetInt32());
            Assert.Contains("Unknown tool", error.GetProperty("data").GetString());
        }

        // --- unknown method -------------------------------------------------------

        [Fact]
        public async Task UnknownMethod_ReturnsMethodNotFoundError()
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "5",
                method = "unknown/method"
            });

            var doc = await SendSingleRequestAsync(request);
            var error = doc.RootElement.GetProperty("error");

            Assert.Equal(-32601, error.GetProperty("code").GetInt32());
            Assert.Equal("Method not found", error.GetProperty("message").GetString());
        }

        // --- invalid JSON ---------------------------------------------------------

        [Fact]
        public async Task InvalidJson_ReturnsParseError()
        {
            var doc = await SendSingleRequestAsync("not valid json {{{");
            var error = doc.RootElement.GetProperty("error");

            Assert.Equal(-32700, error.GetProperty("code").GetInt32());
            Assert.Equal("Parse error", error.GetProperty("message").GetString());
        }

        // --- empty lines are skipped ----------------------------------------------

        [Fact]
        public async Task EmptyLines_AreSkipped()
        {
            var validRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "6",
                method = "initialize",
                @params = new { }
            });

            // empty line first, then valid request
            var docs = await SendRequestsAsync("", "  ", validRequest);

            Assert.Single(docs);
            Assert.Equal("6", docs[0].RootElement.GetProperty("id").GetString());
        }

        // --- multiple requests processed sequentially -----------------------------

        [Fact]
        public async Task MultipleRequests_ProcessedSequentially()
        {
            var req1 = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "a",
                method = "initialize",
                @params = new { }
            });
            var req2 = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "b",
                method = "tools/list"
            });

            var docs = await SendRequestsAsync(req1, req2);

            Assert.Equal(2, docs.Count);
            Assert.Equal("a", docs[0].RootElement.GetProperty("id").GetString());
            Assert.Equal("b", docs[1].RootElement.GetProperty("id").GetString());
        }

        // --- tools/call missing params -------------------------------------------

        [Fact]
        public async Task ToolsCall_MissingParams_ReturnsError()
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "7",
                method = "tools/call"
            });

            var doc = await SendSingleRequestAsync(request);
            var error = doc.RootElement.GetProperty("error");

            Assert.Equal(-32603, error.GetProperty("code").GetInt32());
        }

        // --- no registered tools --------------------------------------------------

        [Fact]
        public async Task ToolsList_NoToolsRegistered_ReturnsEmptyList()
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = "8", method = "tools/list" }) + "\n"));
            using var output = new MemoryStream();

            var server = new McpServer(_logger, Enumerable.Empty<IMcpToolHandler>(), input, output);
            await server.RunAsync(CancellationToken.None);

            output.Position = 0;
            var doc = JsonDocument.Parse(new StreamReader(output).ReadToEnd().Trim());
            var tools = doc.RootElement.GetProperty("result").GetProperty("tools");

            Assert.Equal(0, tools.GetArrayLength());
        }

        // --- notifications (no id) -----------------------------------------------

        [Fact]
        public async Task Notification_WithoutId_IsSilentlyIgnored()
        {
            // notifications/initialized has no id — should produce no response
            var notification = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });

            var docs = await SendRequestsAsync(notification);

            Assert.Empty(docs);
        }

        [Fact]
        public async Task Notification_DoesNotAffectSubsequentRequests()
        {
            var notification = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "after-notif",
                method = "tools/list"
            });

            var docs = await SendRequestsAsync(notification, request);

            Assert.Single(docs);
            Assert.Equal("after-notif", docs[0].RootElement.GetProperty("id").GetString());
            Assert.False(docs[0].RootElement.TryGetProperty("error", out _));
        }

        [Fact]
        public async Task UnknownMethod_WithId_StillReturnsError()
        {
            // Requests WITH an id should still return method-not-found error
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "req-with-id",
                method = "notifications/initialized"
            });

            var doc = await SendSingleRequestAsync(request);
            var error = doc.RootElement.GetProperty("error");

            Assert.Equal(-32601, error.GetProperty("code").GetInt32());
        }
    }
}
