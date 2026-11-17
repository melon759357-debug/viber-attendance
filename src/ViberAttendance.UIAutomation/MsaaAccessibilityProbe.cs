using System.Runtime.InteropServices;
using ViberAttendance.Core.Models;

namespace ViberAttendance.UIAutomation;

/// <summary>MSAA / IAccessible 备用探测（AccessibleObjectFromWindow + OBJID_CLIENT）。</summary>
internal static class MsaaAccessibilityProbe
{
    private const uint ObjIdClient = unchecked((uint)0xFFFFFFFC);

    private static readonly string[] ParticipantTokens =
    [
        "participant", "participants", "member", "members", "成员", "参与者", "参会者"
    ];

    private static readonly string[] IgnoredTokens =
    [
        "mute", "unmute", "camera", "microphone", "hang up", "leave", "invite",
        "静音", "麦克风", "摄像头", "挂断", "离开", "邀请", "搜索", "search"
    ];

    internal static DiagnosticMsaaWindowResult Probe(long hwnd, int maxChildren = 80)
    {
        try
        {
            var handle = new nint(hwnd);
            var iid = typeof(IAccessible).GUID;
            var hr = AccessibleObjectFromWindow(handle, ObjIdClient, ref iid, out var accessibleObj);
            if (hr != 0 || accessibleObj is not IAccessible accessible)
            {
                return Failed(hwnd, $"AccessibleObjectFromWindow HRESULT=0x{hr:X8}");
            }

            var rootName = SafeGetName(accessible, 0);
            var rootRole = SafeGetRole(accessible, 0);
            var childCount = SafeGetChildCount(accessible);
            var names = new List<DiagnosticTextElement>();
            var limit = Math.Min(Math.Max(childCount, 0), maxChildren);
            for (var index = 1; index <= limit; index++)
            {
                var name = SafeGetName(accessible, index);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                names.Add(ToTextElement($"msaa/{index}", SafeGetRole(accessible, index) ?? "IAccessible", name));
            }

            var unique = names
                .GroupBy(item => item.RedactedName + ":" + item.NameLength, StringComparer.Ordinal)
                .Select(group => group.First())
                .Take(40)
                .ToArray();

            return new DiagnosticMsaaWindowResult(
                hwnd,
                true,
                rootName is null ? null : UserFacingStatuses.RedactDisplayName(rootName).DisplayLabel,
                rootRole,
                childCount,
                unique.Length,
                unique.Any(item => item.LooksLikeParticipantName),
                unique,
                null);
        }
        catch (Exception exception)
        {
            return Failed(hwnd, exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static DiagnosticTextElement ToTextElement(string path, string controlType, string name)
    {
        var redacted = UserFacingStatuses.RedactDisplayName(name);
        var looksLike = LooksLikePersonName(name);
        var reason = looksLike
            ? "接受：疑似参与者姓名"
            : IgnoredTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase))
                ? "排除：控件/操作标签"
                : "排除：不像姓名";
        return new DiagnosticTextElement(
            path,
            controlType,
            redacted.Masked,
            redacted.Length,
            string.Empty,
            looksLike,
            reason);
    }

    private static bool LooksLikePersonName(string value)
    {
        var text = value.Trim();
        if (text.Length is < 2 or > 40)
        {
            return false;
        }

        if (ParticipantTokens.Any(token => text.Equals(token, StringComparison.OrdinalIgnoreCase)) ||
            IgnoredTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return text.Any(char.IsLetter);
    }

    private static DiagnosticMsaaWindowResult Failed(long hwnd, string error) =>
        new(hwnd, false, null, null, 0, 0, false, [], error);

    private static string? SafeGetName(IAccessible accessible, int childId)
    {
        try
        {
            object child = childId;
            return accessible.get_accName(child);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetRole(IAccessible accessible, int childId)
    {
        try
        {
            object child = childId;
            return accessible.get_accRole(child)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int SafeGetChildCount(IAccessible accessible)
    {
        try
        {
            return accessible.accChildCount;
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object? accessible);

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        // IDispatch placeholders to keep vtable layout for dual interface usage via late-bound style getters below.
        void GetTypeInfoCount(out uint pctinfo);
        void GetTypeInfo(uint iTInfo, uint lcid, out nint ppTInfo);
        void GetIDsOfNames(ref Guid riid, nint rgszNames, uint cNames, uint lcid, nint rgDispId);
        void Invoke(int dispIdMember, ref Guid riid, uint lcid, short wFlags, nint pDispParams, nint pVarResult, nint pExcepInfo, nint puArgErr);

        [DispId(-5000)]
        object? accParent { get; }

        [DispId(-5001)]
        int accChildCount { get; }

        [DispId(-5002)]
        object? get_accChild(object varChild);

        [DispId(-5003)]
        string? get_accName(object varChild);

        [DispId(-5004)]
        string? get_accValue(object varChild);

        [DispId(-5005)]
        string? get_accDescription(object varChild);

        [DispId(-5006)]
        object? get_accRole(object varChild);

        [DispId(-5007)]
        object? get_accState(object varChild);
    }
}
