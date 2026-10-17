using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Identity.Journeys;

/// <summary>Allowlisted identity operations called by the general workflow operation task.</summary>
internal sealed class IdentityJourneyOperationDispatcher(IServiceProvider services) : IWorkflowOperationDispatcher
{
    private static readonly Dictionary<string, Type> Operations = new(StringComparer.Ordinal)
    {
        ["idp-verify-credentials"] = typeof(VerifyCredentialsOperation),
        ["idp-verify-totp"] = typeof(VerifyTotpOperation),
        ["idp-check-consent"] = typeof(CheckConsentOperation),
        ["idp-grant-consent"] = typeof(GrantConsentOperation),
        ["idp-create-user"] = typeof(CreateUserOperation),
        ["idp-send-email-code"] = typeof(SendEmailCodeOperation),
        ["idp-verify-email-code"] = typeof(VerifyEmailCodeOperation),
        ["idp-reset-password"] = typeof(ResetPasswordOperation),
        ["idp-totp-setup"] = typeof(TotpSetupOperation),
        ["idp-enable-totp"] = typeof(EnableTotpOperation),
    };

    public bool CanHandle(string operation) => Operations.ContainsKey(operation);

    public Task ExecuteAsync(string operation, WorkflowOperationContext context, CancellationToken cancellationToken)
    {
        if (!Operations.TryGetValue(operation, out var type))
        {
            throw new InvalidOperationException($"Identity operation '{operation}' is not registered.");
        }

        var handler = (IIdentityJourneyOperation)ActivatorUtilities.CreateInstance(services, type);
        return handler.ExecuteAsync(context, cancellationToken);
    }
}
