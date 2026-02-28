namespace ApocalypseSnow;

public struct StateStruct
{
    public StateList Current;
    public StateList Old;

    public void Update()
    {
        Old = Current;
        Current = StateList.None;
    }
    
    public bool IsPressed(StateList action)
    {
        return Current.HasFlag(action);
    }
    
    public bool JustPressed(StateList action)
    {
        return Current.HasFlag(action) && !Old.HasFlag(action);
    }
    
    public bool JustReleased(StateList action)
    {
        return !Current.HasFlag(action) && Old.HasFlag(action);
    }
}