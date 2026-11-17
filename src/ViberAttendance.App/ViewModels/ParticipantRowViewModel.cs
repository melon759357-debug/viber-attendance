using System.Globalization;

namespace ViberAttendance.App.ViewModels;

public sealed record ParticipantRowViewModel(
    Guid MemberId,
    string DisplayName,
    string StableUiIdentity,
    string OnlineAt,
    string OfflineAt,
    bool IsOnline,
    string CurrentDuration,
    string TotalDuration,
    string StatusText)
{
    public string AvatarText => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : StringInfo.GetNextTextElement(DisplayName.Trim()).ToUpper(CultureInfo.CurrentCulture);
}
