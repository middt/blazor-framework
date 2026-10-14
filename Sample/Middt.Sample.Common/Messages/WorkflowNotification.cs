namespace Middt.Sample.Common.Messages;

/// <summary>Published by the workflow <c>publish</c> task (e.g. <c>OrderApproved</c>); <see cref="Payload"/> is JSON.</summary>
public sealed record WorkflowNotification(string Type, string Payload);
