using ViberAttendance.Core.Models;

namespace ViberAttendance.App.ViewModels;

public sealed class DiagnosticTreeNodeViewModel
{
    public DiagnosticTreeNodeViewModel(UiAutomationNode node)
    {
        Node = node;
        Children = node.Children.Select(child => new DiagnosticTreeNodeViewModel(child)).ToArray();
    }

    public UiAutomationNode Node { get; }
    public IReadOnlyList<DiagnosticTreeNodeViewModel> Children { get; }

    public string DisplayLabel
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Node.Name) ? "<无名称>" : Truncate(Node.Name, 80);
            var automationId = string.IsNullOrWhiteSpace(Node.AutomationId) ? "-" : Node.AutomationId;
            return $"{Node.Path} · {Node.ControlType} · {name} · AutomationId={automationId} · Offscreen={Node.IsOffscreen}";
        }
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length] + "…";
}

