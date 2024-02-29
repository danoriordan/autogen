﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Example05_Dalle_And_GPT4V.cs

using AutoGen;
using AutoGen.OpenAI;
using Azure.AI.OpenAI;
using FluentAssertions;
using autogen = AutoGen.Core.API.LLMConfigAPI;

public partial class Example05_Dalle_And_GPT4V
{
    private readonly OpenAIClient openAIClient;

    public Example05_Dalle_And_GPT4V(OpenAIClient openAIClient)
    {
        this.openAIClient = openAIClient;
    }

    /// <summary>
    /// Generate image from prompt using DALL-E.
    /// </summary>
    /// <param name="prompt">prompt with feedback</param>
    /// <returns></returns>
    [FunctionAttribute]
    public async Task<string> GenerateImage(string prompt)
    {
        // TODO
        // generate image from prompt using DALL-E
        // and return url.
        var option = new ImageGenerationOptions
        {
            Size = ImageSize.Size1024x1024,
            Style = ImageGenerationStyle.Vivid,
            ImageCount = 1,
            Prompt = prompt,
            Quality = ImageGenerationQuality.Standard,
            DeploymentName = "dall-e-3",
        };

        var imageResponse = await openAIClient.GetImageGenerationsAsync(option);
        var imageUrl = imageResponse.Value.Data.First().Url.OriginalString;

        return $@"// ignore this line [IMAGE_GENERATION]
The image is generated from prompt {prompt}

{imageUrl}";
    }

    public static async Task RunAsync()
    {
        // This example shows how to use DALL-E and GPT-4V to generate image from prompt and feedback.
        // The DALL-E agent will generate image from prompt.
        // The GPT-4V agent will provide feedback to DALL-E agent to help it generate better image.
        // The conversation will be terminated when the image satisfies the condition.
        // The image will be saved to image.jpg in current directory.

        // get OpenAI Key and create config
        var openAIKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("Please set OPENAI_API_KEY environment variable.");
        var gpt35Config = autogen.GetOpenAIConfigList(openAIKey, new[] { "gpt-3.5-turbo" });
        var gpt4vConfig = autogen.GetOpenAIConfigList(openAIKey, new[] { "gpt-4-vision-preview" });
        var openAIClient = new OpenAIClient(openAIKey);
        var instance = new Example05_Dalle_And_GPT4V(openAIClient);
        var imagePath = Path.Combine(Environment.CurrentDirectory, "image.jpg");
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        var dalleAgent = new AssistantAgent(
            name: "dalle",
            systemMessage: "You are a DALL-E agent that generate image from prompt, when conversation is terminated, return the most recent image url",
            llmConfig: new ConversableAgentConfig
            {
                Temperature = 0,
                ConfigList = gpt35Config,
                FunctionDefinitions = new[]
                {
                    instance.GenerateImageFunction,
                },
            },
            functionMap: new Dictionary<string, Func<string, Task<string>>>
            {
                { nameof(GenerateImage), instance.GenerateImageWrapper },
            })
            .RegisterReply(async (msgs, ct) =>
            {
                // if last message contains [TERMINATE], then find the last image url and terminate the conversation
                if (msgs.Last().Content?.Contains("TERMINATE") is true)
                {
                    var lastMessageWithImage = msgs.Last(msg => msg.Content?.Contains("IMAGE_GENERATION") is true);
                    var lastImageUrl = lastMessageWithImage.Content!.Split("\n").Last();
                    Console.WriteLine($"download image from {lastImageUrl} to {imagePath}");
                    var httpClient = new HttpClient();
                    var imageBytes = await httpClient.GetByteArrayAsync(lastImageUrl);
                    File.WriteAllBytes(imagePath, imageBytes);

                    var messageContent = $@"{GroupChatExtension.TERMINATE}

{lastImageUrl}";
                    return new Message(Role.Assistant, messageContent)
                    {
                        From = "dalle",
                    };
                }

                return null;
            })
            .RegisterPrintFormatMessageHook();

        var gpt4VAgent = new AssistantAgent(
            name: "gpt4v",
            systemMessage: @"You are a critism that provide feedback to DALL-E agent.
Carefully check the image generated by DALL-E agent and provide feedback.
If the image satisfies the condition, then terminate the conversation by saying [TERMINATE].
Otherwise, provide detailed feedback to DALL-E agent so it can generate better image.

The image should satisfy the following conditions:
- There should be a cat and a mouse in the image
- The cat should be chasing after the mouse
",
            llmConfig: new ConversableAgentConfig
            {
                Temperature = 0,
                ConfigList = gpt4vConfig,
            }).RegisterReply(async (msgs, ct) =>
            {
                // if no image is generated, then ask DALL-E agent to generate image
                if (msgs.Last().Content?.Contains("IMAGE_GENERATION") is false)
                {
                    return new Message(Role.Assistant, "Hey dalle, please generate image")
                    {
                        From = "gpt4v",
                    };
                }

                return null;
            })
            .RegisterPreProcess(async (msgs, ct) =>
            {
                // add image url to message metadata so it can be recognized by GPT-4V
                return msgs.Select(msg =>
                {
                    if (msg.Content?.Contains("IMAGE_GENERATION") is true)
                    {
                        var imageUrl = msg.Content.Split("\n").Last();
                        var imageMessageItem = new ChatMessageImageContentItem(new Uri(imageUrl));
                        var gpt4VMessage = new ChatRequestUserMessage(imageMessageItem);
                        var message = gpt4VMessage.ToMessage();
                        message.From = msg.From;

                        return message;
                    }
                    else
                    {
                        return msg;
                    }
                });
            }).RegisterPrintFormatMessageHook();

        IEnumerable<Message> conversation = new List<Message>()
        {
            new Message(Role.User, "Hey dalle, please generate image from prompt: English short hair blue cat chase after a mouse")
        };
        var maxRound = 20;
        await gpt4VAgent.InitiateChatAsync(
            receiver: dalleAgent,
            message: "Hey dalle, please generate image from prompt: English short hair blue cat chase after a mouse",
            maxRound: maxRound);

        File.Exists(imagePath).Should().BeTrue();
    }
}