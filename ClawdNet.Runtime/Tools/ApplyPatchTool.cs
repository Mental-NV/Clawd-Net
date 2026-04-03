using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Editing;

namespace ClawdNet.Runtime.Tools;

public sealed class ApplyPatchTool : IReviewableEditTool
{
    private readonly IEditPreviewService _previewService;
    private readonly IEditApplier _editApplier;

    public ApplyPatchTool(IEditPreviewService previewService, IEditApplier editApplier)
    {
        _previewService = previewService;
        _editApplier = editApplier;
    }

    public string Name => "apply_patch";

    public string Description => "Apply a structured batch of file edits with preview and approval.";

    public ToolCategory Category => ToolCategory.Write;

    public bool RequiresEditReview => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["edits"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string" },
                        ["operation"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("patch", "create", "delete") },
                        ["content"] = new JsonObject { ["type"] = "string" },
                        ["hunks"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["oldText"] = new JsonObject { ["type"] = "string" },
                                    ["newText"] = new JsonObject { ["type"] = "string" }
                                },
                                ["required"] = new JsonArray("oldText", "newText")
                            }
                        }
                    },
                    ["required"] = new JsonArray("path", "operation")
                }
            }
        },
        ["required"] = new JsonArray("edits")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var result = await ApplyAsync(request, cancellationToken);
        return result.Success
            ? new ToolExecutionResult(true, $"{result.Summary}{Environment.NewLine}{result.Diff}".TrimEnd())
            : new ToolExecutionResult(false, string.Empty, result.Error ?? result.Summary);
    }

    public async Task<EditPreview> PreviewAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var parsed = ParseBatch(request.Input);
        if (!parsed.Success)
        {
            return new EditPreview(false, new EditBatch([]), 0, "Edit batch is invalid.", string.Empty, parsed.Error);
        }

        return await _previewService.PreviewAsync(parsed.Batch!, cancellationToken);
    }

    public async Task<EditApplyResult> ApplyAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var parsed = ParseBatch(request.Input);
        if (!parsed.Success)
        {
            return new EditApplyResult(false, new EditBatch([]), 0, "Edit batch is invalid.", string.Empty, parsed.Error);
        }

        return await _editApplier.ApplyAsync(parsed.Batch!, cancellationToken);
    }

    private static ParseBatchResult ParseBatch(JsonNode? input)
    {
        var editsNode = input?["edits"] as JsonArray;
        if (editsNode is null)
        {
            return ParseBatchResult.Failure("apply_patch requires an 'edits' array.");
        }

        var edits = new List<FileEdit>();
        foreach (var editNode in editsNode)
        {
            var path = editNode?["path"]?.GetValue<string>();
            var operationText = editNode?["operation"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(operationText))
            {
                return ParseBatchResult.Failure("Each edit requires 'path' and 'operation'.");
            }

            if (!TryParseOperation(operationText, out var operation))
            {
                return ParseBatchResult.Failure($"Unknown edit operation '{operationText}'.");
            }

            switch (operation)
            {
                case EditOperation.Patch:
                {
                    var hunksNode = editNode?["hunks"] as JsonArray;
                    if (hunksNode is null || hunksNode.Count == 0)
                    {
                        return ParseBatchResult.Failure($"Patch edit for '{path}' requires a non-empty 'hunks' array.");
                    }

                    var hunks = new List<EditHunk>();
                    foreach (var hunkNode in hunksNode)
                    {
                        var oldText = hunkNode?["oldText"]?.GetValue<string>();
                        var newText = hunkNode?["newText"]?.GetValue<string>();
                        if (oldText is null || newText is null)
                        {
                            return ParseBatchResult.Failure($"Patch hunks for '{path}' require 'oldText' and 'newText'.");
                        }

                        hunks.Add(new EditHunk(oldText, newText));
                    }

                    edits.Add(new FileEdit(path, operation, hunks));
                    break;
                }
                case EditOperation.Create:
                {
                    var content = editNode?["content"]?.GetValue<string>();
                    if (content is null)
                    {
                        return ParseBatchResult.Failure($"Create edit for '{path}' requires 'content'.");
                    }

                    edits.Add(new FileEdit(path, operation, null, content));
                    break;
                }
                case EditOperation.Delete:
                    edits.Add(new FileEdit(path, operation));
                    break;
            }
        }

        return ParseBatchResult.FromBatch(new EditBatch(edits));
    }

    private static bool TryParseOperation(string value, out EditOperation operation)
    {
        operation = value.ToLowerInvariant() switch
        {
            "patch" => EditOperation.Patch,
            "create" => EditOperation.Create,
            "delete" => EditOperation.Delete,
            _ => default
        };
        return value is "patch" or "create" or "delete";
    }

    private sealed record ParseBatchResult(bool Success, EditBatch? Batch, string? Error)
    {
        public static ParseBatchResult FromBatch(EditBatch batch) => new(true, batch, null);

        public static ParseBatchResult Failure(string error) => new(false, null, error);
    }
}
