namespace Middt.Sample.Common.Messages;

/// <summary>Published by the API after a category was inserted.</summary>
public sealed record CategoryCreated(int CategoryId, string CategoryName, DateTimeOffset CreatedAt);
