namespace ClickToPDF.App;

// The Help topics, in the order the Help window lists them. Each key names two resw strings,
// <Key>Title and <Key>Body. Kept apart from MainWindow so HelpTopicKeysTests can read the keys
// without starting WinUI.
public static class HelpTopicKeys
{
    public static readonly IReadOnlyList<string> InOrder =
    [
        "HelpGettingStarted",
        "HelpFormats",
        "HelpArranging",
        "HelpPreview",
        "HelpAdding",
        "HelpUndo",
        "HelpSaving",
        "HelpSaveOptions",
        "HelpSearchable",
        "HelpShortcuts",
        "HelpPrivacy",
        "HelpTroubleshooting",
    ];
}
