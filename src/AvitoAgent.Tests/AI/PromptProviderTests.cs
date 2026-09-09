using AvitoAgent.AI;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvitoAgent.Tests.AI;

public sealed class PromptProviderTests
{
    [Fact]
    public void GetAuthenticityPrompt_reads_file_and_prefixes_nothink()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-prompt-" + Guid.NewGuid());
        try
        {
            var paths = new ApplicationPaths(root);
            Directory.CreateDirectory(paths.Prompts);
            File.WriteAllText(
                Path.Combine(paths.Prompts, PromptProvider.AuthenticityFileName),
                "Проверь подлинность"
            );

            var provider = new PromptProvider(paths, NullLogger<PromptProvider>.Instance);
            var prompt = provider.GetAuthenticityPrompt();
            Assert.StartsWith(QwenThinking.NoThinkTag, prompt);
            Assert.Contains("Проверь подлинность", prompt);
            Assert.Equal(prompt, provider.GetAuthenticityPrompt());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Missing_prompt_throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-prompt-" + Guid.NewGuid());
        try
        {
            var paths = new ApplicationPaths(root);
            var provider = new PromptProvider(paths, NullLogger<PromptProvider>.Instance);
            Assert.Throws<FileNotFoundException>(() => provider.GetAuthenticityPrompt());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
