namespace MouseGesture.Core.Actions;

public sealed class ActionRegistry
{
    private readonly Dictionary<string, IGestureAction> _byId = new();

    public ActionRegistry()
    {
        foreach (var action in BuiltInActions.All)
            _byId[action.Id] = action;
    }

    public IGestureAction? Get(string id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<IGestureAction> AllActions => _byId.Values.ToArray();
}
