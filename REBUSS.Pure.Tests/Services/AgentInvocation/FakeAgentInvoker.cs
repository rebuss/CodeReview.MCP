using REBUSS.Pure.Core.Services.AgentInvocation;

namespace REBUSS.Pure.Tests.Services.AgentInvocation;

/// <summary>
/// Scripted <see cref="IAgentInvoker"/> for unit tests. Consumers set up a queue of
/// responses (or exceptions) to return from successive <c>InvokeAsync</c> calls and
/// inspect recorded invocation state afterwards.
/// </summary>
/// <remarks>
/// <para>Either <see cref="Enqueue"/> or <see cref="EnqueueException"/> may be called
/// multiple times to script multi-call scenarios (e.g. retry loops). If both are left
/// empty, <c>InvokeAsync</c> throws <see cref="InvalidOperationException"/> so the
/// test author notices the unprepared call immediately.</para>
/// <para>Callers needing fully dynamic behaviour (e.g. different responses keyed on
/// prompt content) can pass <see cref="OnInvoke"/> — when set, the queue is ignored.</para>
/// </remarks>
internal sealed class FakeAgentInvoker : IAgentInvoker
{
    private readonly Queue<Func<string, string?, CancellationToken, Task<string>>> _scripted = new();
    private readonly List<(string Prompt, string? Model)> _received = new();

    /// <summary>Number of times <see cref="InvokeAsync"/> has been called.</summary>
    public int InvocationCount => _received.Count;

    /// <summary>Prompt supplied to the most recent invocation, or <c>null</c> if never called.</summary>
    public string? LastPrompt => _received.Count == 0 ? null : _received[^1].Prompt;

    /// <summary>Model supplied to the most recent invocation, or <c>null</c>.</summary>
    public string? LastModel => _received.Count == 0 ? null : _received[^1].Model;

    /// <summary>All prompts in call order — useful for retry-loop assertions.</summary>
    public IReadOnlyList<string> ReceivedPrompts => _received.Select(x => x.Prompt).ToList();

    /// <summary>Dynamic hook. When non-null, overrides the scripted queue for every call.</summary>
    public Func<string, string?, CancellationToken, Task<string>>? OnInvoke { get; set; }

    /// <summary>Queues a successful response for the next <c>InvokeAsync</c> call.</summary>
    public void Enqueue(string response)
    {
        _scripted.Enqueue((_, _, _) => Task.FromResult(response));
    }

    /// <summary>Queues an exception for the next <c>InvokeAsync</c> call.</summary>
    public void EnqueueException(Exception ex)
    {
        _scripted.Enqueue((_, _, _) => Task.FromException<string>(ex));
    }

    public Task<string> InvokeAsync(string prompt, string? model, CancellationToken cancellationToken)
    {
        _received.Add((prompt, model));

        if (OnInvoke is not null)
            return OnInvoke(prompt, model, cancellationToken);

        if (_scripted.Count == 0)
            throw new InvalidOperationException(
                "FakeAgentInvoker was called but no response was enqueued. " +
                "Call Enqueue / EnqueueException before the test exercises the code under test.");

        var next = _scripted.Dequeue();
        return next(prompt, model, cancellationToken);
    }
}
