using UnSetBim.Core.Api;
using UnSetBim.Core.Diagnostics;
using UnSetBim.Framework.Dialogs;
using UnSetBim.Framework.Hosting;
using UnSetBim.Framework.Modules;

namespace UnSetBim.Framework.Account;

/// <summary>Sign-in and plan checks used by <see cref="Commands.UnSetCommand"/>. Runs on the Revit thread.</summary>
public static class AccountGate
{
    private static readonly TimeSpan BlockingTimeout = TimeSpan.FromSeconds(20);

    /// <summary>True when signed in, after validating a stored token or showing the sign-in window.</summary>
    public static bool EnsureSignedIn(UnSetHost host)
    {
        var session = host.Session;

        if (session.IsSignedIn)
            return true;

        // A token from a previous session not yet confirmed (e.g. Revit started offline).
        if (session.HasStoredToken)
        {
            try
            {
                if (RunBlocking(() => session.RestoreAsync()))
                    return true;
            }
            catch (Exception ex) when (ex is ApiConnectionException or TimeoutException)
            {
                MessageDialog.Show("Sem conexão", ApiConnectionException.DefaultMessage, DialogKind.Warning);
                return false;
            }
            catch (ApiException ex)
            {
                MessageDialog.Show("Não foi possível validar sua conta", ex.UserMessage, DialogKind.Warning);
                return false;
            }
        }

        return SignInWindow.ShowSignIn(host);
    }

    /// <summary>
    /// True when the plan includes <paramref name="moduleKey"/>. Refreshes the access block once
    /// before denying (the user may have just subscribed on the site).
    /// </summary>
    public static bool EnsurePlanModule(UnSetHost host, string moduleKey, string featureName)
    {
        if (host.Session.Access.HasModule(moduleKey))
            return true;

        try
        {
            RunBlocking(async () =>
            {
                await host.Session.RefreshAccessAsync();
                return true;
            });
        }
        catch (Exception ex)
        {
            Log.Info($"Access refresh before module check failed: {ex.Message}");
        }

        if (host.Session.Access.HasModule(moduleKey))
            return true;

        var plan = PlanModules.DisplayName(moduleKey);
        var choice = MessageDialog.Show(
            "Recurso não incluído no seu plano",
            $"{featureName} faz parte do módulo {plan}. Assine ou altere seu plano no site para liberar.",
            DialogKind.Info,
            "Ver planos", "Agora não");

        if (choice == 0)
            Browser.Open(host.Links.Plans);

        return false;
    }

    private static bool RunBlocking(Func<Task<bool>> work)
    {
        // Short blocking wait on the Revit thread (the work itself runs on the thread pool).
        // WaitHandle instead of Task.Wait so a faulted task rethrows its original exception below.
        var task = Task.Run(work);
        if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(BlockingTimeout))
            throw new TimeoutException();

        return task.GetAwaiter().GetResult();
    }
}
