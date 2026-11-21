using Bureau.Common.Help;

namespace ClickToPDF.App.Tests;

// HelpTopics.Load quietly leaves out a topic whose Title or Body string is missing, so a mistyped
// key in HelpTopicKeys or the resw would drop a page from Help with no error. This loads the keys
// against the real resw and expects every topic back.
public class HelpTopicKeysTests
{
    // MRT Core's GetString returns "" for a key it can't find; the lookup does the same.
    private static string GetString(string key) => AppResw.EnglishStrings.GetValueOrDefault(key, "");

    [Fact]
    public void EveryTopicKey_HasATitleAndABody()
    {
        IReadOnlyList<HelpTopic> topics = HelpTopics.Load(GetString, HelpTopicKeys.InOrder);

        Assert.Equal(HelpTopicKeys.InOrder.Count, topics.Count);
    }

    [Fact]
    public void TopicTitles_AreDistinct()
    {
        IReadOnlyList<HelpTopic> topics = HelpTopics.Load(GetString, HelpTopicKeys.InOrder);

        Assert.Equal(topics.Count, topics.Select(t => t.Title).Distinct().Count());
    }

    // The Help window draws a shortcut as key caps only when its line is "keys | action".
    [Theory]
    [InlineData("F1 | Open Help")]
    [InlineData("Enter | Save")]
    [InlineData("Ctrl+Z | Undo")]
    public void ShortcutsTopic_WritesShortcutsAsTableRows(string row)
    {
        string[] lines = GetString("HelpShortcutsBody").ReplaceLineEndings("\n").Split('\n');

        Assert.Contains(row, lines);
    }
}
