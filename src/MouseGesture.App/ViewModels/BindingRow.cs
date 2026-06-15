using CommunityToolkit.Mvvm.Input;
using MouseGesture.Core.Recognition;

namespace MouseGesture.App.ViewModels;

public sealed class BindingRow
{
    public string Stroke { get; }
    public string StrokeArrows { get; }
    public string ActionId { get; }
    public string Description { get; }
    public IRelayCommand RemoveCommand { get; }

    public BindingRow(string stroke, string actionId, string description, Action<string> remove)
    {
        Stroke = stroke;
        StrokeArrows = DirectionExtensions.StrokeToArrows(stroke);
        ActionId = actionId;
        Description = description;
        RemoveCommand = new RelayCommand(() => remove(stroke));
    }
}
