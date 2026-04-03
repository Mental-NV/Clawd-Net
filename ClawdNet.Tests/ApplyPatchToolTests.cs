using System.Text.Json.Nodes;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Editing;
using ClawdNet.Runtime.Tools;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class ApplyPatchToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clawdnet-apply-patch", Guid.NewGuid().ToString("N"));

    public ApplyPatchToolTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Apply_patch_tool_previews_and_applies_single_file_patch()
    {
        var path = Path.Combine(_root, "note.txt");
        await File.WriteAllTextAsync(path, "hello");
        var lspClient = new FakeLspClient();
        var tool = new ApplyPatchTool(new EditPreviewService(), new EditApplier(lspClient));
        var request = new ToolExecutionRequest(
            "apply_patch",
            new JsonObject
            {
                ["edits"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["path"] = path,
                        ["operation"] = "patch",
                        ["hunks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["oldText"] = "hello",
                                ["newText"] = "hi"
                            }
                        }
                    }
                }
            });

        var preview = await tool.PreviewAsync(request, CancellationToken.None);
        var result = await tool.ApplyAsync(request, CancellationToken.None);

        Assert.True(preview.Success);
        Assert.Contains("---", preview.Diff);
        Assert.Contains("+++ ", preview.Diff);
        Assert.True(result.Success);
        Assert.Equal("hi", await File.ReadAllTextAsync(path));
        Assert.Single(lspClient.SyncRequests);
    }

    [Fact]
    public async Task Apply_patch_tool_handles_create_and_delete_in_one_batch()
    {
        var deletePath = Path.Combine(_root, "delete.txt");
        var createPath = Path.Combine(_root, "create.txt");
        await File.WriteAllTextAsync(deletePath, "remove me");
        var tool = new ApplyPatchTool(new EditPreviewService(), new EditApplier(new FakeLspClient()));
        var request = new ToolExecutionRequest(
            "apply_patch",
            new JsonObject
            {
                ["edits"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["path"] = createPath,
                        ["operation"] = "create",
                        ["content"] = "new file"
                    },
                    new JsonObject
                    {
                        ["path"] = deletePath,
                        ["operation"] = "delete"
                    }
                }
            });

        var result = await tool.ApplyAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(createPath));
        Assert.False(File.Exists(deletePath));
    }

    [Fact]
    public async Task Apply_patch_tool_rejects_invalid_patch_without_writing()
    {
        var path = Path.Combine(_root, "note.txt");
        await File.WriteAllTextAsync(path, "hello");
        var tool = new ApplyPatchTool(new EditPreviewService(), new EditApplier(new FakeLspClient()));
        var request = new ToolExecutionRequest(
            "apply_patch",
            new JsonObject
            {
                ["edits"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["path"] = path,
                        ["operation"] = "patch",
                        ["hunks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["oldText"] = "missing",
                                ["newText"] = "hi"
                            }
                        }
                    }
                }
            });

        var preview = await tool.PreviewAsync(request, CancellationToken.None);
        var result = await tool.ApplyAsync(request, CancellationToken.None);

        Assert.False(preview.Success);
        Assert.False(result.Success);
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
