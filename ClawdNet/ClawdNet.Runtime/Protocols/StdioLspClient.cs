using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class StdioLspClient : ILspClient
{
    private readonly LspConfigurationLoader _configurationLoader;
    private readonly Dictionary<string, LspServerSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LspServerState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _extensionToServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public StdioLspClient(string dataRoot)
    {
        _configurationLoader = new LspConfigurationLoader(dataRoot);
    }

    public IReadOnlyCollection<LspServerState> Servers => _states.Values.OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ToArray();

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
                    _states[definition.Name] = new LspServerState(definition.Name, false, false, definition.FileExtensions);
                    continue;
                }

                try
                {
                    var session = await LspServerSession.StartAsync(definition, cancellationToken);
                    _sessions[definition.Name] = session;
                    _states[definition.Name] = new LspServerState(definition.Name, true, true, definition.FileExtensions);
                    foreach (var extension in definition.FileExtensions)
                    {
                        _extensionToServer[extension] = definition.Name;
                    }
                }
                catch (Exception ex)
                {
                    _states[definition.Name] = new LspServerState(definition.Name, true, false, definition.FileExtensions, ex.Message);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<LspServerState?> PingAsync(string serverName, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!_sessions.TryGetValue(serverName, out var session))
        {
            return _states.TryGetValue(serverName, out var state) ? state : null;
        }

        try
        {
            await session.SendRequestAsync("workspace/symbol", new JsonObject { ["query"] = "__clawdnet_ping__" }, cancellationToken);
            var state = new LspServerState(session.Definition.Name, true, true, session.Definition.FileExtensions);
            _states[serverName] = state;
            return state;
        }
        catch (Exception ex)
        {
            var state = new LspServerState(session.Definition.Name, true, false, session.Definition.FileExtensions, ex.Message);
            _states[serverName] = state;
            return state;
        }
    }

    public async Task SyncFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!TryResolveSession(path, out var session))
        {
            return;
        }

        await session.SyncFileAsync(path, content, cancellationToken);
    }

    public async Task<IReadOnlyList<LspLocation>> GetDefinitionsAsync(string path, int line, int character, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!TryResolveSession(path, out var session))
        {
            return [];
        }

        return await session.GetLocationsAsync("textDocument/definition", path, line, character, cancellationToken);
    }

    public async Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string path, int line, int character, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!TryResolveSession(path, out var session))
        {
            return [];
        }

        return await session.GetLocationsAsync(
            "textDocument/references",
            path,
            line,
            character,
            cancellationToken,
            new JsonObject
            {
                ["context"] = new JsonObject
                {
                    ["includeDeclaration"] = true
                }
            });
    }

    public async Task<string?> GetHoverAsync(string path, int line, int character, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!TryResolveSession(path, out var session))
        {
            return null;
        }

        return await session.GetHoverAsync(path, line, character, cancellationToken);
    }

    public async Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string path, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!TryResolveSession(path, out var session))
        {
            return [];
        }

        return session.GetDiagnostics(path);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
    }

    private bool TryResolveSession(string path, out LspServerSession session)
    {
        var extension = Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension) &&
            _extensionToServer.TryGetValue(extension, out var serverName) &&
            _sessions.TryGetValue(serverName, out session!))
        {
            return true;
        }

        session = null!;
        return false;
    }

    private sealed class LspServerSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Stream _inputStream;
        private readonly Stream _outputStream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> _pending = new();
        private readonly ConcurrentDictionary<string, IReadOnlyList<LspDiagnostic>> _diagnostics = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _documentVersions = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _readerCts = new();
        private readonly Task _readerTask;
        private int _requestId;

        private LspServerSession(LspServerDefinition definition, Process process, Stream inputStream, Stream outputStream)
        {
            Definition = definition;
            _process = process;
            _inputStream = inputStream;
            _outputStream = outputStream;
            _readerTask = Task.Run(() => ReaderLoopAsync(_readerCts.Token), CancellationToken.None);
        }

        public LspServerDefinition Definition { get; }

        public static async Task<LspServerSession> StartAsync(LspServerDefinition definition, CancellationToken cancellationToken)
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
                throw new InvalidOperationException($"Failed to start LSP server '{definition.Name}'.");
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

            var session = new LspServerSession(definition, process, process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
            await session.InitializeProtocolAsync(cancellationToken);
            return session;
        }

        public async Task SyncFileAsync(string path, string content, CancellationToken cancellationToken)
        {
            var normalizedPath = NormalizePath(path);
            var uri = ToFileUri(normalizedPath);
            var version = _documentVersions.TryGetValue(normalizedPath, out var currentVersion)
                ? currentVersion + 1
                : 1;
            _documentVersions[normalizedPath] = version;

            if (version == 1)
            {
                await SendNotificationAsync(
                    "textDocument/didOpen",
                    new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["uri"] = uri,
                            ["languageId"] = InferLanguageId(normalizedPath, Definition.LanguageId),
                            ["version"] = version,
                            ["text"] = content
                        }
                    },
                    cancellationToken);
            }
            else
            {
                await SendNotificationAsync(
                    "textDocument/didChange",
                    new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["uri"] = uri,
                            ["version"] = version
                        },
                        ["contentChanges"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["text"] = content
                            }
                        }
                    },
                    cancellationToken);
            }

            await SendNotificationAsync(
                "textDocument/didSave",
                new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = uri
                    },
                    ["text"] = content
                },
                cancellationToken);

            await Task.Delay(30, cancellationToken);
        }

        public async Task<IReadOnlyList<LspLocation>> GetLocationsAsync(
            string method,
            string path,
            int line,
            int character,
            CancellationToken cancellationToken,
            JsonObject? extraParams = null)
        {
            var parameters = BuildTextDocumentPositionParams(path, line, character);
            if (extraParams is not null)
            {
                foreach (var kvp in extraParams)
                {
                    parameters[kvp.Key] = kvp.Value?.DeepClone();
                }
            }

            var result = await SendRequestAsync(method, parameters, cancellationToken);
            return ParseLocations(result);
        }

        public async Task<string?> GetHoverAsync(string path, int line, int character, CancellationToken cancellationToken)
        {
            var result = await SendRequestAsync(
                "textDocument/hover",
                BuildTextDocumentPositionParams(path, line, character),
                cancellationToken);

            var contents = (result as JsonObject)?["contents"];
            return contents switch
            {
                JsonValue value => value.GetValue<string>(),
                JsonObject obj when obj["value"] is JsonValue markdown => markdown.GetValue<string>(),
                JsonArray array => string.Join(Environment.NewLine, array.Select(item => item?["value"]?.GetValue<string>() ?? item?.ToJsonString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item))),
                _ => null
            };
        }

        public IReadOnlyList<LspDiagnostic> GetDiagnostics(string path)
        {
            return _diagnostics.TryGetValue(NormalizePath(path), out var diagnostics)
                ? diagnostics
                : [];
        }

        public async Task<JsonNode> SendRequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
        {
            var requestId = Interlocked.Increment(ref _requestId);
            var completion = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[requestId] = completion;

            var payload = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId,
                ["method"] = method,
                ["params"] = parameters
            };

            await WriteMessageAsync(payload, cancellationToken);
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

            return await completion.Task;
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

                    try
                    {
                        await SendNotificationAsync("exit", new JsonObject(), CancellationToken.None);
                    }
                    catch
                    {
                    }

                    if (!_process.WaitForExit(1000))
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _readerCts.Cancel();
                try
                {
                    await _readerTask;
                }
                catch
                {
                }

                _process.Dispose();
                _readerCts.Dispose();
                _writeLock.Dispose();
            }
        }

        private async Task InitializeProtocolAsync(CancellationToken cancellationToken)
        {
            await SendRequestAsync(
                "initialize",
                new JsonObject
                {
                    ["processId"] = Environment.ProcessId,
                    ["rootUri"] = ToFileUri(Environment.CurrentDirectory),
                    ["capabilities"] = new JsonObject()
                },
                cancellationToken);

            await SendNotificationAsync("initialized", new JsonObject(), cancellationToken);
        }

        private async Task ReaderLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await ReadMessageAsync(cancellationToken);
                    if (message["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var requestId))
                    {
                        if (_pending.TryRemove(requestId, out var completion))
                        {
                            if (message["error"] is JsonObject errorObject)
                            {
                                completion.TrySetException(new InvalidOperationException(errorObject["message"]?.GetValue<string>() ?? "LSP request failed."));
                            }
                            else
                            {
                                completion.TrySetResult(message["result"]?.DeepClone() ?? new JsonObject());
                            }
                        }

                        continue;
                    }

                    if (string.Equals(message["method"]?.GetValue<string>(), "textDocument/publishDiagnostics", StringComparison.Ordinal))
                    {
                        HandleDiagnostics(message["params"] as JsonObject);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                foreach (var completion in _pending.Values)
                {
                    completion.TrySetException(ex);
                }
            }
        }

        private void HandleDiagnostics(JsonObject? parameters)
        {
            var uri = parameters?["uri"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(uri))
            {
                return;
            }

            var path = FromFileUri(uri);
            var diagnostics = new List<LspDiagnostic>();
            foreach (var diagnosticNode in parameters?["diagnostics"]?.AsArray() ?? [])
            {
                if (diagnosticNode is not JsonObject diagnosticObject)
                {
                    continue;
                }

                var range = diagnosticObject["range"] as JsonObject;
                var start = range?["start"] as JsonObject;
                diagnostics.Add(new LspDiagnostic(
                    path,
                    start?["line"]?.GetValue<int>() ?? 0,
                    start?["character"]?.GetValue<int>() ?? 0,
                    MapSeverity(diagnosticObject["severity"]?.GetValue<int>()),
                    diagnosticObject["message"]?.GetValue<string>() ?? string.Empty));
            }

            _diagnostics[path] = diagnostics;
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
            var bytes = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _inputStream.WriteAsync(header, cancellationToken);
                await _inputStream.WriteAsync(bytes, cancellationToken);
                await _inputStream.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task<JsonObject> ReadMessageAsync(CancellationToken cancellationToken)
        {
            var headerBuilder = new StringBuilder();
            var buffer = new byte[1];

            while (true)
            {
                var read = await _outputStream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    throw new InvalidOperationException($"LSP server '{Definition.Name}' closed the connection.");
                }

                headerBuilder.Append((char)buffer[0]);
                if (headerBuilder.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            var contentLength = ParseContentLength(headerBuilder.ToString());
            var payloadBytes = new byte[contentLength];
            var offset = 0;
            while (offset < contentLength)
            {
                var read = await _outputStream.ReadAsync(payloadBytes.AsMemory(offset, contentLength - offset), cancellationToken);
                if (read == 0)
                {
                    throw new InvalidOperationException($"LSP server '{Definition.Name}' closed the connection.");
                }

                offset += read;
            }

            return JsonNode.Parse(payloadBytes) as JsonObject
                   ?? throw new InvalidOperationException("Invalid LSP response payload.");
        }

        private static JsonObject BuildTextDocumentPositionParams(string path, int line, int character)
        {
            return new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = ToFileUri(path)
                },
                ["position"] = new JsonObject
                {
                    ["line"] = line,
                    ["character"] = character
                }
            };
        }

        private static IReadOnlyList<LspLocation> ParseLocations(JsonNode result)
        {
            JsonArray nodes = result switch
            {
                JsonObject obj when obj["uri"] is not null => new JsonArray(obj.DeepClone()),
                JsonObject obj => obj["locations"] as JsonArray ?? obj["result"] as JsonArray ?? new JsonArray(),
                JsonArray array => array,
                _ => new JsonArray()
            };

            return nodes
                .Select(node => node as JsonObject)
                .Where(node => node is not null)
                .Select(node =>
                {
                    var range = node!["range"] as JsonObject;
                    var start = range?["start"] as JsonObject;
                    return new LspLocation(
                        FromFileUri(node["uri"]?.GetValue<string>() ?? string.Empty),
                        start?["line"]?.GetValue<int>() ?? 0,
                        start?["character"]?.GetValue<int>() ?? 0);
                })
                .ToArray();
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

            throw new InvalidOperationException("Missing Content-Length header in LSP response.");
        }

        private static string JoinArguments(IReadOnlyList<string> arguments)
        {
            return string.Join(" ", arguments.Select(argument =>
                argument.Contains(' ', StringComparison.Ordinal)
                    ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                    : argument));
        }

        private static string InferLanguageId(string path, string? configuredLanguageId)
        {
            if (!string.IsNullOrWhiteSpace(configuredLanguageId))
            {
                return configuredLanguageId;
            }

            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".cs" => "csharp",
                ".ts" => "typescript",
                ".tsx" => "typescriptreact",
                ".js" => "javascript",
                ".jsx" => "javascriptreact",
                ".py" => "python",
                ".json" => "json",
                _ => "plaintext"
            };
        }

        private static string NormalizePath(string path) => Path.GetFullPath(path);

        private static string ToFileUri(string path) => new Uri(NormalizePath(path)).AbsoluteUri;

        private static string FromFileUri(string uri) => string.IsNullOrWhiteSpace(uri) ? string.Empty : new Uri(uri).LocalPath;

        private static string MapSeverity(int? severity)
        {
            return severity switch
            {
                1 => "Error",
                2 => "Warning",
                3 => "Information",
                4 => "Hint",
                _ => "Unknown"
            };
        }
    }
}
