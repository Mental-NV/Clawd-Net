using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class StdioMcpClient : IMcpClient
{
    private const string ProtocolVersion = "2024-11-05";

    private readonly McpConfigurationLoader _configurationLoader;
    private readonly Dictionary<string, McpServerSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpServerState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public StdioMcpClient(string dataRoot)
    {
        _configurationLoader = new McpConfigurationLoader(dataRoot);
    }

    public IReadOnlyCollection<McpServerState> Servers => _states.Values.OrderBy(state => state.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var configuration = await _configurationLoader.LoadAsync(cancellationToken);
            foreach (var definition in configuration.Servers)
            {
                if (!definition.Enabled)
                {
                    _states[definition.Name] = new McpServerState(definition.Name, false, false, 0);
                    continue;
                }

                try
                {
                    var session = await McpServerSession.StartAsync(definition, cancellationToken);
                    var tools = await session.ListToolsAsync(cancellationToken);
                    session.SetTools(tools);
                    _sessions[definition.Name] = session;
                    _states[definition.Name] = new McpServerState(definition.Name, true, true, tools.Count);
                }
                catch (Exception ex)
                {
                    _states[definition.Name] = new McpServerState(definition.Name, true, false, 0, ex.Message);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<McpServerState?> PingAsync(string serverName, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!_sessions.TryGetValue(serverName, out var session))
        {
            return _states.TryGetValue(serverName, out var state) ? state : null;
        }

        try
        {
            var tools = await session.ListToolsAsync(cancellationToken);
            session.SetTools(tools);
            var state = new McpServerState(serverName, true, true, tools.Count);
            _states[serverName] = state;
            return state;
        }
        catch (Exception ex)
        {
            var state = new McpServerState(serverName, true, false, 0, ex.Message);
            _states[serverName] = state;
            return state;
        }
    }

    public async Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(string? serverName, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(serverName))
        {
            return _sessions.Values
                .SelectMany(session => session.Tools)
                .OrderBy(tool => tool.ServerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (_sessions.TryGetValue(serverName, out var session))
        {
            return session.Tools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return [];
    }

    public async Task<ToolExecutionResult> InvokeToolAsync(string serverName, string toolName, JsonNode? input, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!_sessions.TryGetValue(serverName, out var session))
        {
            return new ToolExecutionResult(false, string.Empty, $"MCP server '{serverName}' is not connected.");
        }

        try
        {
            var result = await session.CallToolAsync(toolName, input, cancellationToken);
            return new ToolExecutionResult(true, result, null);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, $"MCP tool '{serverName}.{toolName}' failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
    }

    private sealed class McpServerSession : IAsyncDisposable
    {
        private readonly McpServerDefinition _definition;
        private readonly Process _process;
        private readonly Stream _inputStream;
        private readonly Stream _outputStream;
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private int _requestId;

        private McpServerSession(
            McpServerDefinition definition,
            Process process,
            Stream inputStream,
            Stream outputStream)
        {
            _definition = definition;
            _process = process;
            _inputStream = inputStream;
            _outputStream = outputStream;
        }

        public IReadOnlyList<McpToolDefinition> Tools { get; private set; } = [];

        public static async Task<McpServerSession> StartAsync(McpServerDefinition definition, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = definition.Command,
                Arguments = JoinArguments(definition.Arguments),
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var entry in definition.Environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start MCP server '{definition.Name}'.");
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        await process.StandardError.ReadLineAsync();
                    }
                }
                catch
                {
                }
            }, CancellationToken.None);

            var session = new McpServerSession(
                definition,
                process,
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream);
            await session.InitializeProtocolAsync(cancellationToken);
            return session;
        }

        public void SetTools(IReadOnlyList<McpToolDefinition> tools)
        {
            Tools = tools;
        }

        public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken)
        {
            var result = await SendRequestAsync("tools/list", new JsonObject(), cancellationToken);
            var tools = new List<McpToolDefinition>();
            foreach (var toolNode in result["tools"]?.AsArray() ?? [])
            {
                if (toolNode is not JsonObject toolObject)
                {
                    continue;
                }

                tools.Add(new McpToolDefinition(
                    _definition.Name,
                    toolObject["name"]?.GetValue<string>() ?? string.Empty,
                    toolObject["description"]?.GetValue<string>() ?? string.Empty,
                    toolObject["inputSchema"] as JsonObject ?? toolObject["input_schema"] as JsonObject ?? new JsonObject(),
                    _definition.ToolsReadOnly));
            }

            return tools;
        }

        public async Task<string> CallToolAsync(string toolName, JsonNode? input, CancellationToken cancellationToken)
        {
            var result = await SendRequestAsync(
                "tools/call",
                new JsonObject
                {
                    ["name"] = toolName,
                    ["arguments"] = input?.DeepClone() ?? new JsonObject()
                },
                cancellationToken);

            var content = result["content"] as JsonArray;
            if (content is null || content.Count == 0)
            {
                return result.ToJsonString();
            }

            var parts = new List<string>();
            foreach (var item in content)
            {
                if (item is JsonObject objectItem && objectItem["text"] is JsonValue textNode)
                {
                    parts.Add(textNode.GetValue<string>());
                }
                else
                {
                    parts.Add(item?.ToJsonString() ?? string.Empty);
                }
            }

            return string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    try
                    {
                        await SendRequestAsync("shutdown", new JsonObject(), CancellationToken.None);
                    }
                    catch
                    {
                    }

                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                _process.Dispose();
            }
        }

        private async Task InitializeProtocolAsync(CancellationToken cancellationToken)
        {
            await SendRequestAsync(
                "initialize",
                new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = "ClawdNet",
                        ["version"] = "0.1.0"
                    }
                },
                cancellationToken);

            await SendNotificationAsync("notifications/initialized", new JsonObject(), cancellationToken);
        }

        private async Task<JsonObject> SendRequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
        {
            await _requestLock.WaitAsync(cancellationToken);
            try
            {
                var requestId = Interlocked.Increment(ref _requestId);
                var payload = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = requestId,
                    ["method"] = method,
                    ["params"] = parameters
                };

                await WriteMessageAsync(payload, cancellationToken);

                while (true)
                {
                    var response = await ReadMessageAsync(cancellationToken);
                    if (response["id"]?.GetValue<int>() != requestId)
                    {
                        continue;
                    }

                    if (response["error"] is JsonObject errorObject)
                    {
                        var message = errorObject["message"]?.GetValue<string>() ?? $"MCP request '{method}' failed.";
                        throw new InvalidOperationException(message);
                    }

                    return response["result"] as JsonObject ?? new JsonObject();
                }
            }
            finally
            {
                _requestLock.Release();
            }
        }

        private async Task SendNotificationAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
        {
            var payload = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };

            await WriteMessageAsync(payload, cancellationToken);
        }

        private async Task WriteMessageAsync(JsonObject payload, CancellationToken cancellationToken)
        {
            var json = payload.ToJsonString();
            var contentBytes = Encoding.UTF8.GetBytes(json);
            var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {contentBytes.Length}\r\n\r\n");
            await _inputStream.WriteAsync(headerBytes, cancellationToken);
            await _inputStream.WriteAsync(contentBytes, cancellationToken);
            await _inputStream.FlushAsync(cancellationToken);
        }

        private async Task<JsonObject> ReadMessageAsync(CancellationToken cancellationToken)
        {
            var headerBuilder = new StringBuilder();
            var oneByte = new byte[1];

            while (true)
            {
                var read = await _outputStream.ReadAsync(oneByte, cancellationToken);
                if (read == 0)
                {
                    throw new InvalidOperationException($"MCP server '{_definition.Name}' closed the connection.");
                }

                headerBuilder.Append((char)oneByte[0]);
                if (headerBuilder.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            var contentLength = ParseContentLength(headerBuilder.ToString());
            var contentBytes = new byte[contentLength];
            var offset = 0;
            while (offset < contentLength)
            {
                var read = await _outputStream.ReadAsync(contentBytes.AsMemory(offset, contentLength - offset), cancellationToken);
                if (read == 0)
                {
                    throw new InvalidOperationException($"MCP server '{_definition.Name}' closed the connection.");
                }

                offset += read;
            }

            return JsonNode.Parse(contentBytes) as JsonObject
                   ?? throw new InvalidOperationException("Invalid MCP response payload.");
        }

        private static int ParseContentLength(string headers)
        {
            foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line["Content-Length:".Length..].Trim(), out var length))
                {
                    return length;
                }
            }

            throw new InvalidOperationException("Missing Content-Length header in MCP response.");
        }

        private static string JoinArguments(IReadOnlyList<string> arguments)
        {
            return string.Join(" ", arguments.Select(argument =>
                argument.Contains(' ', StringComparison.Ordinal)
                    ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                    : argument));
        }
    }
}
