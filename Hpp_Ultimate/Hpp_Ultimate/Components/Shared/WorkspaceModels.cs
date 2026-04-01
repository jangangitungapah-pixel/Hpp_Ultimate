namespace Hpp_Ultimate.Components.Shared;

public sealed record WorkspaceMetric(string Label, string Value, string Note);

public sealed record WorkspaceAction(string Label, string Detail);

public sealed record WorkspacePanel(
    string Eyebrow,
    string Title,
    string Description,
    IReadOnlyList<string> Points,
    string Badge = "",
    string Tone = "default");
