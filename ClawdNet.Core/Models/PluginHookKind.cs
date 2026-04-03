namespace ClawdNet.Core.Models;

public enum PluginHookKind
{
    /// <summary>Fires before a query is sent to the model.</summary>
    BeforeQuery,
    /// <summary>Fires after a query response is received.</summary>
    AfterQuery,
    /// <summary>Fires after a tool execution completes.</summary>
    AfterToolResult,
    /// <summary>Fires after a task completes.</summary>
    AfterTaskCompletion,
    /// <summary>Fires before a tool is executed (can intercept or modify tool calls).</summary>
    BeforeToolCall,
    /// <summary>Fires after a tool completes execution (can observe results).</summary>
    AfterToolCall,
    /// <summary>Fires when the application starts up.</summary>
    OnStartup,
    /// <summary>Fires when the application is shutting down.</summary>
    OnShutdown,
    /// <summary>Fires when a new conversation session is created.</summary>
    OnSessionCreated
}
