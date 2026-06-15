namespace MouseGesture.Core.Actions;

/// <summary>Catalog of pre-defined Windows shortcuts commonly bound to gestures.</summary>
public static class BuiltInActions
{
    public static IGestureAction NextDesktop { get; } =
        new KeyComboAction("NextDesktop", "다음 가상 데스크탑", VirtualKey.Right, VirtualKey.LControl, VirtualKey.LWin);

    public static IGestureAction PreviousDesktop { get; } =
        new KeyComboAction("PreviousDesktop", "이전 가상 데스크탑", VirtualKey.Left, VirtualKey.LControl, VirtualKey.LWin);

    public static IGestureAction TaskView { get; } =
        new KeyComboAction("TaskView", "작업 보기", VirtualKey.Tab, VirtualKey.LWin);

    public static IGestureAction ShowDesktop { get; } =
        new KeyComboAction("ShowDesktop", "바탕 화면 보기", VirtualKey.D, VirtualKey.LWin);

    public static IGestureAction MinimizeWindow { get; } =
        new KeyComboAction("MinimizeWindow", "현재 창 최소화", VirtualKey.Down, VirtualKey.LWin);

    public static IGestureAction MaximizeWindow { get; } =
        new KeyComboAction("MaximizeWindow", "현재 창 최대화", VirtualKey.Up, VirtualKey.LWin);

    public static IGestureAction CloseWindow { get; } =
        new KeyComboAction("CloseWindow", "현재 창 닫기", VirtualKey.F4, VirtualKey.LAlt);

    public static IGestureAction BrowserBack { get; } =
        new KeyComboAction("BrowserBack", "뒤로 가기", VirtualKey.BrowserBack);

    public static IGestureAction BrowserForward { get; } =
        new KeyComboAction("BrowserForward", "앞으로 가기", VirtualKey.BrowserForward);

    public static IGestureAction Refresh { get; } =
        new KeyComboAction("Refresh", "새로 고침", VirtualKey.F5);

    public static IGestureAction NewTab { get; } =
        new KeyComboAction("NewTab", "새 탭", VirtualKey.T, VirtualKey.LControl);

    public static IGestureAction CloseTab { get; } =
        new KeyComboAction("CloseTab", "탭 닫기", VirtualKey.W, VirtualKey.LControl);

    public static IGestureAction Copy { get; } =
        new KeyComboAction("Copy", "복사", VirtualKey.C, VirtualKey.LControl);

    public static IGestureAction Paste { get; } =
        new KeyComboAction("Paste", "붙여넣기", VirtualKey.V, VirtualKey.LControl);

    public static IGestureAction MediaPlayPause { get; } =
        new KeyComboAction("MediaPlayPause", "재생/일시정지", VirtualKey.MediaPlayPause);

    public static IGestureAction MediaNext { get; } =
        new KeyComboAction("MediaNext", "다음 트랙", VirtualKey.MediaNextTrack);

    public static IGestureAction MediaPrev { get; } =
        new KeyComboAction("MediaPrev", "이전 트랙", VirtualKey.MediaPrevTrack);

    public static IGestureAction VolumeUp { get; } =
        new KeyComboAction("VolumeUp", "볼륨 올리기", VirtualKey.VolumeUp);

    public static IGestureAction VolumeDown { get; } =
        new KeyComboAction("VolumeDown", "볼륨 내리기", VirtualKey.VolumeDown);

    public static IGestureAction VolumeMute { get; } =
        new KeyComboAction("VolumeMute", "음소거", VirtualKey.VolumeMute);

    public static IReadOnlyList<IGestureAction> All { get; } =
    [
        NextDesktop,
        PreviousDesktop,
        TaskView,
        ShowDesktop,
        MinimizeWindow,
        MaximizeWindow,
        CloseWindow,
        BrowserBack,
        BrowserForward,
        Refresh,
        NewTab,
        CloseTab,
        Copy,
        Paste,
        MediaPlayPause,
        MediaNext,
        MediaPrev,
        VolumeUp,
        VolumeDown,
        VolumeMute,
    ];
}
