using Middt.Sample.Common.Messages;
using Rebus.Handlers;

namespace Middt.Sample.Api.Handlers;

/// <summary>Demo subscriber: reacts to <see cref="CategoryCreated"/> events (e.g. cache invalidation, notifications).</summary>
public sealed partial class CategoryCreatedHandler(ILogger<CategoryCreatedHandler> logger) : IHandleMessages<CategoryCreated>
{
    public Task Handle(CategoryCreated message)
    {
        LogCategoryCreated(logger, message.CategoryId, message.CategoryName);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CategoryCreated event handled: {CategoryId} {CategoryName}")]
    private static partial void LogCategoryCreated(ILogger logger, int categoryId, string categoryName);
}
