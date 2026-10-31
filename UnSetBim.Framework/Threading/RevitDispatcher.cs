using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using UnSetBim.Core.Diagnostics;

namespace UnSetBim.Framework.Threading;

/// <summary>
/// Runs code inside a valid Revit API context from anywhere (WPF windows, background tasks).
///
/// Revit only allows document changes on its main thread during an API call. Modeless windows
/// are outside that context, so they queue the work here; Revit executes it on the next idle
/// cycle through a single <see cref="ExternalEvent"/>.
///
/// <code>await host.Revit.InvokeAsync(app => LoadFamily(app.ActiveUIDocument, path));</code>
/// </summary>
public sealed class RevitDispatcher : IExternalEventHandler, IDisposable
{
    private readonly ConcurrentQueue<Action<UIApplication>> _queue = new();
    private ExternalEvent? _event;

    private RevitDispatcher() { }

    /// <summary>Must be called in a valid API context (e.g. IExternalApplication.OnStartup).</summary>
    public static RevitDispatcher Create()
    {
        var dispatcher = new RevitDispatcher();
        dispatcher._event = ExternalEvent.Create(dispatcher);
        return dispatcher;
    }

    public Task InvokeAsync(Action<UIApplication> action) =>
        InvokeAsync<object?>(app =>
        {
            action(app);
            return null;
        });

    public Task<T> InvokeAsync<T>(Func<UIApplication, T> func)
    {
        // Continuations go back to the caller's context (the WPF dispatcher), not inside the Revit call.
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.Enqueue(app =>
        {
            // Already failed (Raise denied): never run it late.
            if (tcs.Task.IsCompleted)
                return;

            try
            {
                tcs.TrySetResult(func(app));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        // Accepted or Pending (an earlier Raise is still waiting) both mean the queue will be drained.
        var request = _event?.Raise() ?? ExternalEventRequest.Denied;
        if (request is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)
        {
            Log.Warn($"ExternalEvent.Raise answered {request}.");
            tcs.TrySetException(new InvalidOperationException("O Revit não aceitou a operação agora. Tente novamente."));
        }

        return tcs.Task;
    }

    void IExternalEventHandler.Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var work))
        {
            try
            {
                work(app);
            }
            catch (Exception ex)
            {
                Log.Error("Error running queued Revit work.", ex);
            }
        }
    }

    string IExternalEventHandler.GetName() => "UnSet BIM dispatcher";

    public void Dispose()
    {
        _event?.Dispose();
        _event = null;
    }
}
