using BirthdayBot.Infrastructure.Services;

namespace BirthdayBot.Api.Endpoints;

/// <summary>
/// Endpoints for viewing messages sent by MockTelegramBotClient in development mode
/// </summary>
public static class MockMessagesEndpoints
{
    public static IEndpointRouteBuilder MapMockMessagesEndpoints(this IEndpointRouteBuilder app)
    {
        // Get all sent messages
        app.MapGet("/api/mock/messages", (long? chatId) =>
        {
            var messages = MockTelegramBotClient.GetSentMessages(chatId);
            return Results.Ok(new
            {
                count = messages.Count,
                messages = messages.Select(m => new
                {
                    chatId = m.ChatId,
                    text = m.Text,
                    parseMode = m.ParseMode,
                    replyToMessageId = m.ReplyToMessageId,
                    replyMarkup = m.ReplyMarkup,
                    timestamp = m.Timestamp
                })
            });
        })
        .WithName("GetMockMessages")
        .WithTags("Mock")
        .Produces(200);

        // Clear all messages
        app.MapDelete("/api/mock/messages", () =>
        {
            MockTelegramBotClient.ClearMessages();
            return Results.Ok(new { message = "Messages cleared" });
        })
        .WithName("ClearMockMessages")
        .WithTags("Mock")
        .Produces(200);

        // Get messages for specific chat
        app.MapGet("/api/mock/messages/{chatId:long}", (long chatId) =>
        {
            var messages = MockTelegramBotClient.GetSentMessages(chatId);
            return Results.Ok(new
            {
                chatId = chatId,
                count = messages.Count,
                messages = messages.Select(m => new
                {
                    text = m.Text,
                    parseMode = m.ParseMode,
                    replyToMessageId = m.ReplyToMessageId,
                    replyMarkup = m.ReplyMarkup,
                    timestamp = m.Timestamp
                })
            });
        })
        .WithName("GetMockMessagesByChat")
        .WithTags("Mock")
        .Produces(200);

        return app;
    }
}

