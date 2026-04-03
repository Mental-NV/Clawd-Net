using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class ProviderCommandHandler : ICommandHandler
{
    public string Name => "provider";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count >= 2
            && string.Equals(request.Arguments[0], "provider", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var action = request.Arguments[1];
        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
        {
            var providers = await context.ProviderCatalog.ListAsync(cancellationToken);
            var defaultProvider = await context.ProviderCatalog.ResolveAsync(null, cancellationToken);
            var lines = providers.Select(provider =>
                $"{(string.Equals(provider.Name, defaultProvider.Name, StringComparison.OrdinalIgnoreCase) ? "*" : "-")} {provider.Name} | kind={provider.Kind} | enabled={provider.Enabled} | defaultModel={provider.DefaultModel ?? "(none)"}");
            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }

        if (string.Equals(action, "show", StringComparison.OrdinalIgnoreCase) && request.Arguments.Count >= 3)
        {
            var provider = await context.ProviderCatalog.GetAsync(request.Arguments[2], cancellationToken);
            if (provider is null)
            {
                return CommandExecutionResult.Failure($"Provider '{request.Arguments[2]}' was not found.", 3);
            }

            var output = string.Join(
                Environment.NewLine,
                [
                    $"Provider: {provider.Name}",
                    $"Kind: {provider.Kind}",
                    $"Enabled: {provider.Enabled}",
                    $"ApiKeyEnv: {provider.ApiKeyEnvironmentVariable}",
                    $"BaseUrl: {provider.BaseUrl ?? "(default)"}",
                    $"DefaultModel: {provider.DefaultModel ?? "(none)"}"
                ]);
            return CommandExecutionResult.Success(output);
        }

        return CommandExecutionResult.Failure("Supported provider commands: provider list, provider show <name>.");
    }
}
